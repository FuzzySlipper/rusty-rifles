using Rifles.Game.Content;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Magic;

internal enum SpellTarget
{
    Enemy,
    Ally,
    Party,
    Feature,
}

internal enum SpellEffect
{
    Damage,
    Defense,
    Injury,
    Slow,
    Heal,
    Cleanse,
    Light,
    Reveal,
    Lever,
    Revive,
}

internal sealed record SpellDefinition(
    string Id,
    string Name,
    string Description,
    SpellTarget Target,
    SpellEffect Effect,
    long Cost,
    double Windup,
    double Recovery,
    float Range,
    float Speed,
    float Radius,
    long Power,
    double Duration,
    double Period,
    float SpeedFactor,
    bool Harmful,
    EffectStackingPolicy Stacking = EffectStackingPolicy.Refresh)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id)
            && !string.IsNullOrWhiteSpace(Name)
            && !string.IsNullOrWhiteSpace(Description), "spell metadata");
        GameDefinitions.Require(Enum.IsDefined(Target) && Enum.IsDefined(Effect), "spell target/effect");
        GameDefinitions.Require(Stacking == EffectStackingPolicy.Refresh, "spell stacking " + Id);
        GameDefinitions.Require(Cost is > 0 and <= MaximumAuthoredValue && double.IsFinite(Windup) && Windup > 0
            && double.IsFinite(Recovery) && Recovery > 0 && float.IsFinite(Range) && Range > 0
            && float.IsFinite(Speed) && Speed >= 0 && float.IsFinite(Radius) && Radius >= 0
            && Power is >= 0 and <= MaximumAuthoredValue && double.IsFinite(Duration) && Duration >= 0
            && double.IsFinite(Period) && Period >= 0 && float.IsFinite(SpeedFactor) && SpeedFactor is > 0 and <= 1,
            "spell tuning " + Id);

        SpellTarget expectedTarget = Effect switch
        {
            SpellEffect.Damage or SpellEffect.Injury or SpellEffect.Slow => SpellTarget.Enemy,
            SpellEffect.Defense or SpellEffect.Heal or SpellEffect.Cleanse or SpellEffect.Revive => SpellTarget.Ally,
            SpellEffect.Light or SpellEffect.Reveal => SpellTarget.Party,
            SpellEffect.Lever => SpellTarget.Feature,
            _ => throw new InvalidDataException("Unknown spell effect."),
        };
        GameDefinitions.Require(Target == expectedTarget, "spell target " + Id);
        GameDefinitions.Require(Harmful == (Effect is SpellEffect.Damage or SpellEffect.Injury or SpellEffect.Slow),
            "spell harmful flag " + Id);

        switch (Effect)
        {
            case SpellEffect.Damage:
                GameDefinitions.Require(Power > 0 && Speed > 0, "damage spell " + Id);
                break;
            case SpellEffect.Defense:
                GameDefinitions.Require(Power > 0 && Duration > 0, "defense spell " + Id);
                break;
            case SpellEffect.Injury:
                GameDefinitions.Require(Power > 0 && Speed > 0 && Duration > 0 && Period > 0, "injury spell " + Id);
                break;
            case SpellEffect.Slow:
                GameDefinitions.Require(Speed > 0 && Duration > 0 && SpeedFactor < 1, "slow spell " + Id);
                break;
            case SpellEffect.Heal:
                GameDefinitions.Require(Power > 0, "heal spell " + Id);
                break;
            case SpellEffect.Cleanse:
                GameDefinitions.Require(Duration == 0 && Period == 0, "cleanse spell " + Id);
                break;
            case SpellEffect.Light:
                GameDefinitions.Require(Duration > 0, "light spell " + Id);
                break;
            case SpellEffect.Reveal:
                GameDefinitions.Require(Radius > 0 && Duration > 0, "reveal spell " + Id);
                break;
            case SpellEffect.Lever:
                GameDefinitions.Require(Radius == 0 && Duration == 0 && Period == 0, "lever spell " + Id);
                break;
            case SpellEffect.Revive:
                GameDefinitions.Require(Power > 0 && Duration == 0 && Period == 0, "revive spell " + Id);
                break;
        }

        if (Effect is not (SpellEffect.Damage or SpellEffect.Injury or SpellEffect.Slow))
            GameDefinitions.Require(Speed == 0, "non-projectile spell " + Id);
        if (Effect != SpellEffect.Damage)
            GameDefinitions.Require(Radius == 0 || Effect == SpellEffect.Reveal, "spell area " + Id);
    }

    private const long MaximumAuthoredValue = 1000;
}

internal sealed record AdvancementDefinition(
    string Id,
    string Name,
    string Description,
    long Power,
    long Defense,
    long CostDiscount,
    string[] Unlocks)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id)
            && !string.IsNullOrWhiteSpace(Name)
            && !string.IsNullOrWhiteSpace(Description)
            && Power is >= 0 and <= MaximumAuthoredValue
            && Defense is >= 0 and <= MaximumAuthoredValue
            && CostDiscount is >= 0 and <= MaximumAuthoredValue
            && Unlocks is not null
            && Unlocks.All(id => !string.IsNullOrWhiteSpace(id))
            && Unlocks.Distinct(StringComparer.Ordinal).Count() == Unlocks.Length,
            "advancement " + Id);
    }

    private const long MaximumAuthoredValue = 1000;
}

internal sealed record MagicDefinition(
    SpellDefinition[] Spells,
    Dictionary<string, string[]> StartingSpells,
    AdvancementDefinition[] Choices,
    long ExperiencePerEnemy,
    long ExperiencePerPoint,
    double RestSeconds,
    long RestVitality,
    long RestResource,
    float ThreatRange,
    string RestItem,
    string RevivalItem,
    int MaximumRevivals,
    float LightIntensity,
    float LightRange,
    float LightHeight,
    float[] LightColor,
    bool AllowLeverMagic,
    Dictionary<string, int> Resistances,
    Dictionary<string, string> EnemySpells,
    long EnemyResource)
{
    internal void Validate()
    {
        GameDefinitions.Require(Spells is { Length: > 0 }
            && Spells.All(spell => spell is not null)
            && Spells.Select(spell => spell.Id).Distinct(StringComparer.Ordinal).Count() == Spells.Length,
            "spell identities");
        foreach (SpellDefinition spell in Spells) spell.Validate();

        GameDefinitions.Require(StartingSpells is { Count: > 0 }
            && StartingSpells.Keys.All(key => !string.IsNullOrWhiteSpace(key)), "starting spell members");
        Dictionary<string, string[]> startingSpells = StartingSpells!;
        HashSet<string> spellIds = Spells.Select(spell => spell.Id).ToHashSet(StringComparer.Ordinal);
        foreach ((string member, string[] spells) in startingSpells)
        {
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(member)
                && spells is not null
                && spells.Distinct(StringComparer.Ordinal).Count() == spells.Length
                && spells.All(spellIds.Contains), "starting spells " + member);
        }

        GameDefinitions.Require(Choices is { Length: > 0 }
            && Choices.All(choice => choice is not null)
            && Choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() == Choices.Length,
            "advancement choices");
        foreach (AdvancementDefinition choice in Choices)
        {
            choice.Validate();
            GameDefinitions.Require(choice.Unlocks.All(spellIds.Contains), "advancement spell " + choice.Id);
        }

        HashSet<string> availableSpells = startingSpells.Values.SelectMany(spells => spells)
            .Concat(Choices.SelectMany(choice => choice.Unlocks)).ToHashSet(StringComparer.Ordinal);
        GameDefinitions.Require(availableSpells.SetEquals(spellIds), "spell catalogue coverage");

        GameDefinitions.Require(ExperiencePerEnemy is > 0 and <= MaximumAuthoredValue
            && ExperiencePerPoint is > 0 and <= MaximumAuthoredValue
            && double.IsFinite(RestSeconds) && RestSeconds > 0
            && RestVitality is > 0 and <= MaximumAuthoredValue
            && RestResource is > 0 and <= MaximumAuthoredValue
            && float.IsFinite(ThreatRange) && ThreatRange > 0
            && !string.IsNullOrWhiteSpace(RestItem) && !string.IsNullOrWhiteSpace(RevivalItem)
            && MaximumRevivals is > 0 and <= MaximumRevivalsLimit
            && float.IsFinite(LightIntensity) && LightIntensity > 0
            && float.IsFinite(LightRange) && LightRange > 0
            && float.IsFinite(LightHeight) && LightHeight > 0
            && LightColor is { Length: 4 } && LightColor.All(channel => float.IsFinite(channel) && channel is >= 0 and <= 1),
            "magic recovery and lighting");

        GameDefinitions.Require(Resistances is not null
            && Resistances.All(entry => !string.IsNullOrWhiteSpace(entry.Key)
                && entry.Value is >= 0 and <= ResistanceLimit), "magic resistance");
        GameDefinitions.Require(EnemySpells is not null
            && EnemySpells.All(entry => !string.IsNullOrWhiteSpace(entry.Key)
                && !string.IsNullOrWhiteSpace(entry.Value)
                && spellIds.Contains(entry.Value)
                && Spell(entry.Value).Target == SpellTarget.Enemy
                && Spell(entry.Value).Cost <= EnemyResource), "enemy spells");
        GameDefinitions.Require(EnemyResource is > 0 and <= MaximumAuthoredValue, "enemy resource");
    }

    internal SpellDefinition Spell(string id) => Spells.SingleOrDefault(spell => spell.Id == id)
        ?? throw new InvalidOperationException("Unknown spell: " + id);

    internal AdvancementDefinition Choice(string id) => Choices.SingleOrDefault(choice => choice.Id == id)
        ?? throw new InvalidOperationException("Unknown advancement choice: " + id);

    private const long MaximumAuthoredValue = 1000;
    private const int MaximumRevivalsLimit = 100;
    private const int ResistanceLimit = 100;
}
