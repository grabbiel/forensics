using System.ComponentModel.DataAnnotations;

namespace EvidenceChain.Api.Http;

/// <summary>
/// A length limit on the value as stored and fingerprinted, without surrounding whitespace, so a retry that differs
/// only in padding validates exactly like the original.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TrimmedLengthAttribute(int minimum, int maximum)
    : ValidationAttribute($"Use {minimum} to {maximum} characters, surrounding spaces aside.")
{
    public override bool IsValid(object? value) =>
        value is null || (value is string text && text.Trim().Length >= minimum && text.Trim().Length <= maximum);
}
