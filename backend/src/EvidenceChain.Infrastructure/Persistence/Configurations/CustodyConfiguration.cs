using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvidenceChain.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="CustodyEvent"/>: append-only, one row per chain link.</summary>
internal sealed class CustodyEventConfiguration : IEntityTypeConfiguration<CustodyEvent>
{
    /// <summary>Raises on UPDATE or DELETE; created by the CustodySchema migration.</summary>
    public const string AppendOnlyTrigger = "TR_CustodyEvents_AppendOnly";

    public void Configure(EntityTypeBuilder<CustodyEvent> builder)
    {
        builder.ToTable("CustodyEvents", table =>
        {
            table.HasCheckConstraint("CK_CustodyEvents_Kind", "[Kind] IN ('EvidenceRegistered', 'TransferRequested', 'TransferAccepted', 'TransferRejected')");
            // Genesis is sequence 1: unlinked, naming the initial custodian and committing the content.
            // Every later link carries the previous MAC and the transfer it records.
            table.HasCheckConstraint("CK_CustodyEvents_Shape",
                "([Seq] = 1 AND [Kind] = 'EvidenceRegistered' AND [PrevMac] IS NULL AND [TransferId] IS NULL"
                + " AND [FromCustodianId] IS NULL AND [ToCustodianId] IS NOT NULL"
                + " AND [ContentSha256] IS NOT NULL AND [ContentLength] >= 0 AND [MediaType] IS NOT NULL)"
                + " OR ([Seq] > 1 AND [Kind] <> 'EvidenceRegistered' AND [PrevMac] IS NOT NULL AND [TransferId] IS NOT NULL"
                + " AND [FromCustodianId] IS NOT NULL AND [ToCustodianId] IS NOT NULL"
                + " AND [ContentSha256] IS NULL AND [ContentLength] IS NULL AND [MediaType] IS NULL)");
            table.HasTrigger(AppendOnlyTrigger);
        });

        builder.HasKey(e => e.CustodyEventId);
        builder.Property(e => e.CustodyEventId).UseIdentityColumn();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.ContentSha256).HasField("_contentSha256").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");
        builder.Property(e => e.MediaType).HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.KeyId).HasMaxLength(16).IsUnicode(false);
        builder.Property(e => e.PrevMac).HasField("_prevMac").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");
        builder.Property(e => e.Mac).HasField("_mac").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");

        builder.HasOne<Evidence>().WithMany().HasForeignKey(e => e.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        // Composite, so an event can only cite a transfer of its own evidence.
        builder.HasOne<CustodyTransfer>().WithMany()
            .HasForeignKey(e => new { e.TransferId, e.EvidenceId })
            .HasPrincipalKey(t => new { t.TransferId, t.EvidenceId })
            .HasConstraintName("FK_CustodyEvents_CustodyTransfers_TransferEvidence")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.FromCustodianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ToCustodianId).OnDelete(DeleteBehavior.Restrict);

        // Two writers appending the same position collide here instead of forking the chain.
        builder.HasIndex(e => new { e.EvidenceId, e.Seq }).IsUnique().HasDatabaseName("UX_CustodyEvents_EvidenceSeq");
    }
}

/// <summary>Maps <see cref="CustodyTransfer"/>: one pending transfer per evidence, idempotent requests and decisions.</summary>
internal sealed class CustodyTransferConfiguration : IEntityTypeConfiguration<CustodyTransfer>
{
    public void Configure(EntityTypeBuilder<CustodyTransfer> builder)
    {
        builder.ToTable("CustodyTransfers", table =>
        {
            table.HasCheckConstraint("CK_CustodyTransfers_Status", "[Status] IN ('Pending', 'Accepted', 'Rejected')");
            table.HasCheckConstraint("CK_CustodyTransfers_DifferentRecipient", "[FromCustodianId] <> [ToCustodianId]");
            // Explicit IS NOT NULL: a CHECK treats an unknown comparison as passing.
            table.HasCheckConstraint("CK_CustodyTransfers_Decision",
                "([Status] = 'Pending' AND [DecidedAtUtc] IS NULL AND [DecidedById] IS NULL AND [DecisionKey] IS NULL AND [DecisionFingerprint] IS NULL)"
                + " OR ([Status] <> 'Pending' AND [DecidedAtUtc] IS NOT NULL AND [DecidedAtUtc] >= [RequestedAtUtc]"
                + " AND [DecidedById] IS NOT NULL AND [DecisionKey] IS NOT NULL AND [DecisionFingerprint] IS NOT NULL)");
            // Only the recipient decides, enforced by the database as well as the domain.
            table.HasCheckConstraint("CK_CustodyTransfers_RecipientDecides", "[DecidedById] IS NULL OR [DecidedById] = [ToCustodianId]");
        });

        builder.HasKey(t => t.TransferId);
        builder.Property(t => t.TransferId).UseIdentityColumn();
        // A string, so the filtered index predicate below can name the state.
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(t => t.Reason).HasMaxLength(500);
        builder.Property(t => t.DecisionNotes).HasMaxLength(500);
        builder.Property(t => t.RequestFingerprint).HasField("_requestFingerprint").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");
        builder.Property(t => t.DecisionFingerprint).HasField("_decisionFingerprint").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasOne<Evidence>().WithMany().HasForeignKey(t => t.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.FromCustodianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.ToCustodianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.RequestedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.DecidedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.EvidenceId).IsUnique().HasFilter("[Status] = 'Pending'").HasDatabaseName("UX_CustodyTransfers_OnePendingPerEvidence");
        // The filtered index only holds pending rows; history and FK checks need every transfer.
        builder.HasIndex(t => new { t.EvidenceId, t.TransferId }).HasDatabaseName("IX_CustodyTransfers_Evidence");
        builder.HasIndex(t => new { t.RequestedById, t.ClientRequestId }).IsUnique().HasDatabaseName("UX_CustodyTransfers_RequestKey");
        builder.HasIndex(t => new { t.DecidedById, t.DecisionKey }).IsUnique().HasFilter("[DecisionKey] IS NOT NULL").HasDatabaseName("UX_CustodyTransfers_DecisionKey");
    }
}
