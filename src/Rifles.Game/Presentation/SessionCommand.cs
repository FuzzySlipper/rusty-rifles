using System.Text.Json;
namespace Rifles.Game.Presentation;
internal sealed record SessionCommand(string Revision, string Action, string? Member, ulong? Target, ulong? TargetRevision)
{
    internal static SessionCommand Parse(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<SessionCommand>(data,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty UI command.");
}
