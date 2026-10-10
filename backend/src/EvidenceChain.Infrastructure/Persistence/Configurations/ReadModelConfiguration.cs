using EvidenceChain.Domain.Catalog;
using EvidenceChain.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvidenceChain.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the EvidenceInbox projection. Every keyset index ends in (LastEventAtUtc DESC, EvidenceId DESC) and includes the
/// rest of an inbox page's columns, so a page is read from the index alone, without a lookup per row. The cost is on
/// writes: each moves its row in all four, now wider.
/// </summary>
internal sealed class EvidenceInboxConfiguration : IEntityTypeConfiguration<EvidenceInboxRow>
{
    public void Configure(EntityTypeBuilder<EvidenceInboxRow> builder)
    {
        builder.ToTable("EvidenceInbox", table =>
            table.HasCheckConstraint("CK_EvidenceInbox_IntegrityStatus", "[IntegrityStatus] IN ('Unverified', 'Valid', 'Invalid')"));

        builder.HasKey(r => r.EvidenceId);
        builder.Property(r => r.EvidenceId).ValueGeneratedNever();
        builder.Property(r => r.Code).HasMaxLength(15).IsUnicode(false).UseCollation(EvidenceConfiguration.BinaryCollation);
        builder.Property(r => r.TypeCode).HasColumnType("char(3)").IsUnicode(false).UseCollation(EvidenceConfiguration.BinaryCollation);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.CurrentCustodianName).HasMaxLength(100);
        builder.Property(r => r.IntegrityStatus).HasConversion<string>().HasMaxLength(16).IsUnicode(false);

        builder.HasOne<Evidence>().WithOne().HasForeignKey<EvidenceInboxRow>(r => r.EvidenceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.Code).IsUnique().HasDatabaseName("UX_EvidenceInbox_Code");
        // Each includes the page's columns but its own keys: SQL Server refuses a column that is both.
        builder.HasIndex(r => new { r.LastEventAtUtc, r.EvidenceId }).IsDescending(true, true).HasDatabaseName("IX_EvidenceInbox_Recent")
            .IncludeProperties(r => new { r.Code, r.TypeCode, r.Description, r.CurrentCustodianId, r.CurrentCustodianName, r.EventCount,
                r.IntegrityStatus, r.IntegrityCheckedAtUtc, r.PendingTransferId, r.PendingToCustodianId, r.PendingSinceUtc });
        builder.HasIndex(r => new { r.TypeCode, r.LastEventAtUtc, r.EvidenceId }).IsDescending(false, true, true).HasDatabaseName("IX_EvidenceInbox_Type")
            .IncludeProperties(r => new { r.Code, r.Description, r.CurrentCustodianId, r.CurrentCustodianName, r.EventCount,
                r.IntegrityStatus, r.IntegrityCheckedAtUtc, r.PendingTransferId, r.PendingToCustodianId, r.PendingSinceUtc });
        builder.HasIndex(r => new { r.CurrentCustodianId, r.LastEventAtUtc, r.EvidenceId }).IsDescending(false, true, true).HasDatabaseName("IX_EvidenceInbox_Custodian")
            .IncludeProperties(r => new { r.Code, r.TypeCode, r.Description, r.CurrentCustodianName, r.EventCount,
                r.IntegrityStatus, r.IntegrityCheckedAtUtc, r.PendingTransferId, r.PendingToCustodianId, r.PendingSinceUtc });
        builder.HasIndex(r => new { r.IntegrityStatus, r.LastEventAtUtc, r.EvidenceId }).IsDescending(false, true, true).HasDatabaseName("IX_EvidenceInbox_Integrity")
            .IncludeProperties(r => new { r.Code, r.TypeCode, r.Description, r.CurrentCustodianId, r.CurrentCustodianName, r.EventCount,
                r.IntegrityCheckedAtUtc, r.PendingTransferId, r.PendingToCustodianId, r.PendingSinceUtc });
        // The integrity sweep's "checked longest ago" order; never-checked (NULL) sorts first.
        builder.HasIndex(r => new { r.IntegrityCheckedAtUtc, r.EvidenceId }).HasDatabaseName("IX_EvidenceInbox_IntegrityChecked");
    }
}

/// <summary>Maps <see cref="SeedRun"/>.</summary>
internal sealed class SeedRunConfiguration : IEntityTypeConfiguration<SeedRun>
{
    public void Configure(EntityTypeBuilder<SeedRun> builder)
    {
        builder.ToTable("SeedRuns");
        builder.HasKey(r => r.SeedRunId);
        builder.Property(r => r.Profile).HasMaxLength(32).IsUnicode(false);
        builder.Property(r => r.Arguments).HasMaxLength(1000);
    }
}
