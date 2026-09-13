using Rifles.Game.Content;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;

internal static class ExpeditionChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        ExpeditionDefinition request = definitions.Generation.Expedition;
        ExpeditionGenerator generator = new();
        ResolvedExpedition Generate(ulong seed)
        {
            var result = generator.Generate(request, seed);
            Require(result.Accepted, "Authored expedition must resolve: " + string.Join(",", result.Diagnostics.Select(d => d.Code)));
            return result.Expedition!;
        }
        foreach (ulong seed in new ulong[] { 0, 1, 29, 83 })
        {
            var resolved = Generate(seed);
            Require(resolved.Identity == Generate(seed).Identity, "Same request and seed resolve the same expedition.");
            Require(resolved.Floors.Select(f => f.Role).Distinct().Count() == 3, "Authored floors have distinct purposes.");
            Require(resolved.Floors.Select(f => string.Join(',', f.Candidate.Graph.Nodes.GroupBy(n => n.Kind)
                .OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count()))).Distinct().Count() == 3,
                "Distinct roles differ in graph structure, not just seed or labels.");
            Require(GraphValidator.ReachableWithItems(ExpeditionGenerator.Compose(resolved)).GoalReached,
                "The main expedition objective is reachable with its actual key dependencies.");
            Require(resolved.Floors.All(f => f.Candidate.Graph.Nodes.Any(n => n.Kind == NodeKind.Treasure || n.Kind == NodeKind.Resource)),
                "Every floor supplies a meaningful branch reward or preparation choice.");
            var reordered = generator.Generate(request with { Floors = request.Floors.Reverse().ToArray() }, seed).Expedition!;
            Require(reordered.Identity == resolved.Identity && reordered.Floors.All(f =>
                CanonicalIdentity.Hash(f.Candidate) == CanonicalIdentity.Hash(resolved.Floors.Single(a => a.Id == f.Id).Candidate)),
                "Floor seeds and identities do not depend on catalogue ordering.");
        }
        var disconnected = generator.Generate(request with { Connectors = [] }, 29);
        Require(!disconnected.Accepted && disconnected.Expedition is null
            && disconnected.Diagnostics.Any(d => d.Code == "goal_unreachable"), "Disconnected main objectives fail with a reason.");
        var exhausted = generator.Generate(request with { Budget = request.Budget with { Total = new GraphBudget(2, 1, 1) } }, 29);
        Require(!exhausted.Accepted && exhausted.Diagnostics.Any(d => d.Code == "node_quota_exceeded"), "Total graph budget bounds composed expeditions.");
        var duplicateRule = request.Floors[1] with { Rules = [GraphRule.LockKeyLoop, GraphRule.LockKeyLoop] };
        var failedRule = generator.Generate(request with { Floors = [request.Floors[0], duplicateRule, request.Floors[2]] }, 29);
        Require(!failedRule.Accepted && failedRule.Diagnostics.Any(d => d.Code == "rule_already_applied"), "Rejected rules terminate generation without retrying or leaking a partial expedition.");
        var saved = Generate(29);
        var corruptConnector = saved.Connectors[0] with { FromNode = "missing" };
        Require(ExpeditionGenerator.Validate(saved with { Connectors = [corruptConnector, saved.Connectors[1]] })
            .Any(d => d.Code == "connector_endpoint_invalid"), "Save validation rejects disconnected connector identities.");
        var first = saved.Floors[0];
        var severed = first with { Candidate = first.Candidate with { Graph = first.Candidate.Graph with { Edges = [] } } };
        Require(ExpeditionGenerator.Validate(saved with { Floors = [severed, .. saved.Floors.Skip(1)] })
            .Any(d => d.Code == "goal_unreachable"), "Save validation rechecks floor objective reachability.");
        Require(ExpeditionGenerator.Validate(saved with { Identity = "tampered" })
            .Any(d => d.Code == "expedition_identity_mismatch"), "Save identity detects graph changes.");
        Console.WriteLine("Expedition graph checks passed: distinct roles, keyed progression, stable identities and bounded rejection.");
    }
}
