namespace Qonaqet.SharedKernel;

/// <summary>Xətanın növü. Host-lar HTTP status kodunu bununla seçir.</summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,

    /// <summary>Limit aşılıb (429), məs. OTP göndərmə limiti.</summary>
    TooManyRequests = 6,
}
