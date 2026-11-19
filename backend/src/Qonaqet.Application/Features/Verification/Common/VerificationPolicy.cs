namespace Qonaqet.Application.Features.Verification.Common;

/// <summary>Təsdiqin vaxt qaydaları.</summary>
public static class VerificationPolicy
{
    /// <summary>Müddətin bitməsinə bu qədər qalmış xatırlatma göndərilir və yeniləmə sorğusu qəbul olunur.</summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(14);
}
