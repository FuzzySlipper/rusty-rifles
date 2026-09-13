namespace Rifles.Game.Content;

internal sealed record HudTuning(double RefreshSeconds)
{
    internal void Validate() => GameDefinitions.Require(double.IsFinite(RefreshSeconds)
        && RefreshSeconds > 0, nameof(RefreshSeconds));
}
