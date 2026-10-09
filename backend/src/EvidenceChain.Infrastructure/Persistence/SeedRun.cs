namespace EvidenceChain.Infrastructure.Persistence;

/// <summary>Record of one seeder run, so a repeated `seed --if-empty` can recognise its own data.</summary>
public sealed class SeedRun
{
    public int SeedRunId { get; set; }

    public long Seed { get; set; }

    public DateTime AnchorUtc { get; set; }

    public string Profile { get; set; } = string.Empty;

    public int Evidences { get; set; }

    public int Events { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>The command line as run, without secrets.</summary>
    public string Arguments { get; set; } = string.Empty;
}
