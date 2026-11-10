# Убирает альфа-канал из PNG (RGBA → RGB) без сторонних библиотек: App Store отклоняет иконку с прозрачностью.
# Пиксели уже непрозрачные (кадр рисуется на базальте) — канал просто отбрасывается.
# python3 -I strip-alpha.py <файл.png>
import struct
import sys
import zlib


def chunks(data):
    i = 8
    while i < len(data):
        n, = struct.unpack('>I', data[i:i + 4])
        yield data[i + 4:i + 8], data[i + 8:i + 8 + n]
        i += 12 + n


def chunk(kind, body):
    return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)


def paeth(a, b, c):
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else b if pb <= pc else c


def main():
    path = sys.argv[1]
    data = open(path, 'rb').read()
    parts = list(chunks(data))
    ihdr = parts[0][1]
    w, h, depth, ctype = struct.unpack('>IIBB', ihdr[:10])
    if ctype == 2:
        print(path, 'уже без альфы')
        return
    if ctype != 6 or depth != 8:
        raise SystemExit(f'неожиданный формат: тип {ctype}, глубина {depth}')
    raw = zlib.decompress(b''.join(b for k, b in parts if k == b'IDAT'))
    stride, out, prev = w * 4, bytearray(), bytearray(w * 4)
    for y in range(h):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for x in range(stride):
            a = line[x - 4] if x >= 4 else 0
            b = prev[x]
            c = prev[x - 4] if x >= 4 else 0
            line[x] = (line[x] + (0, a, b, (a + b) // 2, paeth(a, b, c))[f]) & 255
        out.append(0)
        for x in range(w):
            out += line[x * 4:x * 4 + 3]
        prev = line
    body = struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', body) + chunk(b'IDAT', zlib.compress(bytes(out), 9)) + chunk(b'IEND', b'')
    open(path, 'wb').write(png)
    print(path, 'альфа убрана')


main()
