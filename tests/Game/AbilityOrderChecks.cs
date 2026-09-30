using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Magic;
using Rifles.Game.Generation;
using Rifles.Procgen;
using Rifles.Procgen.Generation;
using Rusty.Engine.Interaction;

internal static class AbilityOrderChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifyForwardTargeting(definitions);
        VerifySharedProviderChoice();
        VerifySecondGeneratedLeverCast(definitions);
    }

    private static void VerifyForwardTargeting(GameDefinitions definitions)
    {
        SpellDefinition spark = definitions.Magic.Spell("spark");
        Require(ForwardAbilityRules.InOriginalForwardArea(spark, 5, 1)
            && !ForwardAbilityRules.InOriginalForwardArea(spark, -1, 0)
            && !ForwardAbilityRules.InOriginalForwardArea(spark, -2, .5f)
            && !ForwardAbilityRules.InOriginalForwardArea(spark, spark.Range + .1f, 0)
            && !ForwardAbilityRules.InOriginalForwardArea(spark, 5, 6),
            "A hostile spell retains only its authored original forward cone and range.");
        Require(ForwardAbilityRules.Select(definitions.Formation, "front-center",
            [new ForwardAbilityCandidate(8, 5, 0, true), new ForwardAbilityCandidate(3, 4, -.7f, true)]) == 8,
            "A caster prefers an exposed hostile in its own forward lane.");
        Require(ForwardAbilityRules.Select(definitions.Formation, "front-left",
            [new ForwardAbilityCandidate(4, 4, .8f, true)]) == 4,
            "A caster falls back to an adjacent permitted lane when its own lane is empty.");
        Require(ForwardAbilityRules.Select(definitions.Formation, "front-center",
            [new ForwardAbilityCandidate(9, 4, 0, false)]) is null,
            "Blocked, fallen, or otherwise unexposed candidates never produce a hostile cast.");
    }

    private static void VerifySharedProviderChoice()
    {
        AbilityProviderReadout[] providers =
        [
            new("mender", true, "Ready.", "", ""),
            new("blade", true, "Ready.", "", ""),
            new("warden", false, "Busy.", "", ""),
            new("fallen", false, "Fallen.", "", ""),
        ];
        Require(AbilityOrderRules.SharedEffectOwner(providers) == "blade",
            "One stable ready provider settles a shared effect while every ready provider still acts.");
        Require(AbilityOrderRules.SharedEffectOwner(providers.Where(provider => !provider.Eligible)) is null,
            "Busy and fallen providers cannot be designated for a shared party effect.");
    }

    private static void VerifySecondGeneratedLeverCast(GameDefinitions definitions)
    {
        GeneratedGate first = new(1, "first", new(0, 0), new(0, -1), TraversalKind.Hidden, null, false, false);
        GeneratedGate second = new(2, "second", new(2, 0), new(2, -1), TraversalKind.Hidden, null, true, false);
        GeneratedFeatureSnapshot live = new(1, [first, second], [], [], [], []);
        List<GridPoint> opened = [];
        void Use(InteractionTarget target) => live = GeneratedFeatureState.UseGate(live, target,
            definitions.GeneratedFeatures.Presentation, _ => true, _ => null, opened.Add).Snapshot;
        Use(new(first.Id, live.Revision));
        Use(new(first.Id, live.Revision));
        Require(live.Gates[0].Open && opened.Contains(first.Cell), "Opening the first generated gate updates its live state and door.");
        var choice = GeneratedFeatures.AbilityTarget(live, new(second.Id, 1), 90, 25);
        Require(choice.Target == second.Id && choice.Revision == live.Revision,
            "A second Lever cast selects the current generated revision instead of the first floor or item revision.");
        SpellDefinition lever = definitions.Magic.Spells.Single(spell => spell.Effect == SpellEffect.Lever);
        ActionState action = new();
        action.Start(new(CombatActionKind.Cast, 0, null, null, choice.Target, null, lever.Windup,
            ActionPhase.Windup, lever.Recovery, Spell: lever.Id, Cost: lever.Cost, FeatureRevision: choice.Revision));
        action.Advance(lever.Windup + lever.Recovery,
            commit => Use(new(commit.Target, commit.FeatureRevision)));
        Require(live.Gates[1].Open && opened.Contains(second.Cell) && !action.Busy,
            "Lever on the second generated gate commits after the first mutation and completes its authored phases.");
        Check.Rejected(() => Use(new(second.Id, choice.Revision)), "A genuinely stale generated target is refused.",
            definitions.GeneratedFeatures.Presentation.HandleChanged);
        var fallback = GeneratedFeatures.AbilityTarget(live, null, 90, 25);
        Require(fallback == (90UL, 25UL), "The ordinary lever retains its independent item-world revision.");
    }

    private static void Require(bool condition, string message)
    {
        Check.Require(condition, message);
    }
}
