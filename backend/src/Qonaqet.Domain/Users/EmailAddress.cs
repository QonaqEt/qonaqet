namespace Qonaqet.Domain.Users;

/// <summary>Email normallaşdırması: kənar boşluqlar silinir, kiçik hərflə (<c>users.email</c> unikal indeksi bunun üzərindədir).</summary>
public static class EmailAddress
{
    public const int MaxLength = 254;

    public static string Normalize(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToLowerInvariant();
    }
}
