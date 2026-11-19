using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary>
/// Audit log qeydi. <c>audit_logs</c> Postgres-dədir, ona görə Mongo tərəfi bu hadisəni dəyişikliklə
/// <b>eyni tranzaksiyada</b> outbox-a yazır; consumer (E01-T09) onu <c>audit_logs</c>-a köçürür.
/// <see cref="Data"/> şəxsi məlumat, token və ya FIN saxlamamalıdır.
/// </summary>
public sealed record AuditRecorded : DomainEvent
{
    public AuditRecorded(
        DateTimeOffset occurredAt,
        Guid? actorId,
        string actorType,
        string action,
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<string, string?>? data = null)
        : base(occurredAt)
    {
        ActorId = actorId;
        ActorType = Guard.NotNullOrWhiteSpace(actorType);
        Action = Guard.NotNullOrWhiteSpace(action);
        EntityType = Guard.NotNullOrWhiteSpace(entityType);
        EntityId = Guard.NotEmpty(entityId);
        Data = data ?? new Dictionary<string, string?>();
    }

    public Guid? ActorId { get; init; }

    /// <summary><c>User</c>, <c>System</c> və ya <c>IntegrationClient</c>.</summary>
    public string ActorType { get; init; }

    /// <summary>Məs. <c>SoftDelete</c>, <c>Restore</c>.</summary>
    public string Action { get; init; }

    public string EntityType { get; init; }

    public Guid EntityId { get; init; }

    public IReadOnlyDictionary<string, string?> Data { get; init; }
}

/// <summary>Audit əməliyyat adları.</summary>
public static class AuditActions
{
    public const string SoftDelete = "SoftDelete";
    public const string Restore = "Restore";
}

/// <summary>İstifadəçi hesabı üzrə audit əməliyyatları.</summary>
public static class UserAuditActions
{
    public const string SetRoles = "SetRoles";
    public const string Block = "Block";
    public const string Unblock = "Unblock";
}

/// <summary>Biznes üzrə audit əməliyyatları.</summary>
public static class BusinessAuditActions
{
    public const string Activate = "BusinessActivate";
    public const string Suspend = "BusinessSuspend";
    public const string ChangeSettings = "BusinessChangeSettings";
    public const string UpdateProfile = "BusinessUpdateProfile";
    public const string AddMember = "BusinessAddMember";
    public const string RemoveMember = "BusinessRemoveMember";
}

/// <summary>Kateqoriya və təsdiq əməliyyatlarının audit adları.</summary>
public static class CategoryAuditActions
{
    public const string Create = "CategoryCreate";
    public const string Update = "CategoryUpdate";
    public const string ApproveVerification = "VerificationApprove";
    public const string RejectVerification = "VerificationReject";
    public const string ChangeVerificationValidity = "VerificationChangeValidity";
}
