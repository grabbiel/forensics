namespace EvidenceChain.Seeder;

/// <summary>Minimal parser: a command followed by --key value pairs or known bare --flags.</summary>
internal static class CommandLine
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "clean", "if-empty", "reset" };

    /// <summary>Splits arguments into a command and its options; a value-taking option without a value is an error.</summary>
    public static (string Command, IReadOnlyDictionary<string, string> Options) Parse(string[] args)
    {
        var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "help";
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = command == "help" ? 0 : 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{args[i]}'.");

            var key = args[i][2..];
            if (Flags.Contains(key))
            {
                options[key] = "true";
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"--{key} needs a value.");
            options[key] = args[++i];
        }

        return (command, options);
    }

    /// <summary>Rejects options the command does not take, so typos fail loudly.</summary>
    public static void Allow(IReadOnlyDictionary<string, string> options, params string[] names)
    {
        var unknown = options.Keys.Except(names, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown.Select(k => $"--{k}"))}.");
    }
}
