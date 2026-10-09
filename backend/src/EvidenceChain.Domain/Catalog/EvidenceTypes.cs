namespace EvidenceChain.Domain.Catalog;

/// <summary>Evidence file types and their three-letter codes.</summary>
public static class EvidenceTypes
{
    public const string Log = "LOG";
    public const string Csv = "CSV";
    public const string Eml = "EML";

    /// <summary>All valid type codes.</summary>
    public static IReadOnlyList<string> All { get; } = [Log, Csv, Eml];

    /// <summary>True when <paramref name="typeCode"/> is a known code (case-sensitive).</summary>
    public static bool IsKnown(string? typeCode) => typeCode is Log or Csv or Eml;

    /// <summary>File extension for a type code, without the dot.</summary>
    public static string FileExtension(string typeCode) => typeCode switch
    {
        Log => "log",
        Csv => "csv",
        Eml => "eml",
        _ => throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type."),
    };
}
