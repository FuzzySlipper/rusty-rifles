using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rusty.Engine.Entities;
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
    string[] Rewards);

/// <summary>
/// Product-owned spellbooks, advancement, timed conditions, and recovery state.
/// Effect stacks live on the target entity's own <see cref="EffectsComponent"/>;
/// stat contributions attach per source to the same <see cref="StatsComponent"/>
/// combat and equipment use. This class owns admitted duration, periodic
/// timing, and per-source handle bookkeeping — never an aggregate rebuild.
/// </summary>
internal sealed class MagicState
{
    private const int HotbarSlots = 3;
    private readonly MagicDefinition definition;
    private readonly CharacterEntities entities;
    private readonly Dictionary<string, MagicBook> books;
    private readonly Dictionary<string, EffectDefinition> effectDefinitions;
    private readonly Dictionary<string, TargetConditions> conditions = new(StringComparer.Ordinal);
    private readonly HashSet<string> rewards = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds one spellbook per roster instance whose archetype admits starting
    /// spells. Archetypes without starting spells get no book: progression
    /// never assumes every character is a caster. Travelling books are adopted
    /// as-is — spellbooks travel once with their characters, never rebuilt.
    /// </summary>
    internal MagicState(MagicDefinition definition, IEnumerable<(string Instance, string Archetype)> roster, CharacterEntities entities,
        IReadOnlyDictionary<string, MagicBook>? travelling = null)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
        ArgumentNullException.ThrowIfNull(roster);
        definition.Validate();

        books = new Dictionary<string, MagicBook>(StringComparer.Ordinal);
        foreach ((string instance, string archetype) in ValidateRoster(roster))
        {
            if (travelling is not null && travelling.TryGetValue(instance, out MagicBook? live))
            {
                books.Add(instance, live);
                entities.AttachComponent(instance, () => live);
                continue;
            }
            if (!definition.StartingSpells.TryGetValue(archetype, out string[]? spells)) continue;
            MagicBook book = new(archetype, spells, definition.ExperiencePerPoint);
            books.Add(instance, book);
            entities.AttachComponent(instance, () => book);
        }

        effectDefinitions = definition.Spells.ToDictionary(
            spell => spell.Id,
            CreateEffectDefinition,
            StringComparer.Ordinal);
    }

    internal IReadOnlyDictionary<string, MagicBook> Books => books;

    internal MagicBook For(string member)
    {
        if (books.TryGetValue(member, out MagicBook? book)
            && entities.TryGetEntity(member, out EntityId entity)
            && new Actor(entities.Store, entity).Has<MagicBook>())
            return book;
        throw new InvalidDataException("Unknown spellbook member: " + member);
    }

    internal bool HasBook(string member) => books.ContainsKey(member);

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
        AttachDevelopment(member, book, selected);
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

    internal long CostDiscount(string member) => SumChoices(For(member), choice => choice.CostDiscount);

    internal void Apply(MagicTarget target, SpellDefinition spell, double? duration = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spell);
        double appliedDuration = duration ?? spell.Duration;
        if (!double.IsFinite(appliedDuration) || appliedDuration <= 0 || appliedDuration > spell.Duration)
            throw new InvalidDataException("Condition duration must be positive and within its authored duration.");
        if (!effectDefinitions.TryGetValue(spell.Id, out EffectDefinition? effectDefinition))
            throw new InvalidDataException("Unknown condition spell: " + spell.Id);

        TargetConditions targetConditions = GetTarget(target.Key);
        if (targetConditions.BySpell.TryGetValue(spell.Id, out Condition? existing))
        {
            EffectsFor(target).Refresh(existing.Instance, Provenance(spell), 1);
            existing.Remaining = appliedDuration;
            // Do not reset TickRemaining. Reapplying an injury cannot indefinitely
            // defer its next due tick. Stat handles persist: refresh changes
            // duration, never the contribution.
            return;
        }

        EffectInstanceId instance = EffectInstance(spell);
        MagicAttachments marker = MarkerFor(target);
        // Effect instances and stat modifiers persist on the entity across
        // state generations: a second restore onto the same entities must not
        // duplicate them. Timing always refreshes from the admitted snapshot.
        if (marker.Effects.Add("fx:" + spell.Id))
            EffectsFor(target).Apply(effectDefinition, instance, Provenance(spell), 1);
        Condition condition = new(spell, instance, appliedDuration, InitialTick(spell));
        AttachConditionStats(target, condition, marker);
        targetConditions.BySpell.Add(spell.Id, condition);
    }

    internal void Clear(MagicTarget target, bool harmfulOnly = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!conditions.TryGetValue(target.Key, out TargetConditions? targetConditions)) return;
        EffectsComponent effects = EffectsFor(target);
        MagicAttachments marker = MarkerFor(target);
        foreach (Condition condition in targetConditions.BySpell.Values.ToArray())
        {
            if (harmfulOnly && !condition.Spell.Harmful) continue;
            effects.Remove(condition.Instance);
            marker.Effects.Remove("fx:" + condition.Spell.Id);
            DetachConditionStats(target, condition, marker);
            targetConditions.BySpell.Remove(condition.Spell.Id);
        }
        if (targetConditions.BySpell.Count == 0) conditions.Remove(target.Key);
    }

    internal bool Has(MagicTarget target, SpellEffect effect) => conditions.TryGetValue(target.Key, out TargetConditions? targetConditions)
        && targetConditions.BySpell.Values.Any(condition => condition.Spell.Effect == effect);

    internal float Radius(MagicTarget target, SpellEffect effect) => conditions.TryGetValue(target.Key, out TargetConditions? active)
        ? active.BySpell.Values.Where(c => c.Spell.Effect == effect).Select(c => c.Spell.Radius).DefaultIfEmpty(0).Max() : 0;

    /// <summary>Live speed factor read from the target's own speed stat.</summary>
    internal double Speed(MagicTarget target) => StatsFor(target).GetStat(RiflesStatIds.Speed).Value;

    internal string Describe(MagicTarget target)
    {
        if (!conditions.TryGetValue(target.Key, out TargetConditions? targetConditions) || targetConditions.BySpell.Count == 0)
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

    /// <summary>
    /// Re-registers travelling condition timing on a new floor. Effect
    /// instances and stat contributions ride the entities (with their marker
    /// keys), so this records timing without re-attaching.
    /// </summary>
    internal void RejoinTravelling(MagicConditionSnapshot[] conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        foreach (MagicConditionSnapshot saved in conditions)
        {
            SpellDefinition spell = definition.Spell(saved.Spell);
            Apply(MagicTarget.Parse(saved.Target), spell);
            Condition condition = this.conditions[saved.Target].BySpell[saved.Spell];
            condition.Remaining = saved.Remaining;
            condition.TickRemaining = saved.TickRemaining;
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
        rewards.OrderBy(reward => reward, StringComparer.Ordinal).ToArray());

    internal static MagicState Restore(
        MagicSnapshot snapshot,
        MagicDefinition definition,
        CharacterEntities entities,
        IEnumerable<(string Instance, string Archetype)> members,
        IEnumerable<string> validTargets,
        IReadOnlyDictionary<string, MagicBook>? travellingBooks = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(validTargets);

        MagicState state = new(definition, members, entities, travellingBooks);
        HashSet<string> targets = RequireDistinct(validTargets, "condition targets");
        ValidateSnapshotShape(snapshot, definition, state.books.ToDictionary(book => book.Key, book => book.Value.Archetype, StringComparer.Ordinal), targets);

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
            foreach (string choice in book.Choices)
                state.AttachDevelopment(saved.Member, book, state.definition.Choice(choice));
            book.Revivals = saved.Revivals;
        }
        foreach (string reward in snapshot.Rewards) state.rewards.Add(reward);
        foreach (MagicConditionSnapshot saved in snapshot.Conditions)
        {
            SpellDefinition spell = definition.Spell(saved.Spell);
            state.Apply(MagicTarget.Parse(saved.Target), spell);
            Condition condition = state.conditions[saved.Target].BySpell[saved.Spell];
            condition.Remaining = saved.Remaining;
            condition.TickRemaining = saved.TickRemaining;
        }
        return state;
    }

    private EffectsComponent EffectsFor(MagicTarget target) =>
        entities.AttachComponent(EntityKey(target), () => new EffectsComponent());

    private StatsComponent StatsFor(MagicTarget target) =>
        new Actor(entities.Store, EntityOf(target)).Get<StatsComponent>();

    private EntityId EntityOf(MagicTarget target)
    {
        if (entities.TryGetEntity(EntityKey(target), out EntityId entity)) return entity;
        throw new InvalidDataException($"Unknown condition target '{target.Key}'.");
    }

    private static string EntityKey(MagicTarget target) => target switch
    {
        MemberTarget member => member.Instance,
        EnemyTarget enemy => "enemy:" + enemy.Id,
        PartyTarget => "party",
        _ => throw new InvalidDataException($"Unknown condition target '{target.Key}'."),
    };

    /// <summary>
    /// Attaches one condition's stat contribution to the target's own stats.
    /// Defense conditions add power; slow conditions cap speed. Party targets
    /// carry no stats, so party conditions are effects-only by construction.
    /// </summary>
    private MagicAttachments MarkerFor(MagicTarget target) =>
        entities.AttachComponent(EntityKey(target), () => new MagicAttachments());

    private void AttachConditionStats(MagicTarget target, Condition condition, MagicAttachments marker)
    {
        if (target is PartyTarget) return;
        if (condition.Spell.Effect is not (SpellEffect.Defense or SpellEffect.Slow)) return;
        if (marker.Stats.TryGetValue("st:" + condition.Spell.Id, out StatModifierHandle? existing))
        {
            // A previous generation attached this contribution; adopt its
            // handle so this generation's expiry removes the original.
            if (condition.Spell.Effect == SpellEffect.Defense) condition.DefenseHandle = existing;
            else condition.SpeedHandle = existing;
            return;
        }
        StatsComponent stats = StatsFor(target);
        if (condition.Spell.Effect == SpellEffect.Defense)
            condition.DefenseHandle = stats.GetStat(RiflesStatIds.Defense).AddModifier(condition.Spell.Power, StatModifierKind.Add);
        else
            condition.SpeedHandle = stats.GetStat(RiflesStatIds.Speed).AddModifier(condition.Spell.SpeedFactor, StatModifierKind.Maximum);
        marker.Stats.Add("st:" + condition.Spell.Id, condition.DefenseHandle ?? condition.SpeedHandle);
    }

    private void DetachConditionStats(MagicTarget target, Condition condition, MagicAttachments marker)
    {
        if (target is PartyTarget) return;
        if (condition.Spell.Effect is not (SpellEffect.Defense or SpellEffect.Slow)) return;
        if (!marker.Stats.Remove("st:" + condition.Spell.Id, out StatModifierHandle? handle) || handle is null) return;
        StatsComponent stats = StatsFor(target);
        if (condition.Spell.Effect == SpellEffect.Defense)
            stats.GetStat(RiflesStatIds.Defense).RemoveModifier(handle);
        else
            stats.GetStat(RiflesStatIds.Speed).RemoveModifier(handle);
    }

    private void AttachDevelopment(string member, MagicBook book, AdvancementDefinition choice)
    {
        MemberTarget target = new(member);
        MagicAttachments marker = MarkerFor(target);
        if (marker.Stats.ContainsKey("dev:" + choice.Id)) return;
        StatsComponent stats = StatsFor(target);
        StatModifierHandle? power = choice.Power == 0 ? null
            : stats.GetStat(RiflesStatIds.Power).AddModifier(choice.Power, StatModifierKind.Add);
        StatModifierHandle? defense = choice.Defense == 0 ? null
            : stats.GetStat(RiflesStatIds.Defense).AddModifier(choice.Defense, StatModifierKind.Add);
        marker.Stats.Add("dev:" + choice.Id, power ?? defense);
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
        MagicTarget parsed = MagicTarget.Parse(target);
        EffectsFor(parsed).Remove(condition.Instance);
        MagicAttachments marker = MarkerFor(parsed);
        marker.Effects.Remove("fx:" + condition.Spell.Id);
        DetachConditionStats(parsed, condition, marker);
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

    private static (string Instance, string Archetype)[] ValidateRoster(IEnumerable<(string Instance, string Archetype)> members)
    {
        (string Instance, string Archetype)[] roster = members.ToArray();
        if (roster.Length == 0 || roster.Any(member => string.IsNullOrWhiteSpace(member.Instance))
            || roster.Select(member => member.Instance).Distinct(StringComparer.Ordinal).Count() != roster.Length)
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
        MagicDefinition definition,
        IReadOnlyDictionary<string, string> roster,
        IReadOnlySet<string> validTargets)
    {
        if (snapshot.Books is null || snapshot.Conditions is null || snapshot.Rewards is null)
            throw new InvalidDataException("Magic snapshot has invalid required values.");
        if (snapshot.Books.Any(book => book is null)
            || snapshot.Books.Select(book => book.Member).Distinct(StringComparer.Ordinal).Count() != snapshot.Books.Length
            || !snapshot.Books.Select(book => book.Member).ToHashSet(StringComparer.Ordinal).IsSubsetOf(roster.Keys))
            throw new InvalidDataException("Magic snapshot spellbook roster does not match.");
        if (snapshot.Conditions.Any(condition => condition is null)
            || snapshot.Conditions.Select(condition => condition.Target + "" + condition.Spell).Distinct(StringComparer.Ordinal).Count() != snapshot.Conditions.Length)
            throw new InvalidDataException("Magic snapshot repeats an active condition.");
        // Rewards are live double-award prevention, not a kill ledger: shape
        // only. Experience is trusted as a coherent current value, never
        // re-derived from encounter history.
        if (snapshot.Rewards.Any(string.IsNullOrWhiteSpace)
            || snapshot.Rewards.Distinct(StringComparer.Ordinal).Count() != snapshot.Rewards.Length)
            throw new InvalidDataException("Magic snapshot rewards are invalid.");
        foreach (MagicBookSnapshot book in snapshot.Books)
        {
            if (book.Selected is null || book.Known is null || book.Hotbar is null || book.Choices is null
                || book.Hotbar.Length != HotbarSlots || book.Experience < 0
                || book.Revivals < 0 || book.Revivals > definition.MaximumRevivals
                || book.Known.Any(string.IsNullOrWhiteSpace) || book.Known.Distinct(StringComparer.Ordinal).Count() != book.Known.Length
                || book.Choices.Any(string.IsNullOrWhiteSpace) || book.Choices.Distinct(StringComparer.Ordinal).Count() != book.Choices.Length
                || book.Hotbar.Any(spell => spell is null))
                throw new InvalidDataException("Magic snapshot spellbook values are invalid.");
            if (book.Selected.Length != 0 && !book.Known.Contains(book.Selected, StringComparer.Ordinal)
                || book.Hotbar.Any(spell => spell.Length != 0 && !book.Known.Contains(spell, StringComparer.Ordinal)))
                throw new InvalidDataException("Magic snapshot selects an unknown spell.");
            HashSet<string> choiceIds = definition.Choices.Select(choice => choice.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> spellIds = definition.Spells.Select(spell => spell.Id).ToHashSet(StringComparer.Ordinal);
            if (book.Choices.Any(choice => !choiceIds.Contains(choice)) || book.Known.Any(spell => !spellIds.Contains(spell)))
                throw new InvalidDataException("Magic snapshot refers to an unknown definition.");
            if (book.Choices.Length > book.Experience / definition.ExperiencePerPoint)
                throw new InvalidDataException("Magic snapshot spends unavailable advancement.");

            HashSet<string> expectedKnown = definition.StartingSpells[roster[book.Member]].ToHashSet(StringComparer.Ordinal);
            foreach (string choice in book.Choices) expectedKnown.UnionWith(definition.Choice(choice).Unlocks);
            if (!expectedKnown.SetEquals(book.Known))
                throw new InvalidDataException("Magic snapshot known spells do not match its advancement choices.");
        }

        ValidateConditions(snapshot.Conditions, validTargets, definition);
    }

    /// <summary>
    /// Data-only condition admission: timers fit their spells and targets fit
    /// their kinds. Shared by live reconstruction and retained-floor checks.
    /// </summary>
    internal static void ValidateConditions(MagicConditionSnapshot[] conditions, IReadOnlySet<string> validTargets,
        MagicDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(validTargets);
        ArgumentNullException.ThrowIfNull(definition);
        foreach (MagicConditionSnapshot condition in conditions)
        {
            if (!validTargets.Contains(condition.Target) || string.IsNullOrWhiteSpace(condition.Spell)
                || !double.IsFinite(condition.Remaining) || condition.Remaining <= 0
                || !double.IsFinite(condition.TickRemaining) || condition.TickRemaining < 0)
                throw new InvalidDataException("Magic snapshot condition timer is invalid.");
            SpellDefinition spell = definition.Spell(condition.Spell);
            if (spell.Duration <= 0 || condition.Remaining > spell.Duration
                || spell.Period == 0 && condition.TickRemaining != 0
                || spell.Period > 0 && (condition.TickRemaining <= 0 || condition.TickRemaining > spell.Period))
                throw new InvalidDataException("Magic snapshot condition does not match its spell.");
            GameDefinitions.Require(condition.Target == "party" ? spell.Target == SpellTarget.Party
                : condition.Target.StartsWith("member:", StringComparison.Ordinal) ? spell.Target == SpellTarget.Ally || spell.Harmful
                : spell.Harmful, "saved condition target kind");
        }
    }

    internal sealed class MagicBook
    {
        internal MagicBook(string archetype, IEnumerable<string> known, long experiencePerPoint)
        {
            Archetype = archetype;
            Known = known.ToHashSet(StringComparer.Ordinal);
            ExperiencePerPoint = experiencePerPoint;
        }

        internal string Archetype { get; }
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
        internal Dictionary<string, Condition> BySpell { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Condition(SpellDefinition spell, EffectInstanceId instance, double remaining, double tickRemaining)
    {
        internal SpellDefinition Spell { get; } = spell;
        internal EffectInstanceId Instance { get; } = instance;
        internal double Remaining { get; set; } = remaining;
        internal double TickRemaining { get; set; } = tickRemaining;
        internal StatModifierHandle? DefenseHandle { get; set; }
        internal StatModifierHandle? SpeedHandle { get; set; }
    }
}
