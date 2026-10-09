namespace EvidenceChain.Domain;

/// <summary>Guards for timestamps: the domain only accepts UTC instants.</summary>
public static class Utc
{
    /// <summary>Throws unless <paramref name="value"/> is a UTC <see cref="DateTime"/>.</summary>
    public static void Require(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamps must be UTC.", paramName);
    }
}
