using System.Text.Json;
namespace Rifles.Game.Presentation;
internal sealed record SessionCommand(string Revision, string Action, string? Member, ulong? Target, ulong? TargetRevision,
    string? Source = null, string? Destination = null, string? Item = null, ulong? Quantity = null,
    string? InventoryRevision = null, string? Slot = null, string? Preset = null, string? OtherMember = null, string? Spell = null, string? Choice = null,
    string? Position = null, int? PartySlot = null)
{
    internal static SessionCommand Parse(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<SessionCommand>(data,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty UI command.");
}
