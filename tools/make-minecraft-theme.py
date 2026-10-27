# Собирает пакет темы themes/custom/minecraft.zip: theme.ts, текстуры блоков 16×16 в img, папки animate и scripts.
# Текстуры рисуются здесь же по пикселям (детерминированный шум), так что пакет можно пересобрать без внешних файлов.
# Запуск: python tools/make-minecraft-theme.py
import os, random, struct, zipfile, zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'themes', 'custom', 'minecraft.zip')


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

SCRIPTS_README = '''Скрипты темы (*.js) выполняются во встроенной песочнице: без доступа к файлам, сети, процессам и .NET.
Скрипту видны только объект theme (onFrame, addWindow, hour, minute, log) и холст g для рисования.
Зависший или упавший скрипт отключается, программа продолжает работать.
'''

MAIN_JS = r'''// Скрипт темы «В стиле Minecraft»: светлячки ночью, птицы днём и окно «Верстак» в меню «Справка».
// Работает в песочнице программы: доступны только theme и холст g.
function hex2(n) {
  var s = Math.max(0, Math.min(255, Math.floor(n))).toString(16).toUpperCase();
  return s.length < 2 ? '0' + s : s;
}

var flies = [];
for (var i = 0; i < 24; i++) {
  flies.push({ x: Math.random(), y: 0.45 + Math.random() * 0.45, p: Math.random() * 6.28, s: 0.3 + Math.random() * 0.7 });
}
var birds = [];
for (var j = 0; j < 4; j++) {
  birds.push({ y: 0.08 + j * 0.05 + Math.random() * 0.05, p: Math.random(), s: 0.015 + Math.random() * 0.012 });
}

theme.onFrame(function (g, t, w, h) {
  var hour = theme.hour();
  if (hour >= 20 || hour < 6) {
    for (var i = 0; i < flies.length; i++) {
      var f = flies[i];
      var x = f.x * w + Math.sin(t * f.s + f.p) * 30;
      var y = f.y * h + Math.cos(t * f.s * 1.3 + f.p) * 20;
      var a = 128 + 127 * Math.sin(t * 2 + f.p);
      g.rect(x - 4, y - 4, 9, 9, '#' + hex2(a / 5) + 'D8FF6A');
      g.rect(x - 1, y - 1, 3, 3, '#' + hex2(a) + 'E8FF8A');
    }
  } else if (hour >= 8 && hour < 17) {
    for (var k = 0; k < birds.length; k++) {
      var b = birds[k];
      var bx = ((b.p + t * b.s) % 1.2 - 0.1) * w;
      var by = b.y * h + Math.sin(t * 2 + b.p * 6) * 4;
      var dy = Math.sin(t * 8 + b.p * 6) * 4;
      g.line(bx - 8, by - dy, bx, by, '#1E2A36', 2);
      g.line(bx, by, bx + 8, by - dy, '#1E2A36', 2);
    }
  }
});

var grid = ['dirt', 'grass_side', 'stone', 'log', 'leaves', 'stone', 'dirt', 'grass_side', 'log'];
var blocks = ['dirt', 'grass_side', 'grass_top', 'stone', 'log', 'leaves'];
var cell = 64, gap = 4, top = 50, lastW = 360;

function origin(w) { return (w - cell * 3 - gap * 2) / 2; }

theme.addWindow({
  title: 'Minecraft — верстак',
  menu: 'Верстак темы Minecraft…',
  width: 360,
  height: 320,
  onFrame: function (g, t, w, h) {
    lastW = w;
    g.rect(0, 0, w, h, '#313131');
    g.text('Верстак: щёлкните по клетке, чтобы сменить блок', 16, 14, '#FFFFFF', 14);
    var ox = origin(w);
    var lit = Math.floor(t * 2) % 9;
    for (var i = 0; i < 9; i++) {
      var x = ox + (i % 3) * (cell + gap), y = top + Math.floor(i / 3) * (cell + gap);
      g.rect(x - 2, y - 2, cell + 4, cell + 4, i === lit ? '#FFFFFF' : '#8B8B8B');
      g.rect(x, y, cell, cell, '#555555');
      g.image(grid[i], x + 6, y + 6, cell - 12, cell - 12);
    }
    g.text('scripts/main.js · песочница без доступа к файлам и сети', 16, h - 30, '#C6C6C6', 12);
  },
  onClick: function (x, y) {
    var col = Math.floor((x - origin(lastW)) / (cell + gap)), row = Math.floor((y - top) / (cell + gap));
    if (col < 0 || col > 2 || row < 0 || row > 2) return;
    var i = row * 3 + col;
    grid[i] = blocks[(blocks.indexOf(grid[i]) + 1) % blocks.length];
  },
});
'''

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('theme.ts', THEME_TS)
    for name, pixels in [('grass_side', grass_side), ('grass_top', grass_top), ('dirt', dirt), ('stone', stone), ('log', log), ('leaves', leaves)]:
        z.writestr('img/' + name + '.png', png(pixels, 16, 16))
    z.writestr('animate/readme.txt', ANIMATE_README)
    z.writestr('scripts/readme.txt', SCRIPTS_README)
    z.writestr('scripts/main.js', MAIN_JS)
print(OUT)
