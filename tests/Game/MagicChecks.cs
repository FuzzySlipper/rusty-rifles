using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Magic;
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

        Console.WriteLine("Magic checks passed: spellbooks, advancement, conditions, saves, and action recovery.");
    }

    private static void VerifySelectionAndKnownHotbar(GameDefinitions definitions)
    {
        MagicState state = NewState(definitions);
        state.Select("warden", "spark");
        Require(state.For("warden").Selected == "spark", "A member can freely select a known spell.");
        state.Select("warden", "");
        Require(state.For("warden").Selected == "", "Cancelling a selection clears the selected spell.");

        state.Assign("warden", "spark", 0);
        state.Assign("warden", "ward", 2);
        Require(state.For("warden").Hotbar.SequenceEqual(["spark", "", "ward"]),
            "Known spells occupy their requested hotbar slots.");
        RequireRejected(() => state.Select("warden", "burst"), "A member cannot select a spell they do not know.");
        RequireRejected(() => state.Assign("warden", "burst", 1), "A member cannot hotbar a spell they do not know.");
    }

    private static void VerifyRewardsAndAdvancement(GameDefinitions definitions)
    {
        MagicState state = NewState(definitions);
        AwardTwoEnemies(state);
        Require(state.For("warden").Experience == 2 * definitions.Magic.ExperiencePerEnemy,
            "Distinct dead enemy identities grant their authored experience once.");
        Require(!state.Reward("enemy:42") && state.For("warden").Experience == 2 * definitions.Magic.ExperiencePerEnemy,
            "Re-awarding a resolved enemy identity does not duplicate experience.");

        AdvancementDefinition power = definitions.Magic.Choices.Single(choice => choice.Power > 0 && choice.CostDiscount > 0);
        state.AdvanceMember("warden", power.Id);
        Require(state.For("warden").Known.IsSupersetOf(power.Unlocks)
            && state.Power("warden") == power.Power
            && state.Defense("warden") == power.Defense
            && state.CostDiscount("warden") == power.CostDiscount,
            "An earned advancement unlocks its actual spells, power, defense, and cost discount.");
        RequireRejected(() => state.AdvanceMember("warden", power.Id), "A chosen advancement cannot be selected twice.");

        MagicState defenseState = NewState(definitions);
        AwardTwoEnemies(defenseState);
        AdvancementDefinition defense = definitions.Magic.Choices.Single(choice => choice.Defense > 0);
        defenseState.AdvanceMember("warden", defense.Id);
        Require(defenseState.Power("warden") == defense.Power
            && defenseState.Defense("warden") == defense.Defense
            && defenseState.CostDiscount("warden") == defense.CostDiscount,
            "A different earned advancement contributes its authored defensive statistics.");
    }

    private static void VerifySnapshotAndRestoreValidation(GameDefinitions definitions)
    {
        MagicState state = NewState(definitions);
        AwardTwoEnemies(state);
        state.Select("warden", "spark");
        state.Assign("warden", "spark", 0);
        state.Assign("warden", "ward", 2);
        SpellDefinition blight = definitions.Magic.Spell("blight");
        state.Apply("enemy:42", blight);
        state.Advance(0.4, (_, _) => throw new InvalidOperationException("Blight must not tick before its period."));
        state.RestRemaining = definitions.Magic.RestSeconds - 0.5;
        state.RestOwner = "warden";
        MagicSnapshot saved = state.Capture();
        MagicConditionSnapshot condition = saved.Conditions.Single();
        Require(Same(condition.Remaining, blight.Duration - 0.4) && Same(condition.TickRemaining, blight.Period - 0.4),
            "Condition snapshots retain exact remaining duration and next periodic tick.");

        MagicState restored = MagicState.Restore(saved, definitions.Magic, Members(definitions), Targets(definitions), ["enemy:42", "enemy:43"]);
        Require(SameSnapshot(restored.Capture(), saved), "Magic restore preserves books, slots, experience, conditions, and recovery state exactly.");
        int ticks = 0;
        restored.Advance(condition.TickRemaining - 0.01, (_, _) => ticks++);
        restored.Advance(0.011, (_, _) => ticks++);
        Require(ticks == 1, "A restored periodic condition resumes on its exact saved tick schedule.");

        MagicSnapshot forgedExperience = saved with
        {
            Books = saved.Books.Select(book => book.Member == "warden" ? book with { Experience = book.Experience + 1 } : book).ToArray(),
        };
        RequireRejected(() => MagicState.Restore(forgedExperience, definitions.Magic, Members(definitions), Targets(definitions), ["enemy:42", "enemy:43"]),
            "Restore rejects experience forged beyond the resolved enemy rewards.");
        MagicSnapshot forgedRewards = saved with { Rewards = ["enemy:42"] };
        RequireRejected(() => MagicState.Restore(forgedRewards, definitions.Magic, Members(definitions), Targets(definitions), ["enemy:42", "enemy:43"]),
            "Restore rejects a reward set that does not match resolved enemies.");
        MagicSnapshot invalidDuration = saved with
        {
            Conditions = saved.Conditions.Select(savedCondition => savedCondition with { Remaining = blight.Duration + 0.01 }).ToArray(),
        };
        RequireRejected(() => MagicState.Restore(invalidDuration, definitions.Magic, Members(definitions), Targets(definitions), ["enemy:42", "enemy:43"]),
            "Restore rejects condition durations beyond their authored limit.");
    }

    private static void VerifyRefreshAndEngineSpeed(GameDefinitions definitions)
    {
        SpellDefinition ward = definitions.Magic.Spell("ward");
        MagicState wardState = NewState(definitions);
        wardState.Apply("warden", ward);
        wardState.Advance(3, (_, _) => { });
        wardState.Apply("warden", ward);
        Require(wardState.Capture().Conditions.Length == 1
            && Same(wardState.Capture().Conditions.Single().Remaining, ward.Duration)
            && wardState.DefenseBonus("warden") == ward.Power,
            "Refreshing a ward restores one authored duration without double-stacking its defense.");
        wardState.Advance(ward.Duration, (_, _) => { });
        Require(wardState.DefenseBonus("warden") == 0, "A refreshed ward expires once at its authored duration.");

        SpellDefinition bind = definitions.Magic.Spell("bind");
        MagicState slowState = NewState(definitions);
        slowState.Apply("enemy:42", bind);
        double evaluatedFactor = slowState.Speed("enemy:42");
        Require(evaluatedFactor < 1 && Same(evaluatedFactor, bind.SpeedFactor),
            "Slow uses the Engine continuous-stat evaluation to reduce movement factor.");
        slowState.Advance(bind.Duration, (_, _) => { });
        Require(Same(slowState.Speed("enemy:42"), 1), "Slow expiry restores the Engine-evaluated speed factor to one.");
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
        MagicState refreshState = NewState(definitions);
        refreshState.Apply("enemy:42", blight);
        refreshState.Advance(0.4, (_, _) => throw new InvalidOperationException("Blight must not tick early."));
        refreshState.Apply("enemy:42", blight);
        MagicConditionSnapshot refreshed = refreshState.Capture().Conditions.Single();
        Require(Same(refreshed.Remaining, blight.Duration) && Same(refreshed.TickRemaining, blight.Period - 0.4),
            "Refreshing damage over time keeps its already-admitted countdown to the next tick.");

        int clearedTicks = 0;
        refreshState.Advance(refreshed.TickRemaining, (target, _) =>
        {
            clearedTicks++;
            refreshState.Clear(target);
        });
        refreshState.Advance(20, (_, _) => clearedTicks++);
        Require(clearedTicks == 1 && !refreshState.Has("enemy:42", SpellEffect.Injury),
            "A periodic callback may clear its condition without the advance loop resurrecting or reticking it.");

        MagicState expiryState = NewState(definitions);
        expiryState.Apply("enemy:42", blight);
        int expiryTicks = 0;
        expiryState.Advance(blight.Duration * 10, (_, _) => expiryTicks++);
        expiryState.Advance(blight.Duration * 10, (_, _) => expiryTicks++);
        Require(expiryTicks == (int)(blight.Duration / blight.Period) && !expiryState.Has("enemy:42", SpellEffect.Injury),
            "A large admitted update resolves ticks only through condition expiry and never after it.");
    }

    private static void VerifyPausedTimeAndCastRecovery(GameDefinitions definitions)
    {
        MagicState paused = NewState(definitions);
        paused.Apply("enemy:42", definitions.Magic.Spell("blight"));
        MagicSnapshot beforePause = paused.Capture();
        int ticks = 0;
        paused.Advance(0, (_, _) => ticks++);
        Require(ticks == 0 && SameSnapshot(paused.Capture(), beforePause), "Paused admitted time leaves magic state unchanged.");

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

    private static MagicState NewState(GameDefinitions definitions) => new(definitions.Magic, Members(definitions));

    private static (string Instance, string Archetype)[] Members(GameDefinitions definitions) => definitions.Characters
        .ResolvePreset(definitions.Characters.DefaultPresetId).Select(member => (member.Id, member.Archetype)).ToArray();

    private static string[] Targets(GameDefinitions definitions) => [.. Members(definitions).Select(member => member.Instance), "enemy:42", "party"];

    private static void AwardTwoEnemies(MagicState state)
    {
        Require(state.Reward("enemy:42") && state.Reward("enemy:43"), "Distinct enemy identities settle their rewards.");
    }

    private static bool SameSnapshot(MagicSnapshot actual, MagicSnapshot expected) => actual.RestRemaining == expected.RestRemaining
        && actual.RestOwner == expected.RestOwner
        && actual.Rewards.SequenceEqual(expected.Rewards)
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
