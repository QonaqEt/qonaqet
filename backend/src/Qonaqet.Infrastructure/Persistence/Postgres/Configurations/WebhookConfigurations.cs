using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qonaqet.Domain.Integration;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Configurations;

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("webhook_subscriptions");
        builder.HasKey(x => x.Id);
        EntityColumns.ConfigureBase(builder);
        builder.Ignore(x => x.DomainEvents);
        builder.Property(x => x.Url).HasMaxLength(WebhookSubscription.UrlMaxLength).IsRequired();
        builder.Property(x => x.SecretSealed).HasMaxLength(500).IsRequired();
        builder.Ignore(x => x.EventTypes);
        builder.Property<List<string>>("_eventTypes").HasColumnName("event_types").HasColumnType("text[]");
        builder.HasIndex(x => x.ClientId).HasFilter("is_deleted = false").HasDatabaseName("ix_webhook_subscriptions_client_id");
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("webhook_deliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ResponseBodySnippet).HasMaxLength(WebhookDelivery.SnippetMaxLength);

        // Append-only status jurnalı: (delivery_id, seq) unikaldır — bir çatdırılmanı yalnız bir worker götürür.
        builder.HasIndex(x => new { x.DeliveryId, x.Seq }).IsUnique().HasDatabaseName("ux_webhook_deliveries_delivery_seq");
        builder.HasIndex(x => new { x.SubscriptionId, x.Status, x.NextAttemptAt }).HasDatabaseName("ix_webhook_deliveries_subscription_status_next");
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("ix_webhook_deliveries_status_next");
        builder.HasIndex(x => new { x.ClientId, x.CreatedAt }).HasDatabaseName("ix_webhook_deliveries_client_created");
    }
}
