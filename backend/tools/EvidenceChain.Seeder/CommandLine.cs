namespace EvidenceChain.Seeder;

/// <summary>Minimal parser: a command followed by --key value pairs or bare --flags.</summary>
internal static class CommandLine
{
    /// <summary>Splits arguments into a command and its options.</summary>
    public static (string Command, IReadOnlyDictionary<string, string> Options) Parse(string[] args)
    {
        var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "help";
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = command == "help" ? 0 : 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;

            var key = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options[key] = hasValue ? args[++i] : "true";
        }

        return (command, options);
    }
}
