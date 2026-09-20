using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Party;
using System.Collections.Generic;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Tests;

/// <summary>
/// Test-only stat snapshot builders. Production code captures through
/// RiflesStats.SnapshotForPersistence; these helpers build or wound snapshots
/// for fixtures and rejection batteries.
/// </summary>
internal static class StatSnapshotHelpers
{
    internal static bool MembersEqual(IReadOnlyList<MemberSnapshot> first, IReadOnlyList<MemberSnapshot> second) =>
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Position == pair.Second.Position
            && RiflesStats.StatsEqual(pair.First.Stats, pair.Second.Stats));

    internal static StatsComponentSnapshot FullMember(MemberDefinition definition) =>
        RiflesStats.SnapshotForPersistence(RiflesStats.ForMember(definition));

    internal static StatsComponentSnapshot FullEnemy(EnemyDefinition definition, long maximumResource) =>
        RiflesStats.SnapshotForPersistence(RiflesStats.ForVitality(
            definition.Vitality, definition.Vitality, maximumResource, maximumResource));

    internal static StatsComponentSnapshot WithTrack(StatsComponentSnapshot saved, TrackId track, double current) =>
        saved with { Tracks = [.. saved.Tracks.Select(t => t.Id == track.Value ? t with { Current = current } : t)] };

    internal static StatsComponentSnapshot WithBase(StatsComponentSnapshot saved, StatId stat, double bases) =>
        saved with { Stats = [.. saved.Stats.Select(s => s.Id == stat.Value ? s with { BaseValue = bases } : s)] };
}
