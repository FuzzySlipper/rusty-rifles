#!/usr/bin/env python3
"""Offline authoring cleanup: discard zero-area triangles, preserve all other GLB data."""
import json
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root / 'docs/art/mesh-originals/musket.glb'
destination = root / 'content/art/mesh/musket.glb'
data = source.read_bytes()
json_length = struct.unpack_from('<I', data, 12)[0]
document = json.loads(data[20:20 + json_length])
binary = bytearray(data[28 + json_length:])


def read_accessor(index, components):
    accessor = document['accessors'][index]
    view = document['bufferViews'][accessor['bufferView']]
    code = {5123: 'H', 5125: 'I', 5126: 'f'}[accessor['componentType']]
    size = struct.calcsize(code) * components
    offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    stride = view.get('byteStride', size)
    return [struct.unpack_from('<' + code * components, binary, offset + i * stride)
            for i in range(accessor['count'])]


removed = 0
for mesh in document['meshes']:
    for primitive in mesh['primitives']:
        assert primitive.get('mode', 4) == 4, 'Expected indexed triangles'
        positions = read_accessor(primitive['attributes']['POSITION'], 3)
        indices = [v[0] for v in read_accessor(primitive['indices'], 1)]
        kept = []
        for start in range(0, len(indices), 3):
            triangle = indices[start:start + 3]
            a, b, c = [positions[i] for i in triangle]
            ab, ac, bc = ([b[i] - a[i] for i in range(3)],
                          [c[i] - a[i] for i in range(3)],
                          [c[i] - b[i] for i in range(3)])
            scale = max(abs(v) for v in ab + ac)
            if scale:
                ab, ac, bc = [[v / scale for v in edge] for edge in (ab, ac, bc)]
                cross = [ab[1]*ac[2]-ab[2]*ac[1], ab[2]*ac[0]-ab[0]*ac[2], ab[0]*ac[1]-ab[1]*ac[0]]
                area = sum(v*v for v in cross)
                edge = max(sum(v*v for v in values) for values in (ab, ac, bc))
            else:
                area, edge = 0, 0
            if area <= 2.220446049250313e-16 * edge * edge:
                removed += 1
            else:
                kept.extend(triangle)
        while len(binary) % 4:
            binary.append(0)
        offset = len(binary)
        payload = struct.pack('<' + 'I' * len(kept), *kept)
        binary.extend(payload)
        view_index = len(document['bufferViews'])
        document['bufferViews'].append(dict(buffer=0, byteOffset=offset, byteLength=len(payload), target=34963))
        accessor = document['accessors'][primitive['indices']]
        accessor.update(bufferView=view_index, byteOffset=0, componentType=5125, count=len(kept), min=[min(kept)], max=[max(kept)])
document['buffers'][0]['byteLength'] = len(binary)
json_data = json.dumps(document, separators=(',', ':')).encode()
json_data += b' ' * (-len(json_data) % 4)
binary.extend(b'\0' * (-len(binary) % 4))
length = 12 + 8 + len(json_data) + 8 + len(binary)
destination.write_bytes(struct.pack('<III', 0x46546c67, 2, length) + struct.pack('<II', len(json_data), 0x4e4f534a) + json_data
                        + struct.pack('<II', len(binary), 0x004e4942) + binary)
print(f'Removed {removed} zero-area triangles; wrote {destination}')
