using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvidenceChain.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Evidence"/>; the code is a persisted computed column.</summary>
internal sealed class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    /// <summary>Raises if InitialCustodianId changes; created by the CustodySchema migration.</summary>
    public const string InitialCustodianTrigger = "TR_Evidence_InitialCustodianImmutable";

    // Binary collation: codes compare byte-for-byte, so "log…" never matches "LOG…".
    public const string BinaryCollation = "Latin1_General_100_BIN2";

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
            table.HasCheckConstraint("CK_Evidence_CapturedBeforeRegistered", "[CapturedAtUtc] <= [RegisteredAtUtc]");
            table.HasCheckConstraint("CK_Evidence_CodeDateIsRegistrationDate", "[CodeDateUtc] = CONVERT(date, [RegisteredAtUtc])");
            table.HasCheckConstraint("CK_Evidence_Head", "([EventCount] = 0 AND [HeadMac] IS NULL) OR ([EventCount] > 0 AND [HeadMac] IS NOT NULL)");
            // EF must not use OUTPUT on a table with triggers.
            table.HasTrigger(InitialCustodianTrigger);
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
        builder.Property(e => e.HeadMac).HasField("_headMac").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");
        builder.Property(e => e.EventCount).HasDefaultValue(0);
        builder.Property(e => e.IsDemoFixture).HasDefaultValue(false);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.RegisteredById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.InitialCustodianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CurrentCustodianId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Content).WithOne().HasForeignKey<EvidenceContent>(c => c.EvidenceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.Code).IsUnique().HasDatabaseName("UX_Evidence_Code");
        builder.HasIndex(e => new { e.TypeCode, e.CodeDateUtc, e.DailyNo }).IsUnique().HasDatabaseName("UX_Evidence_TypeDateNo");
    }
}

/// <summary>Maps <see cref="EvidenceContent"/>: bytes in their own table, off-row past 8,000 bytes.</summary>
internal sealed class EvidenceContentConfiguration : IEntityTypeConfiguration<EvidenceContent>
{
    public void Configure(EntityTypeBuilder<EvidenceContent> builder)
    {
        builder.ToTable("EvidenceContent", table =>
            table.HasCheckConstraint("CK_EvidenceContent_Length", "[ByteLength] = DATALENGTH([Bytes])"));

        builder.HasKey(c => c.EvidenceId);
        builder.Property(c => c.EvidenceId).ValueGeneratedNever();
        builder.Property(c => c.Sha256).HasField("_sha256").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("binary(32)");
        builder.Property(c => c.MediaType).HasMaxLength(100).IsUnicode(false);
        builder.Property(c => c.Bytes).HasField("_bytes").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("varbinary(max)");
    }
}

/// <summary>Maps <see cref="DailyCounter"/>: one row per (type, UTC day).</summary>
internal sealed class DailyCounterConfiguration : IEntityTypeConfiguration<DailyCounter>
{
    public void Configure(EntityTypeBuilder<DailyCounter> builder)
    {
        builder.ToTable("DailyCounter", table =>
            table.HasCheckConstraint("CK_DailyCounter_LastNo", $"[LastNo] BETWEEN 1 AND {EvidenceCode.MaxDailyNo}"));

        builder.HasKey(c => new { c.TypeCode, c.Day });
        builder.Property(c => c.TypeCode).HasColumnType("char(3)").IsUnicode(false).UseCollation(EvidenceConfiguration.BinaryCollation);
        builder.Property(c => c.Day).HasColumnType("date");
    }
}
