using EvidenceChain.Seeder;
using EvidenceChain.SyntheticData;
using Microsoft.Data.SqlClient;

// Evidence Chain seeder: prepare the database, export or load the synthetic dataset.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

try
{
    return await RunAsync(args, cancellation.Token);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
{
    var (command, options) = CommandLine.Parse(args);

    string RequireConnection() =>
        options.GetValueOrDefault("connection")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? throw new ArgumentException("Pass --connection or set ConnectionStrings__Default.");

    switch (command)
    {
        case "prepare":
        {
            CommandLine.Allow(options, "connection", "app-login", "app-password");
            await using var connection = new SqlConnection(RequireConnection());
            await connection.OpenAsync(cancellationToken);
            await DatabasePreparation.EnableReadCommittedSnapshotAsync(connection, cancellationToken);
            await DatabasePreparation.EnableSnapshotIsolationAsync(connection, cancellationToken);

            if (options.TryGetValue("app-login", out var login))
            {
                var password = options.GetValueOrDefault("app-password")
                    ?? throw new ArgumentException("--app-login requires --app-password.");
                await DatabasePreparation.CreateAppLoginAsync(connection, login, password, cancellationToken);
            }

            Console.WriteLine("Database prepared.");
            return 0;
        }

        case "generate":
        {
            CommandLine.Allow(options, "seed", "anchor", "profile", "evidences", "events", "max-transfers", "days", "out", "clean");
            var (seed, anchor, profile) = DatasetFiles.ReadInputs(options);
            var dataset = DatasetBuilder.Build(seed, anchor, profile);
            var directory = options.GetValueOrDefault("out") ?? Path.Combine("database", "synthetic", "out");
            await DatasetFiles.ExportAsync(dataset, directory, clean: options.ContainsKey("clean"), cancellationToken);
            DatasetFiles.PrintSummary(dataset);
            Console.WriteLine($"Wrote {dataset.Evidences.Count} files and manifest.csv to {Path.GetFullPath(directory)}");
            return 0;
        }

        case "reference":
        {
            CommandLine.Allow(options, "dir");
            var dataset = DatasetBuilder.Build(ReferenceDataset.Seed, ReferenceDataset.AnchorUtc);
            var directory = options.GetValueOrDefault("dir") ?? Path.Combine("database", "synthetic");
            await DatasetFiles.WriteReferenceAsync(dataset, directory, cancellationToken);
            DatasetFiles.PrintSummary(dataset);
            Console.WriteLine($"Wrote {ReferenceDataset.ManifestFileName} and {ReferenceDataset.SamplesFolder}/ to {Path.GetFullPath(directory)}");
            return 0;
        }

        case "seed":
        {
            CommandLine.Allow(options, "connection", "seed", "anchor", "profile", "evidences", "events", "max-transfers", "days", "if-empty", "reset");
            if (options.ContainsKey("if-empty") && options.ContainsKey("reset"))
                throw new ArgumentException("--if-empty and --reset contradict each other; pass one.");
            var mode = options.ContainsKey("reset") ? SeedMode.Reset : options.ContainsKey("if-empty") ? SeedMode.IfEmpty : SeedMode.Strict;

            var (seed, anchor, profile) = DatasetFiles.ReadInputs(options);
            var connection = RequireConnection();
            var keys = IntegrityKeys.Load();
            IntegrityKeys.EnsureFitFor(keys, connection);
            Console.WriteLine($"Seeding {profile.Spec} (seed {seed}, anchor {anchor:yyyy-MM-ddTHH:mm:ssZ}) with integrity key '{keys.ActiveKeyId}'.");

            var dataset = DatasetBuilder.Build(seed, anchor, profile);
            // Recorded in SeedRuns; the connection string stays out because it may hold a password.
            var recorded = string.Join(' ', args.Where((a, i) => a != "--connection" && (i == 0 || args[i - 1] != "--connection")));
            var result = await DatasetLoader.SeedAsync(connection, dataset, keys, mode, recorded, cancellationToken);

            Console.WriteLine(result.AlreadySeeded
                ? $"Seed {seed} ({profile.Name}) is already loaded; nothing to do."
                : $"Loaded {result.Users} users, {result.Evidences} evidences, {result.Transfers} transfers and {result.Events} events; tamper fixtures applied.");
            if (!result.AlreadySeeded)
                DatasetFiles.PrintSummary(dataset);
            return 0;
        }

        default:
            Console.WriteLine("""
                Usage:
                  prepare   --connection <cs> [--app-login <name> --app-password <pwd>]   enable RCSI; create the local app login
                  generate  [--seed 42] [--anchor 2026-10-01T00:00:00Z] [--out <dir>] [--clean]
                            [--profile reference|scale] [--evidences N] [--events N] [--max-transfers N] [--days N]
                                                                                          write the files and manifest.csv
                  reference [--dir database/synthetic]                                    refresh the committed reference files
                  seed      --connection <cs> [--seed 42] [--anchor <utc>] [--if-empty | --reset] [size options as for generate]
                                                                                          load the dataset, MAC'd with the active integrity key
                """);
            return command == "help" ? 0 : 1;
    }
}
