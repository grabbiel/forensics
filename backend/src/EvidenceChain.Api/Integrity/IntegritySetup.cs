using EvidenceChain.Application.Integrity;
using EvidenceChain.Domain.Integrity;
using Microsoft.Extensions.Options;

namespace EvidenceChain.Api.Integrity;

/// <summary>Bound from "Integrity": the HMAC keys (base64, by id) and the background sweep.</summary>
public sealed class IntegrityOptions
{
    public const string Section = "Integrity";

    /// <summary>The key new events are signed with; the others still verify older events.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    public Dictionary<string, string> Keys { get; set; } = [];

    public IntegritySweepOptions Sweep { get; set; } = new();

    public IntegrityKeyRing ToKeyRing() => IntegrityKeyRing.FromBase64(ActiveKeyId, Keys);

    internal static bool HasUsableKeys(IntegrityOptions options)
    {
        try
        {
            options.ToKeyRing();
            return true;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }
}

/// <summary>Re-verifies a few chains every interval, oldest-checked first, so stored statuses never go stale for long.</summary>
public sealed class IntegritySweepOptions
{
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Small: Azure SQL S1 is under one vCore.</summary>
    public int BatchSize { get; set; } = 25;
}

public static class IntegritySetup
{
    public static IServiceCollection AddEvidenceReview(this IServiceCollection services)
    {
        services.AddOptions<IntegrityOptions>()
            .BindConfiguration(IntegrityOptions.Section)
            .Validate(IntegrityOptions.HasUsableKeys,
                $"Integrity:ActiveKeyId must name a key under Integrity:Keys, each base64 of at least {IntegrityKeyRing.MinKeyBytes} bytes.")
            .Validate(o => o.Sweep.Interval > TimeSpan.Zero && o.Sweep.BatchSize is > 0 and <= 500,
                "Integrity:Sweep needs a positive Interval and a BatchSize from 1 to 500.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<IntegrityOptions>>().Value.ToKeyRing());
        services.AddScoped<ChainVerificationService>();
        services.AddHostedService<IntegritySweeper>();
        return services;
    }
}

internal sealed class IntegritySweeper(
    IServiceScopeFactory scopes, IOptions<IntegrityOptions> options, TimeProvider clock, ILogger<IntegritySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweep = options.Value.Sweep;
        if (!sweep.Enabled)
            return;

        using var timer = new PeriodicTimer(sweep.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var verified = await scope.ServiceProvider.GetRequiredService<ChainVerificationService>().SweepAsync(sweep.BatchSize, stoppingToken);
                logger.LogInformation("Integrity sweep re-verified {Count} evidence chains.", verified);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Integrity sweep failed; the next interval retries.");
            }
        }
    }
}
