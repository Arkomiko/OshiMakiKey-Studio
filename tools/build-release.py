# Сборка релиза в папку release/:
#   OshiMakiKey-Studio-<версия>-setup-x64.exe / -setup-x86.exe — установщики (для текущего пользователя, без прав администратора);
#   OshiMakiKey-Studio-<версия>-portable-x64.zip / -portable-x86.zip — portable-папка, распаковать и запустить;
#   SHA256SUMS.txt — контрольные суммы.
# Программа внутри самодостаточная (.NET ставить не нужно), установщик — на .NET Framework 4.8 (есть в Windows 10/11).
# Запуск: python tools/build-release.py
import hashlib, os, re, shutil, subprocess, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RELEASE = os.path.join(ROOT, 'release')
BUILD = os.path.join(RELEASE, 'build')
VERSION = re.search(r'Version = "([^"]+)"', open(os.path.join(ROOT, 'MacroPadStudio', 'Branding.cs'), encoding='utf-8').read()).group(1)
NAME = f'OshiMakiKey-Studio-{VERSION}'


def run(*args):
    print('>', ' '.join(args), flush=True)
    subprocess.run(args, check=True, cwd=ROOT)


def zip_folder(source, target, prefix=''):
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for folder, _, files in os.walk(source):
            for name in files:
                path = os.path.join(folder, name)
                z.write(path, prefix + os.path.relpath(path, source).replace(os.sep, '/'))


shutil.rmtree(RELEASE, ignore_errors=True)
os.makedirs(BUILD)
for arch in ('x64', 'x86'):
    app = os.path.join(BUILD, arch)
    run('dotnet', 'publish', 'MacroPadStudio/OshiMakiKeyStudio.csproj', '-c', 'Release', '-r', f'win-{arch}', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None', '-p:DebugSymbols=false', '-o', app)
    for readme in sorted(f for f in os.listdir(ROOT) if f.startswith('README') and f.endswith('.md')):
        shutil.copy(os.path.join(ROOT, readme), app)

    payload = os.path.join(BUILD, f'payload-{arch}.zip')
    zip_folder(app, payload)
    zip_folder(app, os.path.join(RELEASE, f'{NAME}-portable-{arch}.zip'), prefix='OshiMakiKey Studio/')

    setup_out = os.path.join(BUILD, f'setup-{arch}')
    run('dotnet', 'build', 'installer/Setup/Setup.csproj', '-c', 'Release', '--no-incremental', f'-p:PayloadArch={arch}',
        f'-p:PayloadZip={payload}', '-o', setup_out)
    shutil.copy(os.path.join(setup_out, 'OshiMakiKeySetup.exe'), os.path.join(RELEASE, f'{NAME}-setup-{arch}.exe'))

with open(os.path.join(RELEASE, 'SHA256SUMS.txt'), 'w', encoding='utf-8', newline='\n') as sums:
    for name in sorted(os.listdir(RELEASE)):
        path = os.path.join(RELEASE, name)
        if os.path.isfile(path) and name != 'SHA256SUMS.txt':
            sums.write(f'{hashlib.sha256(open(path, "rb").read()).hexdigest()}  {name}\n')

for name in sorted(os.listdir(RELEASE)):
    path = os.path.join(RELEASE, name)
    if os.path.isfile(path):
        print(f'{name:55} {os.path.getsize(path) / 1024 / 1024:6.1f} MB')
