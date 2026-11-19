using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Users.Common;
using Qonaqet.Application.Features.Verification.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.Domain.Verification;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.ReviewVerification;

/// <summary>
/// <c>POST admin/verifications/{id}/approve</c>: admin təsdiqləyir. <c>validUntil</c> kateqoriyanın <c>validity</c> qaydasından hesablanır
/// (FixedDate / Months / None); admin onu əvəz edə bilər (<see cref="ValidUntilOverride"/>, gələcək an olmalıdır). Təsdiq
/// <c>users.verifiedCategories[]</c>-ə yazılır (eyni kateqoriyanın köhnə qeydi əvəz olunur). FIN bu kateqoriyada başqa hesaba
/// bağlanıbsa təsdiq olunmur (<c>identity_already_used</c>).
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ApproveVerificationCommand(VerificationRequestId Id, DateTimeOffset? ValidUntilOverride) : ICommand<VerificationRequestDto>;

/// <summary><c>POST admin/verifications/{id}/reject</c>: rədd, səbəb məcburidir. FIN bağı başqa təsdiqə aid deyilsə açılır.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RejectVerificationCommand(VerificationRequestId Id, string Reason) : ICommand<VerificationRequestDto>;

/// <summary>
/// <c>PUT admin/verifications/{id}/valid-until</c>: təsdiqlənmiş sorğunun müddətini admin əl ilə dəyişir (<c>null</c> = müddətsiz).
/// Keçmiş an verilərsə təsdiq dərhal qüvvədən düşür (məntiqi müddət).
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ChangeVerificationValidityCommand(VerificationRequestId Id, DateTimeOffset? ValidUntil) : ICommand<VerificationRequestDto>;

public sealed class RejectVerificationValidator : Validator<RejectVerificationCommand>
{
    public RejectVerificationValidator()
    {
        RuleFor<string?>(x => x.Reason).NotEmpty().MaxLength(500);
    }
}

/// <summary>Üç əmrin ortaq addımları: sorğu, kateqoriya və sahibin yüklənməsi, audit, bildiriş.</summary>
public sealed class VerificationReview(
    IVerificationRequestRepository requests,
    ICategoryRepository categories,
    IUserRepository users,
    IOutbox outbox,
    ICurrentUser currentUser,
    UserPushNotifier push)
{
    public Guid AdminId => currentUser.ActorId!.Value;

    public async Task<Result<(VerificationRequest Request, Category? Category, User User)>> LoadAsync(VerificationRequestId id, CancellationToken cancellationToken)
    {
        var request = await requests.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return VerificationErrors.NotFound;
        }

        var user = await users.GetAsync(UserId.From(request.UserId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return VerificationErrors.UserNotFound;
        }

        var category = await categories.GetAsync(request.CategoryId, cancellationToken).ConfigureAwait(false);
        return (request, category, user);
    }

    public async Task SaveAsync(VerificationRequest request, User? user, string action, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await requests.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        if (user is not null)
        {
            await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        }

        var data = new Dictionary<string, string?>
        {
            ["userId"] = request.UserId.ToString("D"),
            ["categoryId"] = request.CategoryId.Value.ToString("D"),
            ["status"] = request.Status.ToString(),
            ["validUntil"] = request.ValidUntil?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        await outbox.AddAsync([new AuditRecorded(at, AdminId, "User", action, nameof(VerificationRequest), request.Id.Value, data)], cancellationToken)
            .ConfigureAwait(false);
    }

    public Task NotifyAsync(Guid userId, string title, string body, CancellationToken cancellationToken) =>
        push.NotifyAsync(UserId.From(userId), NotificationType.Verification, new PushMessage(title, body), cancellationToken);
}

public sealed class ApproveVerificationHandler(VerificationReview review, IIdentityHashRepository identities, IClock clock)
    : ICommandHandler<ApproveVerificationCommand, VerificationRequestDto>
{
    public async Task<Result<VerificationRequestDto>> Handle(ApproveVerificationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await review.LoadAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (verification, category, user) = loaded.Value;
        if (!verification.IsPending)
        {
            return VerificationErrors.AlreadyReviewed;
        }

        if (category is null)
        {
            return CategoryErrors.NotFound;
        }

        var now = clock.UtcNow;
        if (request.ValidUntilOverride is { } manual && manual <= now)
        {
            return Error.Validation("valid_until_invalid", "Təsdiq zamanı müddət gələcək an olmalıdır.");
        }

        if (verification.FinHash is { } finHash)
        {
            var bound = await identities.FindAsync(finHash, category.Id, cancellationToken).ConfigureAwait(false);
            if (bound is not null && bound.UserId != verification.UserId)
            {
                return VerificationErrors.IdentityAlreadyUsed;
            }

            if (bound is null)
            {
                await identities.AddAsync(IdentityHash.Bind(finHash, category.Id, verification.UserId, now), cancellationToken).ConfigureAwait(false);
            }
        }

        var validUntil = request.ValidUntilOverride ?? category.Verification.Validity.ValidUntil(now);
        verification.Approve(validUntil, now, review.AdminId);
        user.VerifyCategory(category.Id.Value, verification.Subtype, validUntil, verification.Id.Value, now, review.AdminId);
        await review.SaveAsync(verification, user, CategoryAuditActions.ApproveVerification, now, cancellationToken).ConfigureAwait(false);
        await review.NotifyAsync(user.Id.Value, "Statusunuz təsdiqləndi 💚", $"\"{category.Name}\" statusunuz təsdiqləndi.", cancellationToken).ConfigureAwait(false);
        return verification.ToDto(category);
    }
}

public sealed class RejectVerificationHandler(VerificationReview review, IIdentityHashRepository identities, IClock clock)
    : ICommandHandler<RejectVerificationCommand, VerificationRequestDto>
{
    public async Task<Result<VerificationRequestDto>> Handle(RejectVerificationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await review.LoadAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (verification, category, user) = loaded.Value;
        if (!verification.IsPending)
        {
            return VerificationErrors.AlreadyReviewed;
        }

        var now = clock.UtcNow;
        verification.Reject(request.Reason, now, review.AdminId);

        // FIN bağı yalnız bu istifadəçinin bu kateqoriyada etibarlı təsdiqi yoxdursa açılır (yeniləmə rədd olunsa da köhnə təsdiq qalır).
        if (verification.FinHash is { } finHash && user.VerifiedCategoryFor(verification.CategoryId.Value)?.IsValidAt(now) != true)
        {
            var bound = await identities.FindAsync(finHash, verification.CategoryId, cancellationToken).ConfigureAwait(false);
            if (bound is not null && bound.UserId == verification.UserId)
            {
                await identities.SoftDeleteAsync(bound.Id, review.AdminId, "verification_rejected", cancellationToken).ConfigureAwait(false);
            }
        }

        await review.SaveAsync(verification, null, CategoryAuditActions.RejectVerification, now, cancellationToken).ConfigureAwait(false);
        await review.NotifyAsync(user.Id.Value, "Status sorğunuz rədd olundu", verification.RejectReason!, cancellationToken).ConfigureAwait(false);
        return verification.ToDto(category);
    }
}

public sealed class ChangeVerificationValidityHandler(VerificationReview review, IClock clock)
    : ICommandHandler<ChangeVerificationValidityCommand, VerificationRequestDto>
{
    public async Task<Result<VerificationRequestDto>> Handle(ChangeVerificationValidityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await review.LoadAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (verification, category, user) = loaded.Value;
        if (verification.Status != VerificationStatus.Approved)
        {
            return VerificationErrors.NotApproved;
        }

        var now = clock.UtcNow;
        verification.OverrideValidUntil(request.ValidUntil, now, review.AdminId);

        // Yalnız istifadəçinin cari təsdiqi bu sorğudandırsa onun qeydi dəyişir (yenilənmiş təsdiq köhnə sorğu ilə əvəz olunmasın).
        var current = user.OverrideCategoryValidity(verification.Id.Value, request.ValidUntil, now, review.AdminId);
        await review.SaveAsync(verification, current ? user : null, CategoryAuditActions.ChangeVerificationValidity, now, cancellationToken).ConfigureAwait(false);
        return verification.ToDto(category);
    }
}
