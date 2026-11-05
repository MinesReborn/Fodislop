# Сборка Windows .ico из готовых PNG (слои 16…256) без сторонних библиотек: ICO с PNG внутри поддерживается с Windows Vista.
# python3 -I pack-ico.py <выход.ico> <png> [<png> …]
import struct
import sys


def png_size(data):
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise SystemExit('не PNG')
    return struct.unpack('>II', data[16:24])


def main():
    out, files = sys.argv[1], sys.argv[2:]
    images = [open(f, 'rb').read() for f in files]
    head = struct.pack('<HHH', 0, 1, len(images))
    entries, offset = b'', 6 + 16 * len(images)
    for data in images:
        w, h = png_size(data)
        if w > 256 or h > 256:
            raise SystemExit(f'слой больше 256: {w}×{h}')
        # 0 в поле размера означает 256
        entries += struct.pack('<BBBBHHII', w % 256, h % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(out, 'wb') as f:
        f.write(head + entries + b''.join(images))
    print(out, len(images), 'слоёв')


main()
