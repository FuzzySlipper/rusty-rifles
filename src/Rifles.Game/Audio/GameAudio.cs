using System.Numerics;
using Rifles.Game.Content;
using Rusty.Engine;

namespace Rifles.Game.Audio;

internal enum SoundCue { Rifle, Reload, Spell, Impact, Interaction }
internal sealed record SoundDefinition(SoundCue Cue, string Path, float Volume, float Pitch);
internal sealed record AudioDefinition(float Volume, float SpatialBlend, float Attenuation, SoundDefinition[] Sounds)
{
    internal void Validate()
    {
        GameDefinitions.Require(float.IsFinite(Volume) && Volume >= 0 && Volume <= 1
            && float.IsFinite(SpatialBlend) && SpatialBlend >= 0 && SpatialBlend <= 1
            && float.IsFinite(Attenuation) && Attenuation >= 0, "audio mix");
        GameDefinitions.Require(Sounds.Select(s => s.Cue).Order().SequenceEqual(Enum.GetValues<SoundCue>().Order()), "audio cues");
        foreach (SoundDefinition sound in Sounds)
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(sound.Path) && !sound.Path.Contains("..")
                && !Path.IsPathRooted(sound.Path) && sound.Path.EndsWith(".wav", StringComparison.Ordinal)
                && float.IsFinite(sound.Volume) && sound.Volume >= 0 && sound.Volume <= 1
                && float.IsFinite(sound.Pitch) && sound.Pitch > 0, "sound " + sound.Cue);
    }
}

/// <summary>Retained clips and transient committed-action signals. Playback belongs to Engine.</summary>
internal sealed class GameAudio : IDisposable
{
    private readonly IAudioService audio;
    private readonly AudioDefinition definition;
    private readonly ProductContentBundle bundle;
    private readonly Dictionary<SoundCue, (AudioClip Clip, SoundDefinition Definition)> sounds = [];
    private ulong sequence;

    internal GameAudio(ProductContent content, IAudioService audio, AudioDefinition definition)
    {
        this.audio = audio; this.definition = definition;
        bundle = content.OpenBundle("game-audio");
        try
        {
            foreach (SoundDefinition sound in definition.Sounds)
            {
                using ContentReference reference = bundle.OpenReference(sound.Path);
                sounds.Add(sound.Cue, (audio.OpenClipFromContent(new(reference)), sound));
            }
        }
        catch { Dispose(); throw; }
    }

    internal void Play(SoundCue cue, Vector3 position)
    {
        var sound = sounds[cue];
        audio.Emit(new("rifles-sound-" + checked(++sequence), new(sound.Clip, AudioBus.Sfx,
            definition.Volume * sound.Definition.Volume, sound.Definition.Pitch, false,
            definition.SpatialBlend, definition.Attenuation, 0, AudioEmitterKind.World3d, position, 0, Vector3.Zero)));
    }

    public void Dispose()
    {
        foreach (var sound in sounds.Values) sound.Clip.Dispose();
        sounds.Clear(); bundle.Dispose();
    }
}
