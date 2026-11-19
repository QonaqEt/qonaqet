using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

public readonly record struct UserId(Guid Value) : IStronglyTypedId<UserId>
{
    public static UserId From(Guid value) => new(value);
}

public readonly record struct OtpCodeId(Guid Value) : IStronglyTypedId<OtpCodeId>
{
    public static OtpCodeId From(Guid value) => new(value);
}
