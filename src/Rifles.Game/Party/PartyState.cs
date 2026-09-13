using Rusty.Engine.Mechanics;

namespace Rifles.Game.Party;

internal enum FormationSlot { FrontLeft, FrontRight, RearLeft, RearRight }
internal sealed record MemberDefinition(string Id, string Name, FormationSlot Slot, long MaximumVitality);
internal sealed record MemberSnapshot(string Id, long Vitality);

internal sealed class PartyMemberState
{
    private static readonly TrackId VitalityId = TrackId.Parse("rifles.vitality");
    private readonly ExactTrack vitality;

    internal PartyMemberState(MemberDefinition definition)
    {
        if (definition.MaximumVitality <= 0) throw new ArgumentOutOfRangeException(nameof(definition));
        Definition = definition;
        vitality = new ExactTrack(new ExactTrackDefinition(VitalityId, ExactValue.Zero,
            new ExactTrackMaximum.Fixed(new ExactValue(definition.MaximumVitality))), new ExactValue(definition.MaximumVitality));
    }

    internal MemberDefinition Definition { get; }
    internal long Vitality => vitality.Current.Raw;
    internal bool IsLiving => Vitality > 0;
    internal long ApplyDamage(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        long applied = Math.Min(Vitality, requested);
        vitality.Spend(new ExactValue(applied));
        return applied;
    }
}

internal sealed class PartyState
{
    private readonly MemberDefinition[] roster;
    private PartyMemberState[] members;
    internal PartyState(IReadOnlyList<MemberDefinition> definitions)
    {
        roster = definitions.ToArray();
        members = roster.Select(d => new PartyMemberState(d)).ToArray();
    }
    internal IReadOnlyList<PartyMemberState> Members => Array.AsReadOnly(members);
    internal IReadOnlyList<MemberSnapshot> Capture() => members.Select(m => new MemberSnapshot(m.Definition.Id, m.Vitality)).ToArray();

    internal void Restore(IReadOnlyList<MemberSnapshot> saved)
    {
        if (saved.Count != roster.Length || saved.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != saved.Count)
            throw new InvalidOperationException("Party snapshot roster mismatch.");
        // Validate and construct the entire replacement before committing any member.
        PartyMemberState[] restored = roster.Select(definition =>
        {
            MemberSnapshot value = saved.SingleOrDefault(s => s.Id == definition.Id)
                ?? throw new InvalidOperationException("Party snapshot member missing.");
            if (value.Vitality < 0 || value.Vitality > definition.MaximumVitality)
                throw new InvalidOperationException("Party snapshot vitality out of range.");
            PartyMemberState member = new(definition);
            member.ApplyDamage(definition.MaximumVitality - value.Vitality);
            return member;
        }).ToArray();
        members = restored;
    }
}
