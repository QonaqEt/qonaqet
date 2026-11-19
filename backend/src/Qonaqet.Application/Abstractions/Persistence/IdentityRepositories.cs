using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Abstractions.Persistence;

public interface IUserRepository : IRepository<User, UserId>
{
    /// <summary>Silinməmiş hesab (email normallaşdırılmış olmalıdır). Silinmiş hesabın email-i yenidən istifadə oluna bilər.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
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
