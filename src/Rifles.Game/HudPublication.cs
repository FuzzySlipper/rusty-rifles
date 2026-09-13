namespace Rifles.Game;

// Uses admitted Engine time even while gameplay is paused. Never queues catch-up snapshots.
internal sealed class HudPublication
{
    private double elapsed;
    internal void Advance(double seconds) => elapsed += seconds;
    internal bool Take(double interval, bool immediate)
    {
        if (!immediate && elapsed < interval) return false;
        elapsed = 0;
        return true;
    }
}
