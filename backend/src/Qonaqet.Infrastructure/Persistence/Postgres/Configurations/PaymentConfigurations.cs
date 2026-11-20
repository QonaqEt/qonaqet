using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qonaqet.Domain.Payments;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Configurations;

/// <summary>Bütün entity-lərin ortaq (<c>BaseEntity</c>) sütunları.</summary>
internal static class EntityColumns
{
    public static void ConfigureBase<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<string?>("DeleteReason").HasMaxLength(500);
        builder.Ignore("IsAppendOnly");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);
        EntityColumns.ConfigureBase(builder);
        builder.Ignore(x => x.DomainEvents);
        builder.Ignore(x => x.RefundableAmount);
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderOrderId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PaymentUrl).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Payment.MaxDescriptionLength).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();

        // CLAUDE.md: payments(provider_order_id) unikal; payments(status, created_at) — yenidən yoxlama job-unun sorğusu.
        // payments append-only-dir: indekslər partial deyil.
        builder.HasIndex(x => x.ProviderOrderId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.PayerUserId, x.CreatedAt });
        builder.HasIndex(x => new { x.Purpose, x.ReferenceId });
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("refunds");
        builder.HasKey(x => x.Id);
        EntityColumns.ConfigureBase(builder);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).HasMaxLength(Refund.MaxReasonLength).IsRequired();
        builder.Property(x => x.ProviderRef).HasMaxLength(128);
        builder.Property(x => x.FailureReason).HasMaxLength(Refund.MaxReasonLength);
        builder.HasIndex(x => new { x.PaymentId, x.CreatedAt });
    }
}
