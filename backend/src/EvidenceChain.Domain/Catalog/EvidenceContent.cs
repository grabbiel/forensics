using System.Security.Cryptography;

namespace EvidenceChain.Domain.Catalog;

/// <summary>The evidence file's bytes, stored apart from <see cref="Evidence"/> so lists never load them.</summary>
public sealed class EvidenceContent
{
    /// <summary>Copies the bytes and records their SHA-256 and length.</summary>
    public EvidenceContent(byte[] bytes, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        _bytes = bytes.ToArray();
        _sha256 = SHA256.HashData(_bytes);
        ByteLength = _bytes.Length;
        MediaType = mediaType;
    }

    // Required by EF Core for materialization.
    private EvidenceContent() { }

    public long EvidenceId { get; private set; }

    // Fields, so callers only ever get copies: the hash can never drift from the bytes in memory.
    private byte[] _sha256 = [];
    private byte[] _bytes = [];

    public byte[] Sha256 => _sha256.ToArray();

    public int ByteLength { get; private set; }

    public string MediaType { get; private set; } = string.Empty;

    public byte[] Bytes => _bytes.ToArray();
}
