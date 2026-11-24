using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Leaderboard.Commands.UpdateLeaderboardSettings;

/// <summary>
/// <c>PATCH me/leaderboard</c>: reytinqdə görünmə (opt-out istənilən vaxt) və ləqəb. Yalnız göndərilən sahələr dəyişir (<c>null</c> = toxunma);
/// ləqəb <see cref="RemoveNickname"/> ilə silinir. Opt-out xalı silmir — istifadəçi sadəcə lövhədə görünmür.
/// </summary>
public sealed record UpdateLeaderboardSettingsCommand(bool? OptIn, string? Nickname, bool RemoveNickname = false) : ICommand<LeaderboardSettingsDto>;

public sealed record LeaderboardSettingsDto(bool OptIn, string? Nickname, string DisplayName);

public sealed class UpdateLeaderboardSettingsValidator : Validator<UpdateLeaderboardSettingsCommand>
{
    public UpdateLeaderboardSettingsValidator()
    {
        RuleFor(x => x.Nickname).Must(n => n is null || !string.IsNullOrWhiteSpace(n), "nickname_required", "Ləqəb boş ola bilməz.")
            .MaxLength(User.LeaderboardNameMaxLength);
        RuleFor(x => x.Nickname).Must(
            n => n is null || n.Trim().Length >= User.LeaderboardNameMinLength,
            ValidationCodes.TooShort,
            $"Ən azı {User.LeaderboardNameMinLength} simvol olmalıdır.");
        Rule("nickname", x => !(x.RemoveNickname && x.Nickname is not null), "nickname_conflict", "Ləqəb həm verilə, həm silinə bilməz.");
    }
}

public sealed class UpdateLeaderboardSettingsHandler(IUserRepository users, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateLeaderboardSettingsCommand, LeaderboardSettingsDto>
{
    public async Task<Result<LeaderboardSettingsDto>> Handle(UpdateLeaderboardSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(UserId.From(currentUser.ActorId!.Value), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        var nickname = request.RemoveNickname ? null : request.Nickname ?? user.LeaderboardName;
        user.SetLeaderboardPreferences(request.OptIn ?? user.LeaderboardOptIn, nickname, clock.UtcNow);
        await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        return user.ToLeaderboardSettingsDto();
    }
}

public static class LeaderboardSettingsMapping
{
    public static LeaderboardSettingsDto ToLeaderboardSettingsDto(this User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new LeaderboardSettingsDto(user.LeaderboardOptIn, user.LeaderboardName, user.LeaderboardDisplayName);
    }
}
