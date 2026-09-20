using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

internal static class MagicChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifySelectionAndKnownHotbar(definitions);
        VerifyRewardsAndAdvancement(definitions);
        VerifySnapshotAndRestoreValidation(definitions);
        VerifyRefreshAndEngineSpeed(definitions);
        VerifySlowMovementUsesUnscaledClock(definitions);
        VerifyPeriodicTimingAndExpiry(definitions);
        VerifyPausedTimeAndCastRecovery(definitions);
        VerifyPerSourceExpiryAndRestoreIdentity(definitions);
        VerifyBooklessMemberWorks(definitions);

        Console.WriteLine("Magic checks passed: spellbooks, advancement, conditions, saves, and action recovery.");
    }

    private static void VerifySelectionAndKnownHotbar(GameDefinitions definitions)
    {
        Fixture fixture = NewFixture(definitions);
        fixture.State.Select("warden", "spark");
        Require(fixture.State.For("warden").Selected == "spark", "A member can freely select a known spell.");
        fixture.State.Select("warden", "");
        Require(fixture.State.For("warden").Selected == "", "Cancelling a selection clears the selected spell.");

        fixture.State.Assign("warden", "spark", 0);
        fixture.State.Assign("warden", "ward", 2);
        Require(fixture.State.For("warden").Hotbar.SequenceEqual(["spark", "", "ward"]),
            "Known spells occupy their requested hotbar slots.");
        RequireRejected(() => fixture.State.Select("warden", "burst"), "A member cannot select a spell they do not know.");
        RequireRejected(() => fixture.State.Assign("warden", "burst", 1), "A member cannot hotbar a spell they do not know.");
    }

    private static void VerifyRewardsAndAdvancement(GameDefinitions definitions)
    {
        Fixture fixture = NewFixture(definitions);
        AwardTwoEnemies(fixture.State);
        Require(fixture.State.For("warden").Experience == 2 * definitions.Magic.ExperiencePerEnemy,
            "Distinct dead enemy identities grant their authored experience once.");
        Require(!fixture.State.Reward("enemy:42") && fixture.State.For("warden").Experience == 2 * definitions.Magic.ExperiencePerEnemy,
            "Re-awarding a resolved enemy identity does not duplicate experience.");

        RiflesCharacter warden = Member(fixture.Party, "warden");
        AdvancementDefinition power = definitions.Magic.Choices.Single(choice => choice.Power > 0 && choice.CostDiscount > 0);
        fixture.State.AdvanceMember("warden", power.Id);
        Require(fixture.State.For("warden").Known.IsSupersetOf(power.Unlocks)
            && warden.Power == warden.Definition.BasePower + power.Power
            && warden.Defense == warden.Definition.BaseDefense + power.Defense
            && fixture.State.CostDiscount("warden") == power.CostDiscount,
            "An earned advancement unlocks its actual spells and contributes power, defense, and cost discount to live stats.");
        RequireRejected(() => fixture.State.AdvanceMember("warden", power.Id), "A chosen advancement cannot be selected twice.");

        Fixture defenseFixture = NewFixture(definitions);
        AwardTwoEnemies(defenseFixture.State);
        RiflesCharacter defenseWarden = Member(defenseFixture.Party, "warden");
        AdvancementDefinition defense = definitions.Magic.Choices.Single(choice => choice.Defense > 0);
        defenseFixture.State.AdvanceMember("warden", defense.Id);
        Require(defenseWarden.Power == defenseWarden.Definition.BasePower + defense.Power
            && defenseWarden.Defense == defenseWarden.Definition.BaseDefense + defense.Defense
            && defenseFixture.State.CostDiscount("warden") == defense.CostDiscount,
            "A different earned advancement contributes its authored defensive statistics to live stats.");
    }

    private static void VerifySnapshotAndRestoreValidation(GameDefinitions definitions)
    {
        Fixture fixture = NewFixture(definitions);
        AwardTwoEnemies(fixture.State);
        fixture.State.Select("warden", "spark");
        fixture.State.Assign("warden", "spark", 0);
        fixture.State.Assign("warden", "ward", 2);
        SpellDefinition blight = definitions.Magic.Spell("blight");
        fixture.State.Apply(new EnemyTarget("42"), blight);
        fixture.State.Advance(0.4, (_, _) => throw new InvalidOperationException("Blight must not tick before its period."));
        fixture.Party.RestRemaining = definitions.Magic.RestSeconds - 0.5;
        fixture.Party.RestOwner = "warden";
        MagicSnapshot saved = fixture.State.Capture();
        MagicConditionSnapshot condition = saved.Conditions.Single();
        Require(Same(condition.Remaining, blight.Duration - 0.4) && Same(condition.TickRemaining, blight.Period - 0.4),
            "Condition snapshots retain exact remaining duration and next periodic tick.");
        Require(fixture.Party.RestRemaining == definitions.Magic.RestSeconds - 0.5 && fixture.Party.RestOwner == "warden",
            "Rest state travels with the party, outside the magic snapshot.");

        MagicState restored = MagicState.Restore(saved, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions));
        Require(SameSnapshot(restored.Capture(), saved), "Magic restore preserves books, slots, experience, and conditions exactly.");
        int ticks = 0;
        restored.Advance(condition.TickRemaining - 0.01, (_, _) => ticks++);
        restored.Advance(0.011, (_, _) => ticks++);
        Require(ticks == 1, "A restored periodic condition resumes on its exact saved tick schedule.");

        // Coherent current values are trusted without replaying encounter
        // history: off-ledger experience and reward subsets restore exactly.
        MagicSnapshot coherentExperience = saved with
        {
            Books = saved.Books.Select(book => book.Member == "warden" ? book with { Experience = book.Experience + 1 } : book).ToArray(),
        };
        MagicState trusted = MagicState.Restore(coherentExperience, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions));
        Require(SameSnapshot(trusted.Capture(), coherentExperience), "Restore trusts coherent experience without a kill replay.");
        MagicSnapshot coherentRewards = saved with { Rewards = ["enemy:42"] };
        MagicState trustedRewards = MagicState.Restore(coherentRewards, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions));
        Require(SameSnapshot(trustedRewards.Capture(), coherentRewards), "Restore trusts a coherent reward subset without a kill replay.");
        MagicSnapshot negativeExperience = saved with
        {
            Books = saved.Books.Select(book => book.Member == "warden" ? book with { Experience = -1 } : book).ToArray(),
        };
        RequireRejected(() => MagicState.Restore(negativeExperience, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions)),
            "Restore rejects negative experience.");
        MagicSnapshot duplicateRewards = saved with { Rewards = ["enemy:42", "enemy:42"] };
        RequireRejected(() => MagicState.Restore(duplicateRewards, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions)),
            "Restore rejects duplicated rewards.");
        MagicSnapshot invalidDuration = saved with
        {
            Conditions = saved.Conditions.Select(savedCondition => savedCondition with { Remaining = blight.Duration + 0.01 }).ToArray(),
        };
        RequireRejected(() => MagicState.Restore(invalidDuration, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions)),
            "Restore rejects condition durations beyond their authored limit.");
    }

    private static void VerifyRefreshAndEngineSpeed(GameDefinitions definitions)
    {
        SpellDefinition ward = definitions.Magic.Spell("ward");
        Fixture fixture = NewFixture(definitions);
        RiflesCharacter warden = Member(fixture.Party, "warden");
        fixture.State.Apply(new MemberTarget("warden"), ward);
        fixture.State.Advance(3, (_, _) => { });
        fixture.State.Apply(new MemberTarget("warden"), ward);
        Require(fixture.State.Capture().Conditions.Length == 1
            && Same(fixture.State.Capture().Conditions.Single().Remaining, ward.Duration)
            && warden.Defense == warden.Definition.BaseDefense + ward.Power,
            "Refreshing a ward restores one authored duration without double-stacking its defense on the shared stat.");
        fixture.State.Advance(ward.Duration, (_, _) => { });
        Require(warden.Defense == warden.Definition.BaseDefense, "A refreshed ward expires once at its authored duration.");

        SpellDefinition bind = definitions.Magic.Spell("bind");
        fixture.State.Apply(new EnemyTarget("42"), bind);
        double evaluatedFactor = fixture.State.Speed(new EnemyTarget("42"));
        Require(evaluatedFactor < 1 && Same(evaluatedFactor, bind.SpeedFactor),
            "Slow uses the Engine continuous-stat evaluation to reduce movement factor.");
        fixture.State.Advance(bind.Duration, (_, _) => { });
        Require(Same(fixture.State.Speed(new EnemyTarget("42")), 1), "Slow expiry restores the Engine-evaluated speed factor to one.");
    }

    private static void VerifySlowMovementUsesUnscaledClock(GameDefinitions definitions)
    {
        GridPoint source = new(20, 20);
        GridPoint destination = source + definitions.Exploration.InitialFacing.Offset();
        MovementGrid grid = new(new HashSet<GridPoint> { source, destination }, (_, _) => true);
        ExplorationState state = new(source, definitions.Exploration);
        state.Bind(grid, 77);
        Require(state.Act(ExplorationAction.Forward), "A bound party state starts its authored forward step.");

        double admittedHalfStep = definitions.Exploration.StepSeconds / 2;
        state.Advance(admittedHalfStep, speed: 0.5);
        Require(Same(state.ElapsedSeconds, admittedHalfStep)
            && Same(state.RecoverySeconds, definitions.Exploration.StepSeconds - admittedHalfStep * 0.5)
            && state.Position == source && state.Moving,
            "Slow movement scales recovery while elapsed simulation and camera time retain admitted seconds.");

        double finishAtSlowSpeed = state.RecoverySeconds / 0.5;
        state.Advance(finishAtSlowSpeed, speed: 0.5);
        Require(Same(state.ElapsedSeconds, admittedHalfStep + finishAtSlowSpeed)
            && Same(state.RecoverySeconds, 0) && !state.Moving && state.Position == destination,
            "A slowed step remains at its source until enough admitted time completes the scaled recovery.");
    }

    private static void VerifyPeriodicTimingAndExpiry(GameDefinitions definitions)
    {
        SpellDefinition blight = definitions.Magic.Spell("blight");
        Fixture fixture = NewFixture(definitions);
        fixture.State.Apply(new EnemyTarget("42"), blight);
        fixture.State.Advance(0.4, (_, _) => throw new InvalidOperationException("Blight must not tick early."));
        fixture.State.Apply(new EnemyTarget("42"), blight);
        MagicConditionSnapshot refreshed = fixture.State.Capture().Conditions.Single();
        Require(Same(refreshed.Remaining, blight.Duration) && Same(refreshed.TickRemaining, blight.Period - 0.4),
            "Refreshing damage over time keeps its already-admitted countdown to the next tick.");

        int clearedTicks = 0;
        fixture.State.Advance(refreshed.TickRemaining, (target, _) =>
        {
            clearedTicks++;
            fixture.State.Clear(MagicTarget.Parse(target));
        });
        fixture.State.Advance(20, (_, _) => clearedTicks++);
        Require(clearedTicks == 1 && !fixture.State.Has(new EnemyTarget("42"), SpellEffect.Injury),
            "A periodic callback may clear its condition without the advance loop resurrecting or reticking it.");

        Fixture expiryFixture = NewFixture(definitions);
        expiryFixture.State.Apply(new EnemyTarget("42"), blight);
        int expiryTicks = 0;
        expiryFixture.State.Advance(blight.Duration * 10, (_, _) => expiryTicks++);
        expiryFixture.State.Advance(blight.Duration * 10, (_, _) => expiryTicks++);
        Require(expiryTicks == (int)(blight.Duration / blight.Period) && !expiryFixture.State.Has(new EnemyTarget("42"), SpellEffect.Injury),
            "A large admitted update resolves ticks only through condition expiry and never after it.");
    }

    private static void VerifyPausedTimeAndCastRecovery(GameDefinitions definitions)
    {
        Fixture fixture = NewFixture(definitions);
        fixture.State.Apply(new EnemyTarget("42"), definitions.Magic.Spell("blight"));
        MagicSnapshot beforePause = fixture.State.Capture();
        int ticks = 0;
        fixture.State.Advance(0, (_, _) => ticks++);
        Require(ticks == 0 && SameSnapshot(fixture.State.Capture(), beforePause), "Paused admitted time leaves magic state unchanged.");

        SpellDefinition spark = definitions.Magic.Spell("spark");
        ActionSnapshot cast = new(CombatActionKind.Cast, 0, null, null, 42, null, spark.Windup, ActionPhase.Windup,
            spark.Recovery, new GridPoint(2, 3), Spell: spark.Id, Cost: spark.Cost);
        ActionState action = new();
        action.Start(cast);
        int commits = 0;
        action.Advance(spark.Windup, _ => commits++);
        ActionSnapshot recovery = action.Capture() ?? throw new InvalidOperationException("Cast recovery snapshot was missing.");
        ActionState restored = ActionState.Restore(recovery);
        restored.Advance(spark.Recovery, _ => commits++);
        Require(recovery.Phase == ActionPhase.Recovery && !restored.Busy && commits == 1,
            "A saved spell action in recovery cannot replay its committed cast after restore.");
    }

    private static void VerifyPerSourceExpiryAndRestoreIdentity(GameDefinitions definitions)
    {
        // A condition changes the same stat combat reads and removes only its
        // own contribution on expiry, leaving advancement sources intact.
        Fixture fixture = NewFixture(definitions);
        AwardTwoEnemies(fixture.State);
        AdvancementDefinition defense = definitions.Magic.Choices.Single(choice => choice.Defense > 0);
        fixture.State.AdvanceMember("warden", defense.Id);
        RiflesCharacter warden = Member(fixture.Party, "warden");
        SpellDefinition ward = definitions.Magic.Spell("ward");
        fixture.State.Apply(new MemberTarget("warden"), ward);
        Require(warden.Defense == warden.Definition.BaseDefense + defense.Defense + ward.Power,
            "Condition and advancement contributions coexist on the shared defense stat.");
        fixture.State.Advance(ward.Duration, (_, _) => { });
        Require(warden.Defense == warden.Definition.BaseDefense + defense.Defense,
            "Expiring a condition removes only its own contribution, preserving advancement.");

        // Progress survives save and travel without duplicate bonuses: a
        // second restore onto the same entities attaches nothing twice, and
        // the restored generation expiring the condition removes the original.
        fixture.State.Apply(new MemberTarget("warden"), ward);
        Require(warden.Defense == warden.Definition.BaseDefense + defense.Defense + ward.Power,
            "Reapplying after expiry attaches cleanly.");
        MagicSnapshot saved = fixture.State.Capture();
        MagicState restored = MagicState.Restore(saved, definitions.Magic, fixture.Party.Entities, Members(definitions), Targets(definitions));
        Require(warden.Defense == warden.Definition.BaseDefense + defense.Defense + ward.Power,
            "Restoring an active condition onto live entities does not duplicate its contribution.");
        restored.Advance(ward.Duration, (_, _) => { });
        Require(warden.Defense == warden.Definition.BaseDefense + defense.Defense,
            "The restored generation expiring a condition removes the original contribution.");
        Require(restored.Capture().Conditions.Length == 0,
            "The expired condition leaves no snapshot behind.");
    }

    private static void VerifyBooklessMemberWorks(GameDefinitions definitions)
    {
        // A roster instance whose archetype admits no starting spells gets no
        // book, yet its character still holds conditions and saves without
        // source branches. Content admission still requires preset archetypes
        // to resolve; this is roster-level tolerance for future noncasters.
        (string Instance, string Archetype)[] roster = [.. Members(definitions), ("squire", "hedge-nobody")];
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, definitions.Characters.DefaultPresetId);
        party.Entities.AttachStats("squire", "rifles:member:test", () => RiflesStats.ForVitality(10, 10, 0, 0));
        MagicState state = new(definitions.Magic, roster, party.Entities);
        Require(state.Capture().Books.Length == Members(definitions).Length && !state.HasBook("squire"),
            "A noncaster roster instance carries no spellbook.");
        RequireRejected(() => state.For("squire"), "Casting without a spellbook is rejected, not assumed.");
        SpellDefinition bind = definitions.Magic.Spell("bind");
        state.Apply(new MemberTarget("squire"), bind);
        Require(Same(state.Speed(new MemberTarget("squire")), bind.SpeedFactor),
            "A bookless character still receives condition contributions on shared stats.");
        MagicSnapshot saved = state.Capture();
        Require(saved.Books.All(book => book.Member != "squire"), "Snapshots carry books only for casters.");
        MagicState restored = MagicState.Restore(saved, definitions.Magic, party.Entities, roster, [.. Targets(definitions), "member:squire"]);
        Require(SameSnapshot(restored.Capture(), saved), "Bookless rosters restore exactly.");
    }

    private sealed record Fixture(PartyState Party, MagicState State);

    private static Fixture NewFixture(GameDefinitions definitions)
    {
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, definitions.Characters.DefaultPresetId);
        EnsureEnemy(party, 42);
        EnsureEnemy(party, 43);
        MagicState state = new(definitions.Magic, Members(definitions), party.Entities);
        return new(party, state);
    }

    private static void EnsureEnemy(PartyState party, ulong id)
    {
        party.Entities.AttachStats("enemy:" + id, "rifles:enemy:test",
            () => RiflesStats.ForVitality(10, 10, 0, 0));
    }

    private static RiflesCharacter Member(PartyState party, string id) =>
        party.Members.Single(member => member.Definition.Id == id);

    private static (string Instance, string Archetype)[] Members(GameDefinitions definitions) => definitions.Characters
        .ResolvePreset(definitions.Characters.DefaultPresetId).Select(member => (member.Id, member.Archetype)).ToArray();

    private static string[] Targets(GameDefinitions definitions) => [.. Members(definitions).Select(member => "member:" + member.Instance), "enemy:42", "enemy:43", "party"];

    private static void AwardTwoEnemies(MagicState state)
    {
        Require(state.Reward("enemy:42") && state.Reward("enemy:43"), "Distinct enemy identities settle their rewards.");
    }

    private static bool SameSnapshot(MagicSnapshot actual, MagicSnapshot expected) => actual.Rewards.SequenceEqual(expected.Rewards)
        && actual.Conditions.SequenceEqual(expected.Conditions)
        && actual.Books.Length == expected.Books.Length
        && actual.Books.Zip(expected.Books).All(pair => pair.First.Member == pair.Second.Member
            && pair.First.Selected == pair.Second.Selected
            && pair.First.Known.SequenceEqual(pair.Second.Known)
            && pair.First.Hotbar.SequenceEqual(pair.Second.Hotbar)
            && pair.First.Experience == pair.Second.Experience
            && pair.First.Choices.SequenceEqual(pair.Second.Choices)
            && pair.First.Revivals == pair.Second.Revivals);

    private static bool Same(double actual, double expected) => Math.Abs(actual - expected) < 0.000001;

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
