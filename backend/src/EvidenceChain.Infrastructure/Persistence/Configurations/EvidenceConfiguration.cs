using EvidenceChain.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvidenceChain.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Evidence"/>; the code is a persisted computed column (roadmap F29).</summary>
internal sealed class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    // Binary collation: codes compare byte-for-byte, so "log…" never matches "LOG…".
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    // Deterministic (no FORMAT()), so SQL Server can persist and index it.
    private const string CodeExpression =
        "CONCAT([TypeCode], CONVERT(char(8), [CodeDateUtc], 112), RIGHT(CONCAT('000', [DailyNo]), 4)) COLLATE " + BinaryCollation;

    /// <summary>Configures table, keys, constraints and indexes.</summary>
    public void Configure(EntityTypeBuilder<Evidence> builder)
    {
        builder.ToTable("Evidence", table =>
        {
            table.HasCheckConstraint("CK_Evidence_TypeCode", "[TypeCode] IN ('LOG', 'CSV', 'EML')");
            table.HasCheckConstraint("CK_Evidence_DailyNo", $"[DailyNo] BETWEEN 1 AND {EvidenceCode.MaxDailyNo}");
        });

        builder.HasKey(e => e.EvidenceId);
        builder.Property(e => e.EvidenceId).UseIdentityColumn();

        builder.Property(e => e.TypeCode)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .UseCollation(BinaryCollation);

        builder.Property(e => e.CodeDateUtc).HasColumnType("date");

        builder.Property(e => e.Code)
            .IsUnicode(false)
            .HasMaxLength(15)
            .HasComputedColumnSql(CodeExpression, stored: true);

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => e.Code).IsUnique().HasDatabaseName("UX_Evidence_Code");
        builder.HasIndex(e => new { e.TypeCode, e.CodeDateUtc, e.DailyNo }).IsUnique().HasDatabaseName("UX_Evidence_TypeDateNo");

        // Tracer ordering; replaced by the A4 inbox projection on Day 2.
        builder.HasIndex(e => new { e.CodeDateUtc, e.EvidenceId })
            .IsDescending(true, true)
            .HasDatabaseName("IX_Evidence_Recent");
    }
}
