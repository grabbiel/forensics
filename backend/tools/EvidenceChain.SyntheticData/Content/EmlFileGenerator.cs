using static EvidenceChain.SyntheticData.Content.Invariant;

namespace EvidenceChain.SyntheticData.Content;

/// <summary>RFC 5322 message: 2-10 KiB, UTF-8 8bit body in Spanish, ASCII headers, CRLF, one folded header.</summary>
internal static class EmlFileGenerator
{
    public const int MinBytes = 2048;
    public const int MaxBytes = 10240;

    /// <summary>Lower bound of the large-message fixture, above SQL Server's 8,000-byte in-row limit.</summary>
    public const int LargeMinBytes = 10_000;

    private const int MaxLineBytes = 160;
    private const int WrapColumn = 72;

    private static readonly (string Name, string Address)[] Correspondents =
    [
        ("Javier Perez", "j.perez@example.test"), ("Marta Garcia", "m.garcia@example.com"), ("Alberto Romero", "a.romero@example.test"),
        ("Laura Santos", "l.santos@proveedores.example.test"), ("Raul Navarro", "r.navarro@example.com"), ("Clara Molina", "c.molina@example.test"),
        ("Sergio Ortega", "s.ortega@proveedores.example.test"),
    ];

    // Headers stay ASCII, so subjects carry no accents.
    private static readonly string[] Subjects =
    [
        "Transferencia urgente", "Factura pendiente de pago", "Cambio de cuenta bancaria del proveedor", "Revision de accesos VPN",
        "Confirmacion de pedido", "Documentacion para auditoria", "Pago retenido por cumplimiento",
    ];

    private static readonly string[] Sentences =
    [
        "Le escribo en relación con la transferencia programada para esta semana.",
        "Por favor, confirme el número de cuenta antes de las 18:00.",
        "Adjunto el resumen de movimientos que pidió el área de auditoría.",
        "La reunión de seguimiento se traslada al jueves por la mañana.",
        "El proveedor insistió en que el cambio de cuenta es urgente y confidencial.",
        "Revisé los registros del cortafuegos y no encontré accesos anómalos.",
        "Necesitamos la aprobación del responsable de compras antes del viernes.",
        "La compañía solicitó el mismo cambio el año pasado y se rechazó.",
        "Según el contrato, cualquier modificación debe firmarse por duplicado.",
        "Quedo a la espera de su respuesta para cerrar el expediente.",
        "Le recuerdo que la información es confidencial y no debe reenviarse.",
        "El equipo técnico revisará la incidencia mañana a primera hora.",
    ];

    /// <summary>Generates one message; <paramref name="large"/> forces the 10,000-10,240 byte fixture size.</summary>
    public static (byte[] Content, string Description) Generate(DeterministicRandom random, DateTime capturedAtUtc, bool large)
    {
        var target = large ? LargeMinBytes : FileBuilder.Target(random, MinBytes, MaxBytes, MaxLineBytes);
        var from = random.Pick(Correspondents);
        var to = Other(random, from);
        var cc = random.NextBool(0.4) ? Other(random, from, to) : default;
        var subject = random.Pick(Subjects);
        var reference = F($"(ref. INC-{random.Next(1000, 10000)})");
        // Sent 10 minutes to 36 hours before capture, on a whole second (the Date header has no fraction).
        var sentAt = capturedAtUtc.AddSeconds(-random.Next(10 * 60, 36 * 3600));
        sentAt = new DateTime(sentAt.Ticks - sentAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        var file = new FileBuilder("\r\n", MaxLineBytes);
        file.Line($"From: {from.Name} <{from.Address}>");
        file.Line($"To: {to.Name} <{to.Address}>");
        if (cc != default)
            file.Line($"Cc: {cc.Name} <{cc.Address}>");
        file.Line($"Subject: {subject}");
        file.Line($" {reference}"); // the one folded header: CRLF followed by a space
        file.Line($"Date: {Rfc5322(sentAt)}");
        file.Line(F($"Message-ID: <{random.NextUInt64():x16}@mail.example.test>"));
        file.Line("MIME-Version: 1.0");
        file.Line("Content-Type: text/plain; charset=utf-8");
        file.Line("Content-Transfer-Encoding: 8bit");
        file.Line("");

        file.Line($"Buenos días, señor {to.Name.Split(' ')[1]}:");
        file.Line("");
        while (file.Length < target)
        {
            var paragraph = string.Join(' ', Enumerable.Range(0, random.Next(2, 5)).Select(_ => random.Pick(Sentences)));
            foreach (var line in Wrap(paragraph))
            {
                if (file.Length >= target)
                    break;
                file.Line(line);
            }
            if (file.Length < target)
                file.Line("");
        }

        return (file.ToArray(), $"Correo de {from.Address}: «{subject}»");
    }

    private static (string Name, string Address) Other(DeterministicRandom random, params (string Name, string Address)[] taken)
    {
        while (true)
        {
            var candidate = random.Pick(Correspondents);
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Greedy word wrap at <see cref="WrapColumn"/> characters.</summary>
    private static IEnumerable<string> Wrap(string text)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > WrapColumn)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = line.Length == 0 ? word : $"{line} {word}";
            }
        }
        if (line.Length > 0)
            yield return line;
    }
}
