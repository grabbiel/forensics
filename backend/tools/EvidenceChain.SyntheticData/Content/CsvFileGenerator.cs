using static EvidenceChain.SyntheticData.Content.Invariant;

namespace EvidenceChain.SyntheticData.Content;

/// <summary>Bank export in RFC 4180 CSV: 1-5 KiB, UTF-8, CRLF, amounts with a dot and currency XTS.</summary>
internal static class CsvFileGenerator
{
    public const int MinBytes = 1024;
    public const int MaxBytes = 5120;
    public const string Header = "TransactionId,BookingDate,ValueDate,Account,Counterparty,Description,Amount,Currency,Balance";
    private const int MaxLineBytes = 256;

    // A comma and embedded quotes exercise RFC 4180 quoting.
    private static readonly string[] Counterparties =
    [
        "Distribuciones Norte, S.L.", "Suministros Atlántico S.A.", "Taller \"El Puente\"", "Consultoría Vega y Asociados",
        "Transportes Ruta 9", "Papelería Central", "Nóminas internas", "Cliente 1042",
    ];

    private static readonly (string Text, bool Credit)[] Concepts =
    [
        ("Pago factura {0}", false), ("Cobro factura {0}", true), ("Transferencia nómina", false), ("Comisión mantenimiento", false),
        ("Recibo suministro eléctrico", false), ("Devolución pedido {0}", true), ("Ingreso por servicios {0}", true),
    ];

    /// <summary>Generates one statement covering the month before capture, rows in booking order.</summary>
    public static (byte[] Content, string Description) Generate(DeterministicRandom random, DateTime capturedAtUtc)
    {
        var account = F($"XA00-{random.Next(1, 10000):D4}");
        var monthStart = new DateOnly(capturedAtUtc.Year, capturedAtUtc.Month, 1).AddMonths(-1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var target = FileBuilder.Target(random, MinBytes, MaxBytes, MaxLineBytes);

        var file = new FileBuilder("\r\n", MaxLineBytes);
        file.Line(Header);

        var booking = monthStart;
        var transactionNo = random.Next(1, 5000);
        var balanceCents = random.NextInt64(1_000_000, 20_000_000);
        while (file.Length < target)
        {
            if (booking < monthEnd && random.NextBool(0.5))
                booking = booking.AddDays(1);

            var (concept, credit) = random.Pick(Concepts);
            var amountCents = random.NextInt64(500, 950_000) * (credit ? 1 : -1);
            balanceCents += amountCents;
            string[] fields =
            [
                F($"TX{booking:yyyyMMdd}-{transactionNo++:D4}"),
                Date(booking),
                Date(booking.AddDays(random.Next(0, 3))),
                account,
                random.Pick(Counterparties),
                string.Format(System.Globalization.CultureInfo.InvariantCulture, concept, random.Next(100, 10000)),
                Money(amountCents),
                "XTS",
                Money(balanceCents),
            ];
            file.Line(string.Join(',', fields.Select(Quote)));
        }

        var kind = random.NextBool(0.5) ? "Extracto bancario" : "Movimientos de cuenta";
        return (file.ToArray(), $"{kind}, cuenta {account}, {SpanishMonthYear(monthStart)}");
    }

    /// <summary>RFC 4180: quote fields holding a comma, quote or line break; double inner quotes.</summary>
    internal static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
