using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;
using CorporateAggregate = Qonaqet.Domain.Corporate.Corporate;

namespace Qonaqet.Application.Features.Corporate.Commands.AdminCorporates;

/// <summary><c>POST admin/corporates</c>: admin şirkət yaradır (VÖEN unikal). Audit log-a düşür.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record CreateCorporateCommand(CorporateDetails Details) : ICommand<CorporateDto>;

/// <summary><c>PUT admin/corporates/{id}</c>.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record UpdateCorporateCommand(CorporateId CorporateId, CorporateDetails Details) : ICommand<CorporateDto>;

/// <summary>
/// <c>POST admin/corporates/{id}/users</c>: email ilə mövcud istifadəçini şirkətə bağlayır və ona <c>CorporateUser</c> rolu verir (yeni rol
/// növbəti token yeniləməsində JWT-yə düşür). Başqa şirkətə bağlı istifadəçi bu şirkətə keçir.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record AssignCorporateUserCommand(CorporateId CorporateId, string Email) : ICommand<CorporateUserDto>;

/// <summary><c>GET admin/corporates</c>.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListCorporatesQuery() : IQuery<IReadOnlyList<CorporateDto>>;

public sealed record CorporateUserDto(Guid UserId, Guid CorporateId);

public static class CorporateAuditActions
{
    public const string Create = "CorporateCreate";
    public const string Update = "CorporateUpdate";
    public const string AssignUser = "CorporateAssignUser";
    public const string ConfirmBankTransfer = "CorporateOrderConfirmBankTransfer";
}

public sealed class CorporateDetailsValidator
{
    public static bool IsValid(CorporateDetails? details) =>
        details is not null
        && !string.IsNullOrWhiteSpace(details.Name) && details.Name.Trim().Length <= CorporateAggregate.NameMaxLength
        && CorporateAggregate.IsValidVoen(details.Voen)
        && CorporateAggregate.IsValidColor(details.BrandColor)
        && !string.IsNullOrWhiteSpace(details.ContactEmail) && details.ContactEmail.Contains('@', StringComparison.Ordinal) && details.ContactEmail.Length <= 254;
}

public sealed class CreateCorporateValidator : Validator<CreateCorporateCommand>
{
    public CreateCorporateValidator() =>
        Rule("details", x => CorporateDetailsValidator.IsValid(x.Details), CorporateErrors.Invalid.Code, CorporateErrors.Invalid.Message);
}

public sealed class UpdateCorporateValidator : Validator<UpdateCorporateCommand>
{
    public UpdateCorporateValidator() =>
        Rule("details", x => CorporateDetailsValidator.IsValid(x.Details), CorporateErrors.Invalid.Code, CorporateErrors.Invalid.Message);
}

public sealed class CreateCorporateHandler(ICorporateRepository corporates, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<CreateCorporateCommand, CorporateDto>
{
    public async Task<Result<CorporateDto>> Handle(CreateCorporateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await corporates.VoenExistsAsync(request.Details.Voen, null, cancellationToken).ConfigureAwait(false))
        {
            return CorporateErrors.VoenTaken;
        }

        var now = clock.UtcNow;
        var corporate = CorporateAggregate.Create(request.Details, now, currentUser.ActorId);
        await corporates.AddAsync(corporate, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", CorporateAuditActions.Create, nameof(CorporateAggregate), corporate.Id.Value)],
            cancellationToken).ConfigureAwait(false);
        return corporate.ToDto();
    }
}

public sealed class UpdateCorporateHandler(ICorporateRepository corporates, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<UpdateCorporateCommand, CorporateDto>
{
    public async Task<Result<CorporateDto>> Handle(UpdateCorporateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var corporate = await corporates.GetAsync(request.CorporateId, cancellationToken).ConfigureAwait(false);
        if (corporate is null)
        {
            return CorporateErrors.NotFound;
        }

        if (await corporates.VoenExistsAsync(request.Details.Voen, corporate.Id, cancellationToken).ConfigureAwait(false))
        {
            return CorporateErrors.VoenTaken;
        }

        var now = clock.UtcNow;
        corporate.Update(request.Details, now, currentUser.ActorId);
        await corporates.UpdateAsync(corporate, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", CorporateAuditActions.Update, nameof(CorporateAggregate), corporate.Id.Value)],
            cancellationToken).ConfigureAwait(false);
        return corporate.ToDto();
    }
}

public sealed class AssignCorporateUserHandler(
    ICorporateRepository corporates, IUserRepository users, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<AssignCorporateUserCommand, CorporateUserDto>
{
    public async Task<Result<CorporateUserDto>> Handle(AssignCorporateUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var corporate = await corporates.GetAsync(request.CorporateId, cancellationToken).ConfigureAwait(false);
        if (corporate is null)
        {
            return CorporateErrors.NotFound;
        }

        var user = await users.GetByEmailAsync((request.Email ?? string.Empty).Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CorporateErrors.UserNotFound;
        }

        var now = clock.UtcNow;
        if (user.AssignCorporate(corporate.Id.Value, now, currentUser.ActorId))
        {
            await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
            await outbox.AddAsync(
                [new AuditRecorded(now, currentUser.ActorId, "User", CorporateAuditActions.AssignUser, nameof(CorporateAggregate), corporate.Id.Value,
                    new Dictionary<string, string?> { ["userId"] = user.Id.Value.ToString() })],
                cancellationToken).ConfigureAwait(false);
        }

        return new CorporateUserDto(user.Id.Value, corporate.Id.Value);
    }
}

public sealed class ListCorporatesHandler(ICorporateRepository corporates) : IQueryHandler<ListCorporatesQuery, IReadOnlyList<CorporateDto>>
{
    public async Task<Result<IReadOnlyList<CorporateDto>>> Handle(ListCorporatesQuery request, CancellationToken cancellationToken) =>
        (await corporates.ListAsync(500, cancellationToken).ConfigureAwait(false)).Select(c => c.ToDto()).ToList();
}
