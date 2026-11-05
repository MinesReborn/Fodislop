"""Independent PNG/source-art emission measurements; never imports Unity."""
import json
import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]


def png(path):
    data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    compressed = b''
    offset = 8
    while offset < len(data):
        size = struct.unpack_from('>I', data, offset)[0]
        kind, body = data[offset + 4:offset + 8], data[offset + 8:offset + 8 + size]
        if kind == b'IHDR':
            width, height, depth, color, _, _, interlace = struct.unpack('>IIBBBBB', body)
            assert (depth, color, interlace) == (8, 6, 0)
        if kind == b'IDAT':
            compressed += body
        offset += size + 12
    raw = zlib.decompress(compressed)
    stride = width * 4
    rows = []
    previous = [0] * stride
    for y in range(height):
        filter_type = raw[y * (stride + 1)]
        line = list(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a, b, c = (line[i - 4] if i >= 4 else 0), previous[i], (previous[i - 4] if i >= 4 else 0)
            estimate = a + b - c
            distances = (abs(estimate - a), abs(estimate - b), abs(estimate - c))
            paeth = (a, b, c)[distances.index(min(distances))]
            prediction = (0, a, b, (a + b) // 2, paeth)[filter_type]
            line[i] = (line[i] + prediction) & 255
        rows.append([tuple(line[i:i + 4]) for i in range(0, stride, 4)])
        previous = line
    return width, height, list(reversed(rows))


def linear(value):
    value /= 255
    return value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4


def integrate(rows, rect, area):
    x, y, width, height = rect
    rgb = [0., 0., 0.]
    weighted_x = weighted_y = energy = 0.
    for j in range(height):
        for i in range(width):
            pixel = rows[y + j][x + i]
            sample = [linear(pixel[c]) * pixel[3] / 255 for c in range(3)]
            for channel in range(3):
                rgb[channel] += sample[channel]
            weight = sum(sample)
            energy += weight
            weighted_x += weight * ((i + .5) / width - .5)
            weighted_y += weight * ((j + .5) / height - .5)
    return {'rgb': [v * area / (width * height) for v in rgb],
            'centroid_fraction': [weighted_x / energy, weighted_y / energy] if energy else [0, 0]}


def main():
    catalog = json.loads((ROOT / 'Assets/Resources/VFX/Sprites/Catalog/Catalog.json').read_text())
    report = {}
    for clip in catalog['Clips']:
        if clip['Id'] not in ('Heal', 'Hurt'):
            continue
        frames = []
        for frame in clip['Frames']:
            mesh = catalog['Meshes'][frame['Mesh']]
            w, h, pixels = png(ROOT / 'Assets/Resources' / (mesh['Texture'] + '.bytes'))
            assert len(mesh['Vertices']) == 4 and len(mesh['Triangles']) == 6
            lo = [min(v[c] for v in mesh['Uv']) for c in ('x', 'y')]
            hi = [max(v[c] for v in mesh['Uv']) for c in ('x', 'y')]
            x, y = round(lo[0] * w), round(lo[1] * h)
            rect = (x, y, round(hi[0] * w) - x, round(hi[1] * h) - y)
            size = [max(v[c] for v in mesh['Vertices']) - min(v[c] for v in mesh['Vertices']) for c in ('x', 'y')]
            frames.append(integrate(pixels, rect, size[0] * size[1]))
        report[clip['Id']] = frames
    w, h, pixels = png(ROOT / 'Assets/Textures/VFX/bz/bz.png')
    layouts = json.loads((ROOT / 'Assets/Resources/VFX/Sprites/Configuration/AnimationFrameLayouts.json').read_text())
    layout = next(v for v in layouts['Layouts'] if v['Name'] == 'vfx/bz')
    assert w == 32 and h == 960 and layout['FrameCount'] == 15 and layout['ComputeEmission']
    report['Bz'] = [integrate(pixels, (0, frame * 64, 32, 64), 2) for frame in range(15)]
    for name, frames in report.items():
        assert all(math.isfinite(v) and v >= 0 for f in frames for v in f['rgb'])
        assert any(max(f['rgb']) > 0 for f in frames)
        print(name, len(frames), 'frames; peak RGB integral', [round(max(f['rgb'][c] for f in frames), 7) for c in range(3)])
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
