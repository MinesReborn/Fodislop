# Запуск из корня репозитория: python3 -I visual/kern-ui/tools/emit-skill-icons.py .
# Значки навыков из листа макета (visual/kern-ui/img/legacy/skills.png) по прямоугольникам skill-sprites.css
# → Assets/Resources/UI/HUD/Player/Skills/Icons/<SkillType>.png (путь, который грузит PlayerHUDSkillGrid)
import re, sys, zlib, struct, pathlib, uuid
repo = pathlib.Path(sys.argv[1])
def read_png(p):
    b = p.read_bytes(); assert b[:8] == b'\x89PNG\r\n\x1a\n'
    i, idat, w = 8, b'', 0
    while i < len(b):
        n, = struct.unpack('>I', b[i:i+4]); t = b[i+4:i+8]; d = b[i+8:i+8+n]; i += 12 + n
        if t == b'IHDR': w, h, bd, ct, _, _, il = struct.unpack('>IIBBBBB', d); assert (bd, ct, il) == (8, 6, 0)
        elif t == b'IDAT': idat += d
    raw = zlib.decompress(idat); bpp, stride = 4, w * 4; rows = []; prev = bytearray(stride); o = 0
    for y in range(h):
        f = raw[o]; line = bytearray(raw[o+1:o+1+stride]); o += 1 + stride
        for x in range(stride):
            a = line[x-bpp] if x >= bpp else 0; up = prev[x]; c = prev[x-bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + up) & 255
            elif f == 3: line[x] = (line[x] + (a + up) // 2) & 255
            elif f == 4:
                pa, pb, pc = abs(up - c), abs(a - c), abs(a + up - 2 * c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else up if pb <= pc else c)) & 255
        rows.append(line); prev = line
    return w, h, rows
def write_png(p, w, h, rows):
    raw = b''.join(b'\x00' + bytes(r) for r in rows)
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    p.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))
W, H, rows = read_png(repo / 'visual/kern-ui/img/legacy/skills.png')
css = (repo / 'visual/kern-ui/css/skill-sprites.css').read_text()
enum = (repo / 'Packages/MinesServerNetworking/MinesServer.Data/SkillType.cs').read_text()
code2type = {m.group(1).strip().replace('*', 'x'): m.group(2) for m in re.finditer(r'<summary>([^|]+)\|[^<]+</summary>\s+(\w+)', enum)}
out = repo / 'Assets/Resources/UI/HUD/Player/Skills/Icons'
meta_tpl = (out / 'Health.png.meta').read_text()
for m in re.finditer(r'\.sk-(\w+) \{ background-position: calc\((\d+) / (\d+) \* 100%\) calc\((\d+) / (\d+) \* 100%\); background-size: calc\(746 / (\d+) \* 100%\) calc\(447 / (\d+) \* 100%\)', css):
    code, x, _, y, _, w, h = m.group(1), *map(int, m.groups()[1:])
    t = code2type.get(code)
    if not t: print('нет типа для', code); continue
    crop = [rows[yy][x*4:(x+w)*4] for yy in range(y, y+h)]
    p = out / f'{t}.png'; write_png(p, w, h, crop)
    meta = p.with_name(p.name + '.meta')
    if not meta.exists(): meta.write_text(re.sub(r'guid: [0-9a-f]+', 'guid: ' + uuid.uuid4().hex, meta_tpl, count=1))
    print(code, t, w, h)
