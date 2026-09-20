using System.Text.Json;
namespace Rifles.Game.Presentation;
/// <summary>
/// One parsed UI command. Freshness revisions are gone: commands inspect
/// actual entity/item/target state when acted on, and genuine delayed-impact
/// requirements recheck at impact. TargetRevision survives only for feature
/// commands whose impact rechecks the mechanism revision.
/// </summary>
internal sealed record SessionCommand(string Action, string? Member, ulong? Target, ulong? TargetRevision,
    string? Source = null, string? Destination = null, string? Item = null, ulong? Quantity = null,
    string? Slot = null, string? Preset = null, string? OtherMember = null, string? Spell = null, string? Choice = null,
    string? Position = null, int? PartySlot = null)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static SessionCommand Parse(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<SessionCommand>(data, Options)
        ?? throw new InvalidDataException("Empty UI command.");
}
