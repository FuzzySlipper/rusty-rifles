namespace Rifles.Game.Presentation;

/// <summary>
/// The result of one parsed UI command. Expected unavailable gameplay
/// reports a reason instead of throwing; only malformed input and genuine
/// mid-operation violations throw. An accepted outcome with an empty message
/// keeps whatever feedback the operation already published.
/// </summary>
internal sealed record GameOutcome(bool Accepted, string Message)
{
    internal static GameOutcome Accept(string message = "") => new(true, message);
    internal static GameOutcome Reject(string reason) => new(false, reason);
}
