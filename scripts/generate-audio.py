#!/usr/bin/env python3
"""Rebuild the small procedural prototype sound set from its editable recipe."""
import json
import math
from pathlib import Path
import random
import struct
import wave

root = Path(__file__).resolve().parent.parent / "content/audio"
recipe = json.loads((root / "recipe.json").read_text())
rate = recipe["sampleRate"]
for name, cue in recipe["cues"].items():
    randomizer = random.Random(recipe["seed"])
    samples = []
    for index in range(round(cue["seconds"] * rate)):
        time = index / rate
        value = 0.0
        for pulse in cue["pulses"]:
            elapsed = time - pulse
            if elapsed < 0:
                continue
            sweep = (cue["endFrequency"] - cue["frequency"]) / cue["seconds"]
            phase = math.tau * (cue["frequency"] * elapsed + sweep * elapsed * elapsed / 2)
            tone = math.sin(phase)
            noise = randomizer.uniform(-1, 1)
            envelope = math.exp(-cue["decay"] * elapsed)
            value += envelope * ((1 - cue["noise"]) * tone + cue["noise"] * noise)
        samples.append(value)
    peak = max(1, max(abs(value) for value in samples))
    with wave.open(str(root / (name + ".wav")), "wb") as output:
        output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
        output.writeframes(b"".join(struct.pack("<h", round(value / peak * 32767)) for value in samples))
