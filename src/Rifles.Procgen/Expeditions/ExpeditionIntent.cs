namespace Rifles.Procgen.Expeditions;

public sealed record FloorIntentDefinition(string Id, string Role, string Title, string[] Tags, GraphRule[] Rules);
public sealed record ConnectorDefinition(string Id, string FromFloor, string ToFloor, bool TwoWay);
public sealed record ExpeditionBudget(int MaxFloors, int MaxConnectors, GraphBudget PerFloor, GraphBudget Total);
public sealed record ExpeditionDefinition(string Id, string Title, string EntranceFloor, string ObjectiveFloor,
    FloorIntentDefinition[] Floors, ConnectorDefinition[] Connectors, ExpeditionBudget Budget)
{
    public void Validate()
    {
        // Admission ceilings protect bounded algorithms; authored budgets are normally much smaller.
        static bool Key(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
        static bool GraphBound(GraphBudget? b) => b is { MaxNodes: >= 2 and <= 4096,
            MaxEdges: >= 1 and <= 8192, MaxRuleApplications: >= 0 and <= 1024 };
        static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string field)
        {
            if (!value) throw new InvalidDataException("Invalid expedition " + field + ".");
        }
        Require(Key(Id) && !string.IsNullOrWhiteSpace(Title), "identity/title");
        Require(Budget is { MaxFloors: >= 1 and <= 64, MaxConnectors: >= 0 and <= 256 }
            && GraphBound(Budget.PerFloor) && GraphBound(Budget.Total), "budgets");
        Require(Floors is { Length: > 0 } && Floors.Length <= Budget.MaxFloors, "floor budget");
        Require(Floors.All(f => f is not null && Key(f.Id) && Key(f.Role) && !string.IsNullOrWhiteSpace(f.Title)
            && f.Tags is not null && f.Tags.All(t => !string.IsNullOrWhiteSpace(t))
            && f.Rules is not null && f.Rules.Length <= Budget.PerFloor.MaxRuleApplications
            && f.Rules.All(Enum.IsDefined)), "floor definitions");
        Require(Floors.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() == Floors.Length, "unique floor ids");
        var ids = Floors.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        Require(ids.Contains(EntranceFloor) && ids.Contains(ObjectiveFloor), "entrance/objective floor");
        Require(Connectors is not null && Connectors.Length <= Budget.MaxConnectors, "connector budget");
        Require(Connectors.All(c => c is not null && Key(c.Id) && ids.Contains(c.FromFloor)
            && ids.Contains(c.ToFloor) && c.FromFloor != c.ToFloor), "connector endpoints");
        Require(Connectors.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == Connectors.Length, "unique connector ids");
    }
}

// Graph identities are namespaced by authored floor IDs, never by list positions.
public sealed record ResolvedFloorIntent(string Id, string Role, string Title, Candidate Candidate);
public sealed record ExpeditionConnector(string Id, string FromFloor, string FromNode, string ToFloor, string ToNode, bool TwoWay);
public sealed record ResolvedExpedition(ulong Seed, string Id, string Title, string Identity, string EntranceFloor,
    string ObjectiveFloor, ResolvedFloorIntent[] Floors, ExpeditionConnector[] Connectors, ExpeditionBudget Budget);
public sealed record ExpeditionGenerationResult(ResolvedExpedition? Expedition, Diagnostic[] Diagnostics)
{
    public bool Accepted => Expedition is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Fatal);
}
