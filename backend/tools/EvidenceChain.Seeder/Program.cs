using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.Seeder;
using EvidenceChain.SyntheticData;
using Microsoft.Data.SqlClient;

// Evidence Chain seeder: prepare, tracer, generate and reference. seed arrives with the schema (roadmap §2.4).
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

            if (options.TryGetValue("app-login", out var login))
            {
                var password = options.GetValueOrDefault("app-password")
                    ?? throw new ArgumentException("--app-login requires --app-password.");
                await DatabasePreparation.CreateAppLoginAsync(connection, login, password, cancellationToken);
            }

            Console.WriteLine("Database prepared.");
            return 0;
        }

        case "tracer":
        {
            CommandLine.Allow(options, "connection");
            await using var db = SqlServerSetup.CreateContext(RequireConnection());
            var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
            var codes = await TracerData.InsertIfEmptyAsync(db, today, cancellationToken);
            Console.WriteLine(codes.Count == 0 ? "Evidence already present; nothing inserted." : $"Inserted: {string.Join(", ", codes)}");
            return 0;
        }

        case "generate":
        {
            CommandLine.Allow(options, "seed", "anchor", "out", "clean");
            var (seed, anchor) = DatasetFiles.ReadInputs(options);
            var dataset = DatasetBuilder.Build(seed, anchor);
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
            Console.Error.WriteLine("'seed' arrives with the database schema (roadmap §2.4).");
            return 2;

        default:
            Console.WriteLine("""
                Usage:
                  prepare   --connection <cs> [--app-login <name> --app-password <pwd>]   enable RCSI; create the local app login
                  tracer    --connection <cs>                                             insert five tracer rows if empty
                  generate  [--seed 42] [--anchor 2026-10-01T00:00:00Z] [--out <dir>] [--clean]
                                                                                          write 1,000 files and manifest.csv
                  reference [--dir database/synthetic]                                    refresh the committed reference files
                  seed                                                                    arrives with the schema (§2.4)
                """);
            return command == "help" ? 0 : 1;
    }
}
