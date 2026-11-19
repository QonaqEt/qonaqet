using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Application.Features.Verification.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Files;
using Qonaqet.Domain.Verification;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Verification.Commands.SubmitVerification;

/// <summary>
/// <c>POST verification</c> (multipart): istifadəçi təsdiq tələb edən kateqoriya üçün sorğu göndərir — alt növ, dinamik sahələr,
/// FIN və sənədlər. Bütün yoxlama kateqoriya sənədindən qurulur. Sənədlər (PDF/JPG/PNG, hər biri ≤5 MB) əvvəlcə hamısı yoxlanılır,
/// sonra sorğunun sahibliyi ilə saxlanılır. Xam FIN nə saxlanılır, nə loglanır: yalnız <see cref="IFinHasher"/> hash-i, və o,
/// (hash, kateqoriya) üzrə bir hesaba bağlanır (<c>identityHashes</c>).
/// </summary>
[RequiresActiveAccount]
public sealed record SubmitVerificationCommand(
    CategoryId CategoryId,
    string? Subtype,
    IReadOnlyDictionary<string, string>? Fields,
    string? Fin,
    IReadOnlyList<VerificationDocumentUpload> Files) : ICommand<VerificationRequestDto>
{
    /// <summary>FIN və fayl məzmunu heç bir log/izləmə mətninə düşməsin.</summary>
    public override string ToString() => $"{nameof(SubmitVerificationCommand)} {{ CategoryId = {CategoryId.Value}, Files = {Files?.Count ?? 0} }}";
}

/// <summary>Bir sənəd: klientin elan etdiyi tip və məzmun.</summary>
public sealed record VerificationDocumentUpload(string ContentType, ReadOnlyMemory<byte> Content);

public sealed class SubmitVerificationValidator : Validator<SubmitVerificationCommand>
{
    public SubmitVerificationValidator()
    {
        Rule("files", x => x.Files is not null && x.Files.Count <= VerificationConfig.MaxFilesLimit, "files_count_invalid",
            $"Ən çox {VerificationConfig.MaxFilesLimit} sənəd yükləmək olar.");
        Rule("subtype", x => x.Subtype is null || x.Subtype.Length <= CategoryRules.CodeMaxLength, ValidationCodes.TooLong, "Alt növ düzgün deyil.");
        Rule("fields", x => x.Fields is null || x.Fields.Count <= VerificationConfig.MaxFields, ValidationCodes.Invalid, "Sahələr düzgün deyil.");
    }
}

public sealed class SubmitVerificationHandler(
    ICategoryRepository categories,
    IVerificationRequestRepository requests,
    IIdentityHashRepository identities,
    IFinHasher finHasher,
    FileUploader uploader,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<SubmitVerificationCommand, VerificationRequestDto>
{
    public async Task<Result<VerificationRequestDto>> Handle(SubmitVerificationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId!.Value;
        var category = await categories.GetAsync(request.CategoryId, cancellationToken).ConfigureAwait(false);
        if (category is not { IsActive: true })
        {
            return CategoryErrors.NotFound;
        }

        if (category.IsOpen)
        {
            return VerificationErrors.NotRequired;
        }

        if (await requests.HasPendingAsync(userId, category.Id, cancellationToken).ConfigureAwait(false))
        {
            return VerificationErrors.Pending;
        }

        var fields = (request.Fields ?? new Dictionary<string, string>())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
        var problems = VerificationRules.Check(category.Verification, request.Subtype?.Trim(), fields, request.Fin, request.Files.Count);
        if (problems.Count > 0)
        {
            return new ValidationError([.. problems.Select(p => new ValidationFailure(p.Field, p.Code, p.Message))]);
        }

        // FIN: yalnız HMAC hash-i işlənir. Eyni FIN bu kateqoriyada başqa hesaba bağlıdırsa → identity_already_used.
        string? finHash = null;
        if (category.Verification.RequiresFin)
        {
            finHash = finHasher.Hash(VerificationRules.NormalizeFin(request.Fin)!);
            var bound = await identities.FindAsync(finHash, category.Id, cancellationToken).ConfigureAwait(false);
            if (bound is not null && bound.UserId != userId)
            {
                return VerificationErrors.IdentityAlreadyUsed;
            }

            if (bound is null)
            {
                await identities.AddAsync(IdentityHash.Bind(finHash, category.Id, userId, clock.UtcNow), cancellationToken).ConfigureAwait(false);
            }
        }

        var uploads = request.Files.Select(f => new FileUpload(FilePurpose.VerificationDocument, f.ContentType, f.Content)).ToList();
        foreach (var upload in uploads)
        {
            var check = FileUploader.Check(upload);
            if (check.IsFailure)
            {
                return check.Error;
            }
        }

        var id = VerificationRequest.NewId();
        var fileIds = new List<Guid>(uploads.Count);
        foreach (var upload in uploads)
        {
            var stored = await uploader.UploadAsync(upload, FileOwnerType.VerificationRequest, id.Value, userId, cancellationToken).ConfigureAwait(false);
            if (stored.IsFailure)
            {
                return stored.Error;
            }

            fileIds.Add(stored.Value.Id.Value);
        }

        var verification = VerificationRequest.Submit(
            id, userId, category.Id, request.Subtype?.Trim(), fields, finHash, fileIds, clock.UtcNow);
        await requests.AddAsync(verification, cancellationToken).ConfigureAwait(false);
        return verification.ToDto(category);
    }
}
