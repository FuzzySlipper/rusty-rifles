using System.Text.Json.Serialization;
using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Procgen;
using Rifles.Procgen.Generation;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Expedition;

/// <summary>
/// Source-generated metadata for the one current saved-run schema.
/// Unmapped members are rejected. Enums serialize as strings via one generic
/// converter per reachable enum (the non-generic converter is AOT-blocked):
/// when a new enum enters the save graph, register it here or it silently
/// falls back to numeric.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    Converters = [
        typeof(JsonStringEnumConverter<ActionPhase>),
        typeof(JsonStringEnumConverter<CombatActionKind>),
        typeof(JsonStringEnumConverter<EncounterTacticalRole>),
        typeof(JsonStringEnumConverter<EnemyBrainMode>),
        typeof(JsonStringEnumConverter<EnemyBrainReason>),
        typeof(JsonStringEnumConverter<ExplorationAction>),
        typeof(JsonStringEnumConverter<ArchitectureDetailKind>),
        typeof(JsonStringEnumConverter<ArchitectureDetailMaterial>),
        typeof(JsonStringEnumConverter<ArchitectureDetailSurface>),
        typeof(JsonStringEnumConverter<FloorConnectorKind>),
        typeof(JsonStringEnumConverter<EdgeKind>),
        typeof(JsonStringEnumConverter<CardinalDirection>),
        typeof(JsonStringEnumConverter<CorridorPurpose>),
        typeof(JsonStringEnumConverter<NodeKind>),
        typeof(JsonStringEnumConverter<TraversalKind>),
        typeof(JsonStringEnumConverter<MidpointRounding>),
        typeof(JsonStringEnumConverter<TrackMaximumChangePolicy>),
    ],
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(RunSnapshot))]
internal partial class RunSaveJsonContext : JsonSerializerContext;
