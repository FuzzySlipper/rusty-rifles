using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Expedition;

internal sealed record DifficultyProfile(string Id, string Name, string Description,
    double IncomingDamageMultiplier);
internal sealed record RunDefinition(string Goal, string FinaleLabel, string SuccessText, string DefeatText,
    long FinaleExperience, string DefaultDifficulty, DifficultyProfile[] Difficulties,
    int MapRevealCells, ulong SeedIncrement, bool StartPaused)
{
    internal DifficultyProfile Difficulty(string id) => Difficulties.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidDataException("Unknown difficulty profile.");
    internal void Validate()
    {
        GameDefinitions.Require(new[] { Goal, FinaleLabel, SuccessText, DefeatText }.All(s => !string.IsNullOrWhiteSpace(s))
            && FinaleExperience >= 0 && MapRevealCells > 0 && MapRevealCells <= 8 && SeedIncrement > 0, "expedition rules");
        GameDefinitions.Require(Difficulties.Length > 0 && Difficulties.Select(d => d.Id).Distinct().Count() == Difficulties.Length
            && Difficulties.All(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name)
                && double.IsFinite(d.IncomingDamageMultiplier) && d.IncomingDamageMultiplier > 0), "difficulty profiles");
        _ = Difficulty(DefaultDifficulty);
    }
}
internal sealed record MapMemory(string FloorKey, GridPoint[] Cells);
internal sealed record RunProgress(string Difficulty, bool Completed, MapMemory[] Maps);
