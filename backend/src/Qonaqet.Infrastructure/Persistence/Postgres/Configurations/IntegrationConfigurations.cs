using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qonaqet.Domain.Integration;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Configurations;

public sealed class IntegrationClientConfiguration : IEntityTypeConfiguration<IntegrationClient>
{
    public void Configure(EntityTypeBuilder<IntegrationClient> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("integration_clients");
        builder.HasKey(x => x.Id);
        EntityColumns.ConfigureBase(builder);
        builder.Ignore(x => x.DomainEvents);
        builder.Property(x => x.Name).HasMaxLength(IntegrationClient.NameMaxLength).IsRequired();
        builder.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PreviousSecretHash).HasMaxLength(128);
        builder.Property(x => x.OwnerType).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(x => x.BusinessIds);
        builder.Ignore(x => x.Scopes);
        builder.Property<List<Guid>>("_businessIds").HasColumnName("business_ids").HasColumnType("uuid[]");
        builder.Property<List<string>>("_scopes").HasColumnName("scopes").HasColumnType("text[]");

        // CLAUDE.md: integration_clients(client_id) unikal partial — ləğv olunmuş client-in id-si təkrar verilmir, amma filtrdən kənardadır.
        builder.HasIndex(x => x.ClientId).IsUnique().HasFilter("is_deleted = false").HasDatabaseName("ux_integration_clients_client_id");
        builder.HasIndex(x => new { x.OwnerType, x.OwnerId }).HasFilter("is_deleted = false").HasDatabaseName("ix_integration_clients_owner");
    }
}
