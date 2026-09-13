using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Rifles.Procgen.Expeditions;

// Composes existing GraphCore rules and validation; it does not own spatial generation.
public sealed class ExpeditionGenerator
{
    public ExpeditionGenerationResult Generate(ExpeditionDefinition definition, ulong seed)
    {
        try { definition.Validate(); }
        catch (InvalidDataException error) { return new(null, [Fatal("expedition_request_invalid", error.Message)]); }
        GraphCore graph = new();
        List<ResolvedFloorIntent> floors = [];
        foreach (FloorIntentDefinition floor in definition.Floors)
        {
            ulong floorSeed = floor.Id == definition.EntranceFloor ? seed : FloorSeed(seed, floor.Id);
            Candidate candidate = graph.CreateInitial(new SeedIntent(definition.Id + "." + floor.Id, floor.Title, floor.Tags), floorSeed);
            foreach (GraphRule rule in floor.Rules)
            {
                RuleApplication applied = graph.Apply(candidate, rule, floorSeed, definition.Budget.PerFloor);
                if (!applied.Accepted) return new(null, Scope(floor.Id, applied.Diagnostics));
                candidate = applied.Candidate;
            }
            string Prefix(string id) => floor.Id + "/" + id;
            Candidate named = candidate with
            {
                Graph = new CandidateGraph(candidate.Graph.Nodes.Select(n => n with
                {
                    Id = Prefix(n.Id), GrantsItem = n.GrantsItem is null ? null : Prefix(n.GrantsItem),
                    Tags = n.Tags.Concat(floor.Tags).Append("floor-role:" + floor.Role).ToArray(),
                }).ToArray(), candidate.Graph.Edges.Select(e => e with
                {
                    Id = Prefix(e.Id), From = Prefix(e.From), To = Prefix(e.To),
                    RequiredItem = e.RequiredItem is null ? null : Prefix(e.RequiredItem),
                }).ToArray()),
            };
            floors.Add(new(floor.Id, floor.Role, floor.Title, named));
        }
        ExpeditionConnector[] connectors = definition.Connectors.Select(c => new ExpeditionConnector(c.Id,
            c.FromFloor, Terminal(floors.Single(f => f.Id == c.FromFloor), NodeKind.Goal),
            c.ToFloor, Terminal(floors.Single(f => f.Id == c.ToFloor), NodeKind.Start), c.TwoWay)).ToArray();
        ResolvedExpedition expedition = new(seed, definition.Id, definition.Title, "", definition.EntranceFloor,
            definition.ObjectiveFloor, floors.ToArray(), connectors, definition.Budget);
        expedition = expedition with { Identity = CanonicalIdentity.Hash(Compose(expedition)) };
        Diagnostic[] diagnostics = Validate(expedition);
        return new(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Fatal) ? null : expedition, diagnostics);
    }

    public static Diagnostic[] Validate(ResolvedExpedition expedition)
    {
        List<Diagnostic> diagnostics = [];
        if (expedition is null || expedition.Floors is null || expedition.Connectors is null
            || expedition.Floors.Any(f => f is null || f.Candidate is null || f.Candidate.Graph is null
                || f.Candidate.Graph.Nodes is null || f.Candidate.Graph.Edges is null || f.Candidate.Provenance is null)
            || expedition.Connectors.Any(c => c is null))
            return [Fatal("expedition_structure_invalid", "Resolved expedition fields must not be null.")];
        // Validate saved structure independently of today's authored request. Never regenerate a save.
        try
        {
            new ExpeditionDefinition(expedition.Id, expedition.Title, expedition.EntranceFloor, expedition.ObjectiveFloor,
                expedition.Floors.Select(f => new FloorIntentDefinition(f.Id, f.Role, f.Title, [], [])).ToArray(),
                expedition.Connectors.Select(c => new ConnectorDefinition(c.Id, c.FromFloor, c.ToFloor, c.TwoWay)).ToArray(), expedition.Budget).Validate();
        }
        catch (InvalidDataException error) { return [Fatal("expedition_structure_invalid", error.Message)]; }
        foreach (ResolvedFloorIntent floor in expedition.Floors)
        {
            diagnostics.AddRange(Scope(floor.Id, GraphValidator.Validate(floor.Candidate, expedition.Budget.PerFloor).Diagnostics));
            if (floor.Candidate.Graph.Nodes.Any(n => !n.Id.StartsWith(floor.Id + "/", StringComparison.Ordinal))
                || floor.Candidate.Graph.Edges.Any(e => !e.Id.StartsWith(floor.Id + "/", StringComparison.Ordinal)))
                diagnostics.Add(Fatal("floor_identity_scope", "Graph feature identities must belong to floor " + floor.Id));
        }
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Fatal)) return diagnostics.ToArray();
        foreach (ExpeditionConnector connector in expedition.Connectors)
        {
            var from = expedition.Floors.Single(f => f.Id == connector.FromFloor);
            var to = expedition.Floors.Single(f => f.Id == connector.ToFloor);
            if (connector.FromNode != Terminal(from, NodeKind.Goal) || connector.ToNode != Terminal(to, NodeKind.Start))
                diagnostics.Add(Fatal("connector_endpoint_invalid", "Connector " + connector.Id + " must join its resolved floor terminals."));
        }
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Fatal)) return diagnostics.ToArray();
        Candidate combined = Compose(expedition);
        diagnostics.AddRange(GraphValidator.Validate(combined, expedition.Budget.Total).Diagnostics);
        ProgressionReport reached = GraphValidator.ReachableWithItems(combined);
        foreach (ResolvedFloorIntent floor in expedition.Floors)
            if (!reached.ReachedNodes.Contains(Terminal(floor, NodeKind.Start)))
                diagnostics.Add(Fatal("floor_unreachable", "No route reaches floor " + floor.Id));
        if (CanonicalIdentity.Hash(combined) != expedition.Identity)
            diagnostics.Add(Fatal("expedition_identity_mismatch", "Resolved expedition identity does not match its graph."));
        return diagnostics.ToArray();
    }

    public static Candidate Compose(ResolvedExpedition expedition)
    {
        string start = Terminal(expedition.Floors.Single(f => f.Id == expedition.EntranceFloor), NodeKind.Start);
        string goal = Terminal(expedition.Floors.Single(f => f.Id == expedition.ObjectiveFloor), NodeKind.Goal);
        GraphNode[] nodes = expedition.Floors.SelectMany(f => f.Candidate.Graph.Nodes.Select(n => n with
        {
            Kind = n.Id == start ? NodeKind.Start : n.Id == goal ? NodeKind.Goal
                : n.Kind is NodeKind.Start or NodeKind.Goal ? NodeKind.Junction : n.Kind,
            Tags = n.Tags.Append("floor:" + f.Id).Append("role:" + f.Role).Append("title:" + f.Title).ToArray(),
        })).ToArray();
        List<GraphEdge> edges = expedition.Floors.SelectMany(f => f.Candidate.Graph.Edges).ToList();
        foreach (var connector in expedition.Connectors)
        {
            edges.Add(new("connector/" + connector.Id + "/forward", connector.FromNode, connector.ToNode,
                EdgeKind.CriticalPath, TraversalKind.Open, ["floor-connector"]));
            if (connector.TwoWay) edges.Add(new("connector/" + connector.Id + "/return", connector.ToNode, connector.FromNode,
                EdgeKind.Shortcut, TraversalKind.Open, ["floor-connector", "return"]));
        }
        var provenance = expedition.Floors.OrderBy(f => f.Id, StringComparer.Ordinal).SelectMany(f =>
            f.Candidate.Provenance.Append(new ProvenanceStep(0, "floor-intent", f.Candidate.Seed,
                f.Id + ":" + CanonicalIdentity.Hash(f.Candidate))))
            .Select((p, i) => p with { Step = i + 1 }).ToArray();
        return new(expedition.Id, expedition.Seed, expedition.Title, new(nodes, edges), provenance);
    }

    private static string Terminal(ResolvedFloorIntent floor, NodeKind kind) => floor.Candidate.Graph.Nodes.Single(n => n.Kind == kind).Id;
    private static ulong FloorSeed(ulong seed, string id) => BinaryPrimitives.ReadUInt64LittleEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + id)));
    private static Diagnostic Fatal(string code, string detail) => new(code, DiagnosticSeverity.Fatal, detail);
    private static Diagnostic[] Scope(string floor, IEnumerable<Diagnostic> diagnostics) => diagnostics
        .Select(d => d with { Detail = floor + ": " + d.Detail }).ToArray();
}
