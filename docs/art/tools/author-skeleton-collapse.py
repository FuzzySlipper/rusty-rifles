#!/usr/bin/env python3
"""Author a root fall over the supplied standing stagger; retain original tracks and bytes."""
import copy
import json
import math
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root / 'docs/art/mesh-originals/skeleton-soldier.glb'
destination = root / 'content/art/mesh/skeleton-soldier.glb'
data = source.read_bytes()
length = struct.unpack_from('<I', data, 12)[0]
document = json.loads(data[20:20 + length])
binary = bytearray(data[28 + length:])
recipe = json.loads((root / 'docs/art/prompts/skeleton-collapse.json').read_text())
armature = next(i for i, n in enumerate(document['nodes']) if n['name'] == recipe['root'])
clip = copy.deepcopy(next(a for a in document['animations'] if a['name'] == recipe['baseClip']))
clip['name'] = recipe['clip']


def append_accessor(values, kind, minimum=None, maximum=None):
    while len(binary) % 4:
        binary.append(0)
    offset = len(binary)
    flat = [v for row in values for v in row]
    payload = struct.pack('<' + 'f' * len(flat), *flat)
    binary.extend(payload)
    view = len(document['bufferViews'])
    document['bufferViews'].append(dict(buffer=0, byteOffset=offset, byteLength=len(payload)))
    accessor = dict(bufferView=view, componentType=5126, count=len(values), type=kind)
    if minimum is not None:
        accessor.update(min=minimum, max=maximum)
    result = len(document['accessors'])
    document['accessors'].append(accessor)
    return result


keys = recipe['keys']
times = append_accessor([[k['seconds']] for k in keys], 'SCALAR', [keys[0]['seconds']], [keys[-1]['seconds']])
rotations = [[math.sin(math.radians(k['pitchDegrees']) / 2), 0, 0,
              math.cos(math.radians(k['pitchDegrees']) / 2)] for k in keys]
for path, values, kind in [('rotation', rotations, 'VEC4'),
                           ('translation', [[0, k['height'], 0] for k in keys], 'VEC3')]:
    output = append_accessor(values, kind)
    sampler = len(clip['samplers'])
    clip['samplers'].append(dict(input=times, output=output, interpolation='LINEAR'))
    clip['channels'].append(dict(sampler=sampler, target=dict(node=armature, path=path)))
document['animations'].append(clip)
document['buffers'][0]['byteLength'] = len(binary)
encoded = json.dumps(document, separators=(',', ':')).encode()
encoded += b' ' * (-len(encoded) % 4)
binary.extend(b'\0' * (-len(binary) % 4))
length = 28 + len(encoded) + len(binary)
destination.write_bytes(struct.pack('<III', 0x46546c67, 2, length)
                        + struct.pack('<II', len(encoded), 0x4e4f534a) + encoded
                        + struct.pack('<II', len(binary), 0x004e4942) + binary)
print(f'Authored {recipe["clip"]}; wrote {destination}')
