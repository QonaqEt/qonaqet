using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Users.Queries.GetMe;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Users.Commands.UpdateProfile;

/// <summary>
/// <c>PATCH me</c>: yalnız göndərilən sahələr dəyişir (<c>null</c> = toxunma). <see cref="LastName"/> boş sətirlə silinir,
/// avatar <see cref="RemoveAvatar"/> ilə. Ad heç vaxt boş qala bilməz — qeydiyyatın "ad" addımı da budur.
/// Avatar faylının mövcudluğu və sahibliyi E03-T02 (files) ilə yoxlanılacaq.
/// </summary>
public sealed record UpdateProfileCommand(string? FirstName, string? LastName, Guid? AvatarFileId, bool RemoveAvatar = false) : ICommand<MeDto>;

public sealed class UpdateProfileValidator : Validator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FirstName).Must(n => n is null || !string.IsNullOrWhiteSpace(n), "first_name_required", "Ad boş ola bilməz.").MaxLength(User.NameMaxLength);
        RuleFor(x => x.LastName).MaxLength(User.NameMaxLength);
        RuleFor(x => x.AvatarFileId).Must(id => id != Guid.Empty, "avatar_invalid", "Avatar faylı düzgün deyil.");
        Rule("avatarFileId", x => !(x.RemoveAvatar && x.AvatarFileId is not null), "avatar_conflict", "Avatar həm verilə, həm silinə bilməz.");
    }
}

public sealed class UpdateProfileHandler(IUserRepository users, ICurrentUser currentUser, IClock clock) : ICommandHandler<UpdateProfileCommand, MeDto>
{
    public async Task<Result<MeDto>> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(UserId.From(currentUser.ActorId!.Value), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        var firstName = request.FirstName ?? user.FirstName;
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return AuthErrors.FirstNameRequired;
        }

        var avatar = request.RemoveAvatar ? null : request.AvatarFileId ?? user.AvatarFileId;
        user.UpdateProfile(firstName, request.LastName ?? user.LastName, avatar, clock.UtcNow);
        await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        return user.ToMeDto();
    }
}
