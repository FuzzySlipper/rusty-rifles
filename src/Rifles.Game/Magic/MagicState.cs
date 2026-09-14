using Rusty.Engine.Mechanics;

namespace Rifles.Game.Magic;

internal sealed record MagicBookSnapshot(
    string Member,
    string Selected,
    string[] Known,
    string[] Hotbar,
    long Experience,
    string[] Choices,
    int Revivals);

internal sealed record MagicConditionSnapshot(
    string Target,
    string Spell,
    double Remaining,
    double TickRemaining);

internal sealed record MagicSnapshot(
    MagicBookSnapshot[] Books,
    MagicConditionSnapshot[] Conditions,
    string[] Rewards,
    double RestRemaining,
    string RestOwner);

/// <summary>
/// Product-owned spellbooks, advancement, timed conditions, and recovery state.
/// Engine effects enforce each condition's single refreshable stack; this class
/// owns admitted duration and periodic timing.
/// </summary>
internal sealed class MagicState
{
    private const int HotbarSlots = 3;
    private static readonly StatId SpeedId = StatId.Parse("rifles.speed");
    private static readonly StackingGroupId SlowSpeedGroup = StackingGroupId.Parse("rifles.slow.speed");
    private static readonly ContinuousStatDefinition SpeedDefinition = new(
        SpeedId,
        new ContinuousValue(0),
        new ContinuousValue(1));
    private readonly MagicDefinition definition;
    private readonly Dictionary<string, MagicBook> books;
    private readonly Dictionary<string, EffectDefinition> effectDefinitions;
    private readonly Dictionary<string, TargetConditions> conditions = new(StringComparer.Ordinal);
    private readonly HashSet<string> rewards = new(StringComparer.Ordinal);

    internal MagicState(MagicDefinition definition, IEnumerable<string> members)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentNullException.ThrowIfNull(members);
        definition.Validate();

        string[] roster = ValidateRoster(members);
        books = roster.ToDictionary(
            member => member,
            member => new MagicBook(definition.StartingSpells[member], definition.ExperiencePerPoint),
            StringComparer.Ordinal);
        effectDefinitions = definition.Spells.ToDictionary(
            spell => spell.Id,
            CreateEffectDefinition,
            StringComparer.Ordinal);
    }

    internal double RestRemaining { get; set; }

    internal string RestOwner { get; set; } = "";

    internal MagicBook For(string member) => books.TryGetValue(member, out MagicBook? book)
        ? book
        : throw new InvalidDataException("Unknown spellbook member: " + member);

    internal void Select(string member, string spell)
    {
        MagicBook book = For(member);
        spell ??= "";
        if (spell.Length != 0 && !book.Known.Contains(spell))
            throw new InvalidDataException("That spell is not known by this member.");
        book.Selected = spell;
    }

    internal void Assign(string member, string spell, int slot)
    {
        if (slot < 0 || slot >= HotbarSlots) throw new InvalidDataException("Unknown hotbar slot.");
        MagicBook book = For(member);
        if (string.IsNullOrWhiteSpace(spell) || !book.Known.Contains(spell))
            throw new InvalidDataException("Only a known spell can be assigned.");
        book.Hotbar[slot] = spell;
    }

    internal void AdvanceMember(string member, string choice)
    {
        MagicBook book = For(member);
        if (book.Unspent <= 0) throw new InvalidDataException("That member has no unspent advancement.");
        AdvancementDefinition selected = definition.Choice(choice);
        if (book.Choices.Contains(selected.Id, StringComparer.Ordinal))
            throw new InvalidDataException("That advancement has already been chosen.");

        book.Choices.Add(selected.Id);
        foreach (string spell in selected.Unlocks) book.Known.Add(spell);
    }

    /// <summary>Settles an encounter reward once across the complete roster.</summary>
    internal bool Reward(string encounterKey)
    {
        if (string.IsNullOrWhiteSpace(encounterKey)) throw new ArgumentException("A reward needs an encounter identity.", nameof(encounterKey));
        if (!rewards.Add(encounterKey)) return false;
        foreach (MagicBook book in books.Values)
            book.Experience = checked(book.Experience + definition.ExperiencePerEnemy);
        return true;
    }

    internal void AwardExperience(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        foreach (var book in books.Values) book.Experience = checked(book.Experience + amount);
    }

    internal long Power(string member) => SumChoices(For(member), choice => choice.Power);

    internal long Defense(string member) => SumChoices(For(member), choice => choice.Defense);

    internal long CostDiscount(string member) => SumChoices(For(member), choice => choice.CostDiscount);

    internal void Apply(string target, SpellDefinition spell, double? duration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(spell);
        double appliedDuration = duration ?? spell.Duration;
        if (!double.IsFinite(appliedDuration) || appliedDuration <= 0 || appliedDuration > spell.Duration)
            throw new InvalidDataException("Condition duration must be positive and within its authored duration.");
        if (!effectDefinitions.TryGetValue(spell.Id, out EffectDefinition? effectDefinition))
            throw new InvalidDataException("Unknown condition spell: " + spell.Id);

        TargetConditions targetConditions = GetTarget(target);
        if (targetConditions.BySpell.TryGetValue(spell.Id, out Condition? existing))
        {
            targetConditions.Effects.Refresh(existing.Instance, Provenance(spell), 1);
            existing.Remaining = appliedDuration;
            // Do not reset TickRemaining. Reapplying an injury cannot indefinitely
            // defer its next due tick.
            return;
        }

        EffectInstanceId instance = EffectInstance(spell);
        targetConditions.Effects.Apply(effectDefinition, instance, Provenance(spell), 1);
        targetConditions.BySpell.Add(spell.Id, new Condition(spell, instance, appliedDuration, InitialTick(spell)));
    }

    internal void Clear(string target, bool harmfulOnly = false)
    {
        if (!conditions.TryGetValue(target, out TargetConditions? targetConditions)) return;
        foreach (Condition condition in targetConditions.BySpell.Values.ToArray())
        {
            if (harmfulOnly && !condition.Spell.Harmful) continue;
            targetConditions.Effects.Remove(condition.Instance);
            targetConditions.BySpell.Remove(condition.Spell.Id);
        }
        if (targetConditions.BySpell.Count == 0) conditions.Remove(target);
    }

    internal bool Has(string target, SpellEffect effect) => conditions.TryGetValue(target, out TargetConditions? targetConditions)
        && targetConditions.BySpell.Values.Any(condition => condition.Spell.Effect == effect);

    internal float Radius(string target, SpellEffect effect) => conditions.TryGetValue(target, out TargetConditions? active)
        ? active.BySpell.Values.Where(c => c.Spell.Effect == effect).Select(c => c.Spell.Radius).DefaultIfEmpty(0).Max() : 0;

    internal double Speed(string target)
    {
        if (!conditions.TryGetValue(target, out TargetConditions? targetConditions)) return 1;
        ContinuousSource[] sources = targetConditions.BySpell.Values
            .Where(condition => condition.Spell.Effect == SpellEffect.Slow)
            .Select(condition => new ContinuousSource(
                new EffectSourceIdentity(null, condition.Instance, 1, SourceDefinition(condition.Spell)),
                SourceDefinition(condition.Spell),
                priority: 0,
                [new ContinuousStatContributionDefinition(
                    SpeedId,
                    SlowSpeedGroup,
                    MechanicsStackingPolicy.Lowest,
                    new ContinuousStatContribution.Maximum(new ContinuousValue(condition.Spell.SpeedFactor)))]))
            .ToArray();
        return ContinuousStatEvaluator.Evaluate(SpeedDefinition, new ContinuousValue(1), sources).Value.Value;
    }

    internal long DefenseBonus(string target) => !conditions.TryGetValue(target, out TargetConditions? targetConditions)
        ? 0
        : targetConditions.BySpell.Values.Where(condition => condition.Spell.Effect == SpellEffect.Defense)
            .Aggregate(0L, (sum, condition) => checked(sum + condition.Spell.Power));

    internal string Describe(string target)
    {
        if (!conditions.TryGetValue(target, out TargetConditions? targetConditions) || targetConditions.BySpell.Count == 0)
            return "Healthy";
        return string.Join(", ", targetConditions.BySpell.Values
            .OrderBy(condition => condition.Spell.Name, StringComparer.Ordinal)
            .Select(condition => condition.Spell.Name + " (" + Math.Ceiling(condition.Remaining) + "s)"));
    }

    /// <summary>
    /// Advances only the simulation time admitted by the product. Periodic
    /// callbacks may clear their target, so each post-callback operation first
    /// confirms that its condition remains active.
    /// </summary>
    internal void Advance(double seconds, Action<string, SpellDefinition> periodic)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        ArgumentNullException.ThrowIfNull(periodic);
        if (seconds == 0) return;

        foreach ((string target, TargetConditions targetConditions) in conditions.ToArray())
        {
            foreach (Condition condition in targetConditions.BySpell.Values.ToArray())
                AdvanceCondition(target, targetConditions, condition, seconds, periodic);
        }
    }

    internal MagicSnapshot Capture() => new(
        books.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new MagicBookSnapshot(
            entry.Key,
            entry.Value.Selected,
            entry.Value.Known.OrderBy(spell => spell, StringComparer.Ordinal).ToArray(),
            entry.Value.Hotbar.ToArray(),
            entry.Value.Experience,
            entry.Value.Choices.ToArray(),
            entry.Value.Revivals)).ToArray(),
        conditions.OrderBy(entry => entry.Key, StringComparer.Ordinal).SelectMany(entry => entry.Value.BySpell.Values
            .OrderBy(condition => condition.Spell.Id, StringComparer.Ordinal)
            .Select(condition => new MagicConditionSnapshot(entry.Key, condition.Spell.Id, condition.Remaining, condition.TickRemaining))).ToArray(),
        rewards.OrderBy(reward => reward, StringComparer.Ordinal).ToArray(),
        RestRemaining,
        RestOwner);

    internal static MagicState Restore(
        MagicSnapshot snapshot,
        MagicDefinition definition,
        IEnumerable<string> members,
        IEnumerable<string> validTargets,
        IEnumerable<string> validRewards, long completionExperience = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(validTargets);
        ArgumentNullException.ThrowIfNull(validRewards);

        MagicState state = new(definition, members);
        HashSet<string> targets = RequireDistinct(validTargets, "condition targets");
        HashSet<string> rewardKeys = RequireDistinct(validRewards, "reward identities");
        ValidateSnapshotShape(snapshot, state, targets, rewardKeys, completionExperience);

        foreach (MagicBookSnapshot saved in snapshot.Books)
        {
            MagicBook book = state.For(saved.Member);
            book.Selected = saved.Selected;
            book.Known.Clear();
            foreach (string spell in saved.Known) book.Known.Add(spell);
            Array.Copy(saved.Hotbar, book.Hotbar, HotbarSlots);
            book.Experience = saved.Experience;
            book.Choices.Clear();
            book.Choices.AddRange(saved.Choices);
            book.Revivals = saved.Revivals;
        }
        foreach (string reward in snapshot.Rewards) state.rewards.Add(reward);
        foreach (MagicConditionSnapshot saved in snapshot.Conditions)
        {
            SpellDefinition spell = definition.Spell(saved.Spell);
            state.Apply(saved.Target, spell);
            Condition condition = state.conditions[saved.Target].BySpell[saved.Spell];
            condition.Remaining = saved.Remaining;
            condition.TickRemaining = saved.TickRemaining;
        }
        state.RestRemaining = snapshot.RestRemaining;
        state.RestOwner = snapshot.RestOwner;
        return state;
    }

    private void AdvanceCondition(
        string target,
        TargetConditions targetConditions,
        Condition condition,
        double seconds,
        Action<string, SpellDefinition> periodic)
    {
        if (!targetConditions.BySpell.TryGetValue(condition.Spell.Id, out Condition? active)
            || !ReferenceEquals(active, condition)) return;

        double remainingTime = seconds;
        while (remainingTime > 0 && targetConditions.BySpell.TryGetValue(condition.Spell.Id, out active)
            && ReferenceEquals(active, condition))
        {
            if (condition.Spell.Period <= 0)
            {
                if (remainingTime < condition.Remaining)
                {
                    condition.Remaining -= remainingTime;
                    return;
                }
                Expire(target, targetConditions, condition);
                return;
            }

            double next = Math.Min(condition.TickRemaining, condition.Remaining);
            if (remainingTime < next)
            {
                condition.Remaining -= remainingTime;
                condition.TickRemaining -= remainingTime;
                return;
            }

            condition.Remaining -= next;
            condition.TickRemaining -= next;
            remainingTime -= next;

            // A tick at the exact expiry boundary is a legal final tick. The
            // timer can never tick after its expiry because the expiry branch
            // below removes it before another loop iteration.
            if (condition.TickRemaining <= 0)
            {
                periodic(target, condition.Spell);
                if (!targetConditions.BySpell.TryGetValue(condition.Spell.Id, out active)
                    || !ReferenceEquals(active, condition)) return;
                condition.TickRemaining = condition.Spell.Period;
            }

            if (condition.Remaining <= 0)
            {
                Expire(target, targetConditions, condition);
                return;
            }
        }
    }

    private void Expire(string target, TargetConditions targetConditions, Condition condition)
    {
        if (!targetConditions.BySpell.Remove(condition.Spell.Id)) return;
        targetConditions.Effects.Expire(condition.Instance);
        if (targetConditions.BySpell.Count == 0) conditions.Remove(target);
    }

    private TargetConditions GetTarget(string target)
    {
        if (!conditions.TryGetValue(target, out TargetConditions? result))
        {
            result = new TargetConditions();
            conditions.Add(target, result);
        }
        return result;
    }

    private long SumChoices(MagicBook book, Func<AdvancementDefinition, long> selector) => book.Choices
        .Aggregate(0L, (sum, id) => checked(sum + selector(definition.Choice(id))));

    private static EffectDefinition CreateEffectDefinition(SpellDefinition spell) => new(
        EffectDefinitionId.Parse("rifles.spell." + spell.Id),
        StackingGroupId.Parse("rifles.spell." + spell.Id),
        spell.Stacking,
        maximumInstances: 1,
        maximumStacks: 1,
        sourceDefinitions: [SourceDefinition(spell)]);

    private static EffectInstanceId EffectInstance(SpellDefinition spell) =>
        EffectInstanceId.Parse("rifles.spell." + spell.Id);

    private static MechanicsSourceIdentity Provenance(SpellDefinition spell) =>
        new IntrinsicSourceIdentity(null, SourceInstanceId.Parse("rifles.spell." + spell.Id));

    private static SourceDefinitionId SourceDefinition(SpellDefinition spell) =>
        SourceDefinitionId.Parse("rifles.spell." + spell.Id);

    private static double InitialTick(SpellDefinition spell) => spell.Period > 0 ? spell.Period : 0;

    private static string[] ValidateRoster(IEnumerable<string> members)
    {
        string[] roster = members.ToArray();
        if (roster.Length == 0 || roster.Any(string.IsNullOrWhiteSpace)
            || roster.Distinct(StringComparer.Ordinal).Count() != roster.Length)
            throw new InvalidDataException("Spellbook members must be nonempty and distinct.");
        return roster;
    }

    private static HashSet<string> RequireDistinct(IEnumerable<string> values, string label)
    {
        string[] materialized = values.ToArray();
        if (materialized.Any(string.IsNullOrWhiteSpace)
            || materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new InvalidDataException("Saved " + label + " must be nonempty and distinct.");
        return materialized.ToHashSet(StringComparer.Ordinal);
    }

    private static void ValidateSnapshotShape(
        MagicSnapshot snapshot,
        MagicState state,
        IReadOnlySet<string> validTargets,
        IReadOnlySet<string> validRewards, long completionExperience)
    {
        if (snapshot.Books is null || snapshot.Conditions is null || snapshot.Rewards is null
            || snapshot.RestOwner is null || !double.IsFinite(snapshot.RestRemaining)
            || snapshot.RestRemaining < 0 || snapshot.RestRemaining > state.definition.RestSeconds)
            throw new InvalidDataException("Magic snapshot has invalid required values.");
        if (snapshot.Books.Length != state.books.Count
            || snapshot.Books.Any(book => book is null)
            || snapshot.Books.Select(book => book.Member).Distinct(StringComparer.Ordinal).Count() != snapshot.Books.Length
            || !snapshot.Books.Select(book => book.Member).ToHashSet(StringComparer.Ordinal).SetEquals(state.books.Keys))
            throw new InvalidDataException("Magic snapshot spellbook roster does not match.");
        if (snapshot.Conditions.Any(condition => condition is null)
            || snapshot.Conditions.Select(condition => condition.Target + "\u001f" + condition.Spell).Distinct(StringComparer.Ordinal).Count() != snapshot.Conditions.Length)
            throw new InvalidDataException("Magic snapshot repeats an active condition.");
        if (snapshot.Rewards.Any(string.IsNullOrWhiteSpace)
            || snapshot.Rewards.Distinct(StringComparer.Ordinal).Count() != snapshot.Rewards.Length
            || !snapshot.Rewards.ToHashSet(StringComparer.Ordinal).SetEquals(validRewards))
            throw new InvalidDataException("Magic snapshot rewards do not match resolved encounters.");
        if (snapshot.RestRemaining > 0 && !state.books.ContainsKey(snapshot.RestOwner)
            || snapshot.RestRemaining == 0 && snapshot.RestOwner.Length != 0)
            throw new InvalidDataException("Magic snapshot rest owner is invalid.");

        if (completionExperience < 0) throw new InvalidDataException("Invalid completion experience.");
        long expectedExperience = checked((long)snapshot.Rewards.Length * state.definition.ExperiencePerEnemy + completionExperience);
        foreach (MagicBookSnapshot book in snapshot.Books)
        {
            if (book.Selected is null || book.Known is null || book.Hotbar is null || book.Choices is null
                || book.Hotbar.Length != HotbarSlots || book.Experience < 0 || book.Experience != expectedExperience
                || book.Revivals < 0 || book.Revivals > state.definition.MaximumRevivals
                || book.Known.Any(string.IsNullOrWhiteSpace) || book.Known.Distinct(StringComparer.Ordinal).Count() != book.Known.Length
                || book.Choices.Any(string.IsNullOrWhiteSpace) || book.Choices.Distinct(StringComparer.Ordinal).Count() != book.Choices.Length
                || book.Hotbar.Any(spell => spell is null))
                throw new InvalidDataException("Magic snapshot spellbook values are invalid.");
            if (book.Selected.Length != 0 && !book.Known.Contains(book.Selected, StringComparer.Ordinal)
                || book.Hotbar.Any(spell => spell.Length != 0 && !book.Known.Contains(spell, StringComparer.Ordinal)))
                throw new InvalidDataException("Magic snapshot selects an unknown spell.");

            HashSet<string> choiceIds = state.definition.Choices.Select(choice => choice.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> spellIds = state.definition.Spells.Select(spell => spell.Id).ToHashSet(StringComparer.Ordinal);
            if (book.Choices.Any(choice => !choiceIds.Contains(choice)) || book.Known.Any(spell => !spellIds.Contains(spell)))
                throw new InvalidDataException("Magic snapshot refers to an unknown definition.");
            if (book.Choices.Length > book.Experience / state.definition.ExperiencePerPoint)
                throw new InvalidDataException("Magic snapshot spends unavailable advancement.");

            HashSet<string> expectedKnown = state.definition.StartingSpells[book.Member].ToHashSet(StringComparer.Ordinal);
            foreach (string choice in book.Choices) expectedKnown.UnionWith(state.definition.Choice(choice).Unlocks);
            if (!expectedKnown.SetEquals(book.Known))
                throw new InvalidDataException("Magic snapshot known spells do not match its advancement choices.");
        }

        foreach (MagicConditionSnapshot condition in snapshot.Conditions)
        {
            if (!validTargets.Contains(condition.Target) || string.IsNullOrWhiteSpace(condition.Spell)
                || !double.IsFinite(condition.Remaining) || condition.Remaining <= 0
                || !double.IsFinite(condition.TickRemaining) || condition.TickRemaining < 0)
                throw new InvalidDataException("Magic snapshot condition timer is invalid.");
            SpellDefinition spell = state.definition.Spell(condition.Spell);
            if (spell.Duration <= 0 || condition.Remaining > spell.Duration
                || spell.Period == 0 && condition.TickRemaining != 0
                || spell.Period > 0 && (condition.TickRemaining <= 0 || condition.TickRemaining > spell.Period))
                throw new InvalidDataException("Magic snapshot condition does not match its spell.");
        }
    }

    internal sealed class MagicBook
    {
        internal MagicBook(IEnumerable<string> known, long experiencePerPoint)
        {
            Known = known.ToHashSet(StringComparer.Ordinal);
            ExperiencePerPoint = experiencePerPoint;
        }

        internal string Selected { get; set; } = "";
        internal HashSet<string> Known { get; }
        internal string[] Hotbar { get; } = ["", "", ""];
        internal long Experience { get; set; }
        internal List<string> Choices { get; } = [];
        internal int Revivals { get; set; }
        internal long Unspent => Experience / ExperiencePerPoint - Choices.Count;
        private long ExperiencePerPoint { get; }
    }

    private sealed class TargetConditions
    {
        internal EffectState Effects { get; } = new();
        internal Dictionary<string, Condition> BySpell { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Condition(SpellDefinition spell, EffectInstanceId instance, double remaining, double tickRemaining)
    {
        internal SpellDefinition Spell { get; } = spell;
        internal EffectInstanceId Instance { get; } = instance;
        internal double Remaining { get; set; } = remaining;
        internal double TickRemaining { get; set; } = tickRemaining;
    }
}
