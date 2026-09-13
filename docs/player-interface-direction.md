# Player interface and formation direction

This is future-facing design guidance, not an expansion of the foundation
campaign or a specification of final party size, layout, or combat rules.

The current upper-left scrolling panel is useful for exercising systems, but
is not the intended player interface. Move toward distinct player-facing areas
for formation, ready equipment/actions, and an inventory workspace with direct
drag-and-drop interaction. Keep diagnostics and test controls separate from
normal play. Preserve a clear view of the dungeon; do not make every activity
an accordion in one scrolling panel.

The owner's Wizardry 8 and Dungeon Master 2 screenshots explain different
parts of the direction. Wizardry 8 illustrates formation choices with more
positions than occupants. Dungeon Master 2 makes party position and ready
hands prominent. Neither is a layout to reproduce: Rifles does not require a
portrait-heavy frame or giant click-to-move arrows. The references describe
interaction priorities, not a requirement for pixel art.

Formation should eventually communicate where members stand, not merely their
order in a roster. Empty positions can be meaningful choices, allowing a party
to concentrate forward or cover its flanks. A larger party than four remains
possible. Exact position topology, occupancy limits and the effects of flank
coverage are undecided; do not infer new combat bonuses or targeting rules.

When extending the relevant code:

- Keep member identity distinct from formation-position identity. Selection,
  equipment and inventory ownership follow the member when positions change.
- Keep the displayed formation separate from roster ordering. An eventual
  layout should be able to show empty positions and their occupants explicitly.
- Keep legal placements and positional combat effects in typed, authored C#
  game definitions and rules. The UI displays legal targets and submits intents;
  it does not decide placement legality or change authoritative inventory state.
- Keep inventory transfers and formation changes as distinct actions even when
  both use drag-and-drop. Retain click-based alternatives and rejection feedback.
- Avoid extending assumptions that every position is occupied or that the
  number of positions must equal the number of members. Do not introduce a
  generic layout framework or preemptively rewrite working systems for this.

The campaign's four-member 2×2 baseline remains valid. Apply these boundaries
when touching formation and player UI, and revisit concrete layout and rules
with the owner before implementing the broader positional design.
