# Game content

Authored game assets belong here. The initial dungeon is generated in C#;
it requires no external art or sibling repository at runtime.

`art/prompts/` contains editable generation recipes. These are authoring inputs,
not a runtime configuration format. Supplied style references live in
`docs/art/references/` until selected assets are prepared for the game.

Runtime tuning lives in `tuning/`; starter party and exploration-feature definitions
live in `definitions/`. The Engine admits these files and C# validates them before
creating a world. See [milestone tuning](../docs/milestone-1.md#tuning) for each file.
Changing a watched file reloads the dev product; Save first if the current run matters.
