using Rusty.Engine;
namespace Rifles.Game.Dungeon;

// At most one requested action per admitted update; Engine owns held-key facts and clearing.
internal sealed class ExplorationInput
{
    private ExplorationAction? pending;
    internal void Clear() => pending = null;
    internal void Observe(ExplorationAction action) => pending = action;
    internal void Apply(ExplorationState state)
    {
        if (pending is { } action) state.Act(action);
        pending = null;
    }
}
