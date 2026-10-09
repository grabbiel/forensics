using static EvidenceChain.SyntheticData.Content.Invariant;

namespace EvidenceChain.SyntheticData.Content;

/// <summary>Firewall log in RFC 5424 syslog: 1-5 KiB, UTF-8, LF, ascending timestamps over a two-hour window.</summary>
internal static class LogFileGenerator
{
    public const int MinBytes = 1024;
    public const int MaxBytes = 5120;
    private const int MaxLineBytes = 256;

    private static readonly string[] Hosts = ["fw-edge-01", "fw-edge-02", "fw-dmz-01", "fw-core-01"];
    private static readonly int[] ServicePorts = [443, 443, 443, 80, 53, 123, 22, 8443, 25];
    private static readonly int[] ProbedPorts = [22, 23, 445, 3389];

    /// <summary>Generates the file and its description. About 10% of files hold a DENY burst from one source.</summary>
    public static (byte[] Content, string Description) Generate(DeterministicRandom random, DateTime capturedAtUtc)
    {
        var host = random.Pick(Hosts);
        var procId = random.Next(1000, 10000);
        var windowEnd = new DateTime(capturedAtUtc.Year, capturedAtUtc.Month, capturedAtUtc.Day, capturedAtUtc.Hour, 0, 0, DateTimeKind.Utc);
        var windowStart = windowEnd.AddHours(-2);
        var target = FileBuilder.Target(random, MinBytes, MaxBytes, MaxLineBytes);

        var file = new FileBuilder("\n", MaxLineBytes);
        var at = windowStart.AddMilliseconds(random.Next(0, 60_000));
        // Early enough that even a 1 KiB file reaches it.
        var burstAtLine = random.NextBool(0.10) ? random.Next(1, 4) : -1;

        for (var line = 0; file.Length < target; line++)
        {
            if (line == burstAtLine)
            {
                var attacker = F($"203.0.113.{random.Next(2, 255)}");
                var port = random.Pick(ProbedPorts);
                for (var i = random.Next(6, 11); i > 0 && file.Length < target; i--)
                {
                    at = at.AddMilliseconds(random.Next(200, 900));
                    file.Line(Entry(host, procId, at, "DENY", "TCP", attacker, random.Next(49152, 65536), Server(random), port, random.Next(40, 80)));
                }
                continue;
            }

            // Up to ~45 lines at 1-150 s apart stay inside the two-hour window.
            at = at.AddMilliseconds(random.Next(1_000, 150_000));
            var dpt = random.Pick(ServicePorts);
            var proto = dpt is 53 or 123 ? "UDP" : "TCP";
            var action = random.NextBool(0.05) ? "DENY" : "ALLOW";
            file.Line(Entry(host, procId, at, action, proto, F($"192.0.2.{random.Next(10, 251)}"), random.Next(49152, 65536), Server(random), dpt, random.Next(60, 24_000)));
        }

        var description = F($"Log del firewall {host}, {windowStart:HH}:00-{windowEnd:HH}:00 UTC");
        return (file.ToArray(), description);
    }

    private static string Server(DeterministicRandom random) => F($"198.51.100.{random.Next(2, 255)}");

    /// <summary>One syslog line: local0.info for ALLOW, local0.warning for DENY.</summary>
    private static string Entry(string host, int procId, DateTime at, string action, string proto, string src, int spt, string dst, int dpt, int bytes) =>
        F($"<{(action == "DENY" ? 132 : 134)}>1 {Iso(at)} {host}.example.test filterlog {procId} - - action={action} proto={proto} src={src} spt={spt} dst={dst} dpt={dpt} bytes={bytes}");
}
