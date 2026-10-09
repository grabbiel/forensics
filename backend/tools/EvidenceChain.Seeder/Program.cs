using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.Seeder;
using Microsoft.Data.SqlClient;

// Evidence Chain seeder. Day 1: prepare + tracer. Day 2 adds generate + seed (roadmap §2.3–§2.4).
var (command, options) = CommandLine.Parse(args);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

string RequireConnection() =>
    options.GetValueOrDefault("connection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? throw new ArgumentException("Pass --connection or set ConnectionStrings__Default.");

switch (command)
{
    case "prepare":
    {
        await using var connection = new SqlConnection(RequireConnection());
        await connection.OpenAsync(cancellation.Token);
        await DatabasePreparation.EnableReadCommittedSnapshotAsync(connection, cancellation.Token);

        if (options.TryGetValue("app-login", out var login))
        {
            var password = options.GetValueOrDefault("app-password")
                ?? throw new ArgumentException("--app-login requires --app-password.");
            await DatabasePreparation.CreateAppLoginAsync(connection, login, password, cancellation.Token);
        }

        Console.WriteLine("Database prepared.");
        return 0;
    }

    case "tracer":
    {
        await using var db = SqlServerSetup.CreateContext(RequireConnection());
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        var codes = await TracerData.InsertIfEmptyAsync(db, today, cancellation.Token);
        Console.WriteLine(codes.Count == 0 ? "Evidence already present; nothing inserted." : $"Inserted: {string.Join(", ", codes)}");
        return 0;
    }

    case "generate":
    case "seed":
        Console.Error.WriteLine($"'{command}' arrives on Day 2 (roadmap §2.3–§2.4).");
        return 2;

    default:
        Console.WriteLine("""
            Usage:
              prepare --connection <cs> [--app-login <name> --app-password <pwd>]   enable RCSI; create the local app login
              tracer  --connection <cs>                                             insert five tracer rows if empty
              generate | seed                                                       Day 2
            """);
        return command == "help" ? 0 : 1;
}
