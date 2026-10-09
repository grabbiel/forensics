using System.Diagnostics.CodeAnalysis;

namespace EvidenceChain.Application.Common;

/// <summary>Strong ETags for rowversion-guarded resources: the 8-byte version as quoted lowercase hex.</summary>
public static class EntityTags
{
    private const int VersionLength = 8;

    public static string Format(byte[] version)
    {
        if (version.Length != VersionLength)
            throw new ArgumentException($"A rowversion is {VersionLength} bytes.", nameof(version));
        return $"\"{Convert.ToHexStringLower(version)}\"";
    }

    /// <summary>Parses one strong ETag in this format; weak tags, lists and "*" never match a version.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out byte[]? version)
    {
        version = null;
        var tag = value?.Trim();
        if (tag is not { Length: VersionLength * 2 + 2 } || tag[0] != '"' || tag[^1] != '"')
            return false;

        try
        {
            version = Convert.FromHexString(tag.AsSpan(1, VersionLength * 2));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
