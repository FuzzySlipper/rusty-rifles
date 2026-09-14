#!/usr/bin/env python3
"""Render a rifles.floor.read JSON artifact; this does not generate or certify a floor."""
import argparse
import html
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('input', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
data = json.loads(args.input.read_text())
if isinstance(data, str):
    data = json.loads(data)
# The debug readout uses CLR member names; archived JSON may use camel case.
def field(obj, name):
    return obj.get(name, obj.get(name[0].lower() + name[1:]))
def point(obj):
    return field(obj, 'X'), field(obj, 'Y')
cells = [point(c) for c in field(data, 'Cells')]
rooms = field(data, 'Rooms')
left, top = min(x for x, y in cells), min(y for x, y in cells)
right, bottom = max(x for x, y in cells), max(y for x, y in cells)
size = 16
margin = 36
width = max(760, (right-left+1)*size + margin*2)
map_height = (bottom-top+1)*size
height = map_height + 180 + len(rooms)*23
colors = ['#aa9272', '#769e99', '#b58268', '#8d8eaf', '#96a76f', '#b6a36c', '#a17883', '#6d9baa']
room_cells = {point(c): colors[i % len(colors)] for i, room in enumerate(rooms) for c in field(room, 'Cells')}
def xy(p):
    return margin+(p[0]-left)*size, 90+(p[1]-top)*size
out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
       f'<rect width="{width}" height="{height}" fill="#20282c"/>',
       '<g font-family="sans-serif" fill="#eee8d7">',
       f'<text x="24" y="29" font-size="21">{html.escape(str(field(data,"IntentFloorId")))} · seed {field(data,"Seed")}</text>',
       '<text x="24" y="53" font-size="13">Resolved grid plan · rooms, passages, protected thresholds · not a game capture</text>']
for p in cells:
    x, y = xy(p)
    out.append(f'<rect x="{x}" y="{y}" width="{size}" height="{size}" fill="{room_cells.get(p,"#525e63")}" stroke="#20282c" stroke-width="1"/>')
for elevation in field(data, 'Elevations') or []:
    x, y = xy(point(field(elevation, 'Cell')))
    level = field(elevation, 'Level')
    out.append(f'<text x="{x+3}" y="{y+12}" fill="#172127" font-size="10" font-weight="bold">{level:+}</text>')
for connector in field(data, 'Connectors') or []:
    a, b = point(field(connector, 'From')), point(field(connector, 'To'))
    if a > b:
        continue
    x1,y1=xy(a); x2,y2=xy(b)
    out.append(f'<path d="M{x1+8},{y1+8} L{x2+8},{y2+8}" stroke="#cbe9ec" stroke-width="3"><title>{html.escape(str(field(connector,"Id")))}</title></path>')
for route in field(data, 'Routes') or []:
    traversal = field(route, 'Traversal')
    if traversal in (0, 'Open'):
        continue
    path = field(route, 'Cells')
    x, y = xy(point(path[len(path)//2]))
    out.append(f'<rect x="{x+2}" y="{y+2}" width="{size-4}" height="{size-4}" fill="#e9ae4e"><title>{html.escape(str(field(route,"Id")))}</title></rect>')
for label, name in [('E', 'Entrance'), ('X', 'Exit')]:
    x,y=xy(point(field(data,name)))
    out.append(f'<text x="{x+3}" y="{y+13}" fill="#fff" font-weight="bold" font-size="14">{label}</text>')
out.append(f'<text x="24" y="{map_height+122}" font-size="12">E entrance · X objective · gold barrier · ±1 height · pale link stair/bridge/pit edge</text>')
for i, room in enumerate(rooms):
    y=map_height+153+i*23
    out.append(f'<rect x="24" y="{y-12}" width="13" height="13" fill="{colors[i%len(colors)]}"/>')
    out.append(f'<text x="46" y="{y}" font-size="13">{html.escape(field(room,"Title"))} · {html.escape(field(room,"Landmark"))}</text>')
out.append('</g></svg>')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text('\n'.join(out)+'\n')
