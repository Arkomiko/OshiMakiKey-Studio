# Собирает пакет темы theme/custom/minecraft.zip: theme.ts, текстуры блоков 16×16 в img, папки animate и scripts.
# Текстуры рисуются здесь же по пикселям (детерминированный шум), так что пакет можно пересобрать без внешних файлов.
# Запуск: python tools/make-minecraft-theme.py
import os, random, struct, zipfile, zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'theme', 'custom', 'minecraft.zip')


def png(pixels, w, h):
    """pixels: список строк из (r, g, b, a)."""
    raw = b''.join(b'\x00' + b''.join(struct.pack('BBBB', *p) for p in row) for row in pixels)
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xFFFFFFFF)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def hexc(h, a=255):
    return (int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16), a)


def noise(seed, palette, weights=None):
    rnd = random.Random(seed)
    return [[hexc(rnd.choices(palette, weights)[0]) for _ in range(16)] for _ in range(16)]


DIRT = ['#866043', '#6C4A31', '#9B7653', '#593D29']
STONE = ['#7F7F7F', '#747474', '#8F8F8F', '#686868']
GRASS = ['#5FA83A', '#4F9A2E', '#72B84A', '#467F28']

dirt = noise(1, DIRT, [4, 3, 2, 1])
stone = noise(2, STONE, [4, 3, 2, 2])

grass_side = noise(3, DIRT, [4, 3, 2, 1])
rnd = random.Random(4)
for x in range(16):
    depth = 3 + rnd.choice([0, 0, 1, 1, 2])
    for y in range(depth):
        grass_side[y][x] = hexc(rnd.choice(GRASS))

grass_top = noise(5, GRASS, [3, 3, 2, 1])

log = [[hexc('#6B5134') for _ in range(16)] for _ in range(16)]
rnd = random.Random(6)
for x in range(16):
    shade = '#5A4329' if x % 4 in (0, 3) else '#7A5E3B'
    for y in range(16):
        log[y][x] = hexc(shade if rnd.random() > 0.15 else '#4A3622')

rnd = random.Random(7)
leaves = [[(0, 0, 0, 0) if rnd.random() < 0.12 else hexc(rnd.choice(['#3E7A2A', '#356B23', '#4A8C33', '#2C5A1C'])) for _ in range(16)] for _ in range(16)]

THEME_TS = '''// Тема в стиле Minecraft: прямые углы, палитра камня и травы, анимированный фон из блоков.
// Сцену «minecraft» рисует программа; текстуры блоков она берёт из папки img этого пакета.
export default {
  id: 'minecraft',
  name: {
    ru: 'В стиле Minecraft',
    be: 'У стылі Minecraft',
    uk: 'У стилі Minecraft',
    kk: 'Minecraft стилінде',
    zh: 'Minecraft 风格',
    ja: 'Minecraft スタイル',
    en: 'Minecraft style',
    de: 'Minecraft-Stil',
  },
  order: 10,
  dark: true,
  corners: 0,
  background: 'minecraft',
  colors: {
    window: '#16203A',
    surface: '#313131',
    border: '#0E0E0E',
    text: '#FFFFFF',
    muted: '#C6C6C6',
    faint: '#8B8B8B',
    hover: '#3F3F3F',
    input: '#1E1E1E',
    inputText: '#FFFFFF',
    success: '#55FF55',
    danger: '#FF5555',
    warning: '#FFAA00',
    successSoft: '#1F3A1F',
    successText: '#7CFF7C',
    warningSoft: '#3D3018',
    warningText: '#FFCC55',
    padTop: '#79553A',
    padBottom: '#4A3220',
    keyTop: '#7A7A7A',
    keyBottom: '#5E5E5E',
    keyHover: '#8E8E8E',
    keyText: '#FFFFFF',
    keyMuted: '#E6E6E6',
    knobTop: '#9A9A9A',
    knobBottom: '#3A3A3A',
    knobCap: '#5E5E5E',
    knobCapHover: '#707070',
    knobCapBottom: '#2E2E2E',
    knobGlyph: '#FFFFFF',
    badge: '#1E1E1E',
    badgeText: '#FFFFFF',
    accent: '#5FA83A',
    accentDark: '#74C24B',
    accentPressed: '#8FD465',
    accentSoft: '#2F4A23',
    accentText: '#9BE070',
    onAccent: '#FFFFFF',
    keySelectedTop: '#5FA83A',
    keySelectedBottom: '#4A8A2C',
    keySelectedText: '#FFFFFF',
    knobGlow: '#8FD465',
  },
};
'''

ANIMATE_README = '''Кадры анимированного фона: PNG/JPG, проигрываются по порядку имён файлов (скорость — animationFps в theme.ts).
Эта тема рисует фон сценой «minecraft», поэтому кадров здесь нет.
'''

SCRIPTS_README = '''Папка для скриптов темы. Программа их хранит, но никогда не выполняет:
тема из интернета не должна запускать код на компьютере.
'''

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('theme.ts', THEME_TS)
    for name, pixels in [('grass_side', grass_side), ('grass_top', grass_top), ('dirt', dirt), ('stone', stone), ('log', log), ('leaves', leaves)]:
        z.writestr('img/' + name + '.png', png(pixels, 16, 16))
    z.writestr('animate/readme.txt', ANIMATE_README)
    z.writestr('scripts/readme.txt', SCRIPTS_README)
print(OUT)
