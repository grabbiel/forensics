namespace EvidenceChain.SyntheticData;

/// <summary>The fixed cast: 3 Investigadores, 6 Custodios and 2 Supervisores, one demo account per role.</summary>
public static class SyntheticPeople
{
    /// <summary>Custodio who receives the live demo transfer.</summary>
    public const string DemoCustodian = "custodio.demo";

    /// <summary>Everyone, in id order; read-only so no caller can change later builds.</summary>
    public static IReadOnlyList<SyntheticUser> All { get; } = Array.AsReadOnly<SyntheticUser>(
    [
        new(1, "investigador.demo", "Lucía Ferrer", SyntheticRole.Investigador),
        new(2, "martin.ochoa", "Martín Ochoa", SyntheticRole.Investigador),
        new(3, "irene.calvo", "Irene Calvo", SyntheticRole.Investigador),
        new(4, DemoCustodian, "Diego Salas", SyntheticRole.Custodio),
        new(5, "nuria.paredes", "Nuria Paredes", SyntheticRole.Custodio),
        new(6, "oscar.villalba", "Óscar Villalba", SyntheticRole.Custodio),
        new(7, "carmen.robles", "Carmen Robles", SyntheticRole.Custodio),
        new(8, "hector.lozano", "Héctor Lozano", SyntheticRole.Custodio),
        new(9, "paula.benitez", "Paula Benítez", SyntheticRole.Custodio),
        new(10, "supervisor.demo", "Elena Ruiz", SyntheticRole.Supervisor),
        new(11, "tomas.herrera", "Tomás Herrera", SyntheticRole.Supervisor),
    ]);

    /// <summary>User names with the given role, in id order.</summary>
    public static IReadOnlyList<string> WithRole(SyntheticRole role) =>
        All.Where(u => u.Role == role).Select(u => u.UserName).ToArray();
}
