using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Magic;

internal static class AbilityOrderChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifyForwardTargeting(definitions);
        VerifySharedProviderChoice();
        Console.WriteLine("Ability order checks passed: forward targeting and shared provider coordination.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
