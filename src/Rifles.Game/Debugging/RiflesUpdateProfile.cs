using System.Diagnostics;
using System.Text.Json;
using Rusty.Engine.Debugging;

namespace Rifles.Game.Debugging;

internal enum UpdatePhase { Input, Simulation, WorldInteractions, CameraAndLight, FeatureFocus, AppearancePublication, UiProjection, TotalUpdate }

/// <summary>Opt-in timings of Rifles callbacks, including synchronous Engine calls.</summary>
public sealed class RiflesUpdateProfile : IDebugCommandModule
{
    // Diagnostic retention bound, independent of authored gameplay timing.
    private const int SampleCapacity = 2048;
    private readonly Dictionary<UpdatePhase, Queue<double>> samples = Enum.GetValues<UpdatePhase>()
        .ToDictionary(phase => phase, _ => new Queue<double>(SampleCapacity));
    private bool enabled;
    internal bool UiProjectionEnabled { get; private set; } = true;

    [DebugCommand("rifles.profile.ui", Description = "Diagnostic isolation: false freezes the game HUD projection; true restores it. Simulation and Engine debug UI continue.")]
    public string SetUiProjection(bool publish)
    {
        UiProjectionEnabled = publish;
        return publish ? "Game HUD projection restored." : "Game HUD projection frozen for profiling; restore with rifles.profile.ui true.";
    }

    [DebugCommand("rifles.profile.start", Description = "Clears and starts bounded C# update phase timings. Does not change game timing.")]
    public string Start()
    {
        foreach (Queue<double> values in samples.Values) values.Clear();
        enabled = true;
        return "Rifles C# profiling started. Use rifles.profile.read or rifles.profile.stop.";
    }

    [DebugCommand("rifles.profile.stop", Description = "Stops C# timing collection and returns the retained phase breakdown.")]
    public string Stop() { enabled = false; return Read(); }

    [DebugCommand("rifles.profile.read", Description = "Reads C# phase milliseconds; TotalUpdate contains all other phases and must not be added to them.")]
    public string Read() => JsonSerializer.Serialize(new
    {
        enabled,
        capacityPerPhase = SampleCapacity,
        uiProjectionEnabled = UiProjectionEnabled,
        scope = "Rifles C# wall time, including synchronous Engine services; excludes post-callback output conversion, transport and browser work.",
        phases = samples.Select(pair => Summarize(pair.Key, pair.Value)).ToArray(),
    }, new JsonSerializerOptions { WriteIndented = true });

    internal long Begin() => enabled ? Stopwatch.GetTimestamp() : 0;

    internal long Record(UpdatePhase phase, long started)
    {
        if (!enabled || started == 0) return 0;
        long ended = Stopwatch.GetTimestamp();
        Queue<double> values = samples[phase];
        if (values.Count == SampleCapacity) values.Dequeue();
        values.Enqueue(Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds);
        return ended;
    }

    private static object Summarize(UpdatePhase phase, Queue<double> values)
    {
        double[] sorted = values.Order().ToArray();
        double? Percentile(double fraction) => sorted.Length == 0 ? null
            : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];
        return new { phase = phase.ToString(), samples = sorted.Length,
            meanMs = sorted.Length == 0 ? (double?)null : sorted.Average(),
            p50Ms = Percentile(.50), p95Ms = Percentile(.95), maxMs = Percentile(1) };
    }
}
