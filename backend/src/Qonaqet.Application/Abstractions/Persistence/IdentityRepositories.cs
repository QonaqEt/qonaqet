using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Abstractions.Persistence;

public interface IUserRepository : IRepository<User, UserId>
{
    /// <summary>Silinməmiş hesab (email normallaşdırılmış olmalıdır). Silinmiş hesabın email-i yenidən istifadə oluna bilər.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Push provayderinin etibarsız saydığı token-ləri atomik silir (<c>$pullAll</c>): bütün sənədi əvəz etmir, ona görə
    /// paralel profil dəyişiklikləri ilə toqquşmur.
    /// </summary>
    Task RemoveFcmTokensAsync(UserId userId, IReadOnlyCollection<string> tokens, CancellationToken cancellationToken);
}

public interface IOtpCodeRepository : IRepository<OtpCode, OtpCodeId>
{
    /// <summary><paramref name="since"/>-dən bəri bu email-ə neçə kod göndərilib (göndərmə limiti üçün).</summary>
    Task<long> CountIssuedSinceAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// Email-in ən son istifadə oluna bilən kodu üçün bir cəhdi <b>atomik</b> sərf edir və kodu qaytarır; belə kod
    /// yoxdursa <c>null</c>. Sayğac tranzaksiyadan kənarda yazılır ki, uğursuz cəhd rollback ilə geri alınmasın
    /// (brute force qoruması) və paralel cəhdlər limiti keçə bilməsin.
    /// </summary>
    Task<OtpCode?> ConsumeAttemptAsync(string email, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Kodu cari tranzaksiyada istifadə olunmuş kimi qeyd edir; artıq istifadə olunubsa <c>false</c>.</summary>
    Task<bool> TryMarkUsedAsync(OtpCodeId id, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IRefreshTokenRepository : IRepository<RefreshToken, RefreshTokenId>
{
    /// <summary>Hash üzrə token, ləğv olunmuş və vaxtı keçmiş olsa da (təkrar istifadəni aşkarlamaq üçün).</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Token hələ ləğv olunmayıbsa atomik ləğv edir; artıq ləğv olunubsa <c>false</c> (paralel rotasiya).</summary>
    Task<bool> TryRevokeAsync(
        RefreshTokenId id, DateTimeOffset now, RefreshTokenRevokeReason reason, RefreshTokenId? replacedBy, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin bütün aktiv token-lərini ləğv edir (bütün sessiyalar bağlanır); ləğv olunanların sayı.</summary>
    Task<long> RevokeAllForUserAsync(UserId userId, DateTimeOffset now, RefreshTokenRevokeReason reason, CancellationToken cancellationToken);
}
