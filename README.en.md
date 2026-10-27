# OshiMakiKey Studio

[Русский](README.md) · [Беларуская](README.be.md) · [Українська](README.uk.md) · [Қазақша](README.kk.md) · [中文](README.zh.md) · [日本語](README.ja.md) · **English (US)** · [English (UK)](README.en-GB.md) · [Deutsch](README.de.md)

**⬇ [Download the installer (x64/x86) or portable](https://github.com/Arkomiko/OshiMakiKey-Studio/releases/latest)**

A portable configurator for Chinese macro keyboards based on **CH552 / CH57x** chips (USB `1189:8890`, `1189:8840`, `1189:8842`, `1189:8850`, `514C:8850`, `514C:8851` and related): from 3 to 16 keys and up to 3 rotary knobs.
Every key and every knob action (turn left, press, turn right) can be assigned:

- **Keys** — a shortcut or a sequence of up to 5 presses (`Ctrl + C`, then `Ctrl + V`);
- **Media** — volume, pause, tracks, brightness, calculator, Explorer, browser buttons;
- **Mouse** — left/right/middle click, wheel (also with Ctrl/Shift/Alt);
- **System** — run a program, open a website, file or folder, type text, cmd command, lock, screen off, sleep, shut down, restart.

Each action in the editor has a mark:

- **✓** — stored in the keyboard's memory and works even without the program, on any computer;
- **⚙** — performed by the program or the background service: the device sends a service key F13–F24 and the program does the rest.

An action can be **dragged** from one button to another (on the board picture or in the list): plain drag moves it, with **Ctrl** held it copies. If the target button already has an action, the program warns you (the warning can be turned off with a checkbox).

## Getting started

Version **0.1.6b**. `OshiMakiKeyStudio.exe` is in the project root. Requires the .NET 8 Desktop Runtime.

Everything happens in **one window**: the first-run wizard, settings, the write check and "About" open in place of the board view. Only the key monitor opens as a separate window. Pages, settings sections and theme changes switch with a smooth transition.

On first launch a **wizard** opens: language → theme → board (with a write check for direct-format boards), followed by a **window tour** that highlights areas and explains what they do (**Help → How it works**, F1).

1. Connect the keyboard with a **USB cable**.
2. Click a key or knob on the left, choose an action on the right.
3. Press **"Write to device"** (F5) and **reconnect** the keyboard cable.
4. Check in **"Device → Key monitor"** (F8): it shows which shortcut or media command the device sends, which button it is assigned to and how long the button was held.

**Monitor and privacy.** To see the board, the monitor listens to input from all keyboards, so it never names keys of other devices (only "key pressed" and which device, and only while the monitor window is active), and `monitor.log` receives only board events and program actions — text typed on a regular keyboard never gets there. Both the window and the log keep only the **last 500 events**, so holding a button does not fill up memory or disk.

**Drag and drop.** An action can be dragged onto another key or onto a side of a knob: the exact part under the cursor is highlighted (turn left, press, turn right), and the dragged action shows where it will land.

Under "Device connected" you can see whether the current preset has been written to the keyboard.

## Background service OMK_Studio-Service

To make volume, tracks and other ⚙ actions work while the program is **closed**, install the background service (**Settings → Background service**). There are two options:

- **Windows service** — listed in "Services" as *OMK Studio Service* and starts with Windows. Services run in a separate session 0 without access to the keyboard and sound, so the service starts a **helper** in the signed-in user's session (the same exe with `--service`) and restarts it on sign-in, unlock and crash. Installing and removing ask for administrator rights (UAC prompt). If you move the program folder, reinstall the service — the settings will remind you.
- **Autostart without administrator rights** — the same helper starts when you sign in to Windows from `HKCU\...\Run` ("Task Manager → Startup").

The helper remembers the selected **service preset** and gives the service keys back while the program is open. The service preset can be changed in the settings, in the program's tray icon menu and in the helper's own tray icon menu (shown while the program window is closed).

**Key repeat.** While a key is held, Windows repeats it, as on any keyboard. If that gets in the way, enable **Settings → Keys → "Don't repeat the shortcut while the key is held"**: shortcuts will then be performed by the program (or the service) exactly once per press.

## Program folders

The program is portable: nothing is stored in the Windows profile, everything lives next to the exe.

```
OshiMakiKeyStudio.exe
drivers/      board descriptions (*.json): USB ID, write format, layout, icon colors
themes/       themes (skins)
  light/, dark/      standard: 13 light and 13 dark (*.json)
  custom/            custom: *.json, *.ts, *.zip packages and package folders (minecraft.zip included)
language/     language packs (*.json): ru, be, uk, kk, zh, ja, en, en-GB, de
user_data/    user data — never overwritten by updates
  settings.json      general settings: theme, language, driver, autostart…
  presets/*.json     presets — key and knob layouts, one file each
  working.json       working copy of the active preset (unsaved edits)
  monitor.log        key monitor and write check log (last 500 events)
  diagnostics-*.log  write check reports (kept for 14 days, at most 10 files)
```

The folder can be moved as a whole — to another drive, a USB stick or another computer. Outside it the program writes only autostart entries in `HKCU\...\Run` and the Windows service, and only if you enable them. The `theme` folder of earlier versions is still read.

**Presets.** The active preset is chosen in the window header. Edits go straight into the working copy, and into the preset file with the **"Save"** button (floppy icon, Ctrl+S): if a file with that name already exists, the program offers to pick a new name or overwrite it. The "⋯" menu has "Save preset as…", "Refresh list", "Open presets folder"; everything else is in the **File** menu.

**Themes** (**Settings → Appearance**). Theme and language apply **as soon as you pick them**; "Cancel" brings back the previous ones. Standard themes share a light or dark background and come in 13 accents (red, coral, orange, amber, yellow, lime, green, teal, sky, blue, violet, purple, pink); in the settings they are accent-colored dots in the collapsible "Standard" group. Below is the "Custom" group with previews of themes from `themes/custom`.

A theme is `{"id", "name", "order", "dark", "colors": {…}}`; colors missing from the file are derived from the main ones (`window`, `surface`, `text`, `accent`). The full color list is in any `themes/light/*.json`. Optional fields: `corners` (corner rounding, 0 — square), `background` (built-in animated scene, currently `minecraft`), `backgroundImage` (background picture from the package), `animationFps` (frame rate for `animate`).

Custom theme formats:
- `*.json` — same as the standard ones;
- `*.ts` — the same object in TypeScript syntax (`export default { id: 'my', colors: { accent: '#FF8800' } };`): unquoted keys, single-quoted strings, comments and trailing commas are allowed. The file is read as data;
- `*.zip` or a folder — a package: `theme.ts` (or `theme.json`) and the folders `img` (pictures: background, scene textures), `animate` (animated background frames, in name order) and `scripts` (theme scripts).

**Theme scripts** (`scripts/*.js`) run in a built-in JavaScript **sandbox** (Jint): no access to files, network, processes or .NET, with time, memory and recursion limits on every call. A hanging or crashing script is disabled with a notice and the program keeps working. Scripts can use:

```js
theme.onFrame(function (g, t, w, h) { … });     // draw over the theme background every frame (t — seconds)
theme.addWindow({ title, menu, width, height,     // a window of its own — an item in the Help menu
                  onFrame(g, t, w, h), onClick(x, y) });
theme.hour(); theme.minute(); theme.log('text');
// canvas g: rect(x, y, w, h, '#RRGGBB'), circle(x, y, r, color), line(x1, y1, x2, y2, color, width),
//           text(string, x, y, color, size), image('name from img', x, y, w, h), width, height
```

If a theme has its own background, it shows in the gaps between the cards of the main window (wider for such themes), in the settings and on the "About" page. Included is the **"Minecraft style"** theme (`themes/custom/minecraft.zip`): square corners, a stone-and-grass palette, a sky that **follows the computer clock** (sun and birds by day, an orange horizon at sunset, stars, moon and fireflies at night), clouds and block terrain, plus a "Crafting table" window from the theme script in the Help menu. The package is built by `tools/make-minecraft-theme.py`.

**Languages** (**Settings → Language**): `language/<code>.json` of the form `{"code", "name", "strings": {key: text}}`. Russian is built into the program; `ru.json` is generated from the code by `tools/lang-keys.ps1`, which also shows which keys are missing in other languages. If a key is missing from a translation, the Russian text is shown.

**Drivers** (**Settings → Device** or **Device → Driver**): picked automatically by USB ID and the kind of media interface; if your board differs from the picture, choose the matching layout manually. Each driver has its own icon drawn from the layout and colors in the file.

**Updates and files** (**Help → Check for updates and files** or **Settings → Files and help**): compares your version with the latest GitHub release and checks that driver, theme and language files are present and readable. The program itself downloads nothing — if there is a new version, you can open the download page.

**If files are missing.** If all themes, languages or drivers are deleted, the program runs on built-in settings and shows a notice under the header suggesting to check for updates.

## Boards and write formats

| Format | Boards | Frame |
|---|---|---|
| Direct | `1189:8890`, CH552 with a bitmap media interface (3 or 6 keys + knob) | `[button] [mod] 00 [code1…code6]`, ending with `AA AA` |
| k8890 (ch57x-keyboard-tool) | `1189:8890`, 16-bit media code | `03 FE …`, `03 [button] [layer/type] …`, `03 AA AA` |
| Official program, "version 0" | `1189:8890`, manual | `[button] 01 [count] [index] [mod] [code]`, `AA AA` after each button |
| k884x | `1189:8840`, `8842`, `8850`, `8830`–`8833`, `8810`, `514C:8851` — 3 to 15 keys and up to 3 knobs | `03 FE [button] [layer] [type] …`, then `03 AA AA`, `03 FD FE FF`, `03 AA AA` |
| k8850 (not tested on hardware) | `514C:8850`, 4×4 grid | `03 FD [button] [layer] [type] …`, then `03 FD FE FF` |

**How the direct format was found.** Step-by-step diagnostics showed that the firmware does not parse fields. The first byte of the frame is the button number, and it stores the next 8 bytes as a ready-made keyboard HID report. That is why frames with the `03` prefix ended up on button 3 and media turned into modifiers — the "modifier queues" from [#168](https://github.com/kriomant/ch57x-keyboard-tool/issues/168).

A direct board itself stores one shortcut per button; media, mouse, sequences and system actions are performed by the program (⚙). Button numbers on the CH552 6 keys + knob: keys 1–6 — 1–6, knob: left — 11, press — 12, right — 13. On other direct-format boards, **Device → Check write…** writes test letters to buttons 1–30 (a–z, then digits), asks you to press every key and knob and remembers the numbers it finds.

## Do I need a Windows driver?

No. The device is a standard USB HID device (keyboard, mouse, media and a service interface for programming). The program writes settings to the service interface through the standard `hid.dll`, without administrator rights and without UsbDk/Zadig. The program's "drivers" are board descriptions in the `drivers` folder.

```
key on the device ──► the device sends a service key (F13…F24, Shift/Ctrl + F13…)
                                   │
                                   ▼
OshiMakiKey Studio or the service helper ── RegisterHotKey ──► media / mouse / launch / text / …
```

## Project structure

```
OshiMakiKeyStudio.exe           the ready program (portable)
README*.md                      description in 9 languages (Russian is the main one)
drivers/ themes/ language/      program resources (see "Program folders")
tools/lang-keys.ps1             builds language/ru.json from the code and checks translations
tools/make-minecraft-theme.py   builds themes/custom/minecraft.zip (theme.ts, img, animate, scripts)
docs/drafts/                    drafts of ideas not implemented yet
.vscode/                        workspace (OshiMakiKey-Studio.code-workspace), launch, tasks and VS Code extensions
.vs/OshiMakiKey-Studio.sln      Visual Studio solution (the rest of .vs is Visual Studio cache, not in git)
.editorconfig                   shared code style for Visual Studio and VS Code
MacroPadStudio/                 sources; project — OshiMakiKeyStudio.csproj
  Device/   *Protocol.cs        write formats: direct, k8890, v0, k884x, k8850
            ProfileCompiler.cs  preset → frames
            MacroPadDevice.cs   service interface, frame writing, driver detection
  Model/    Profile.cs          buttons, actions, layers, service keys, what the agent performs
            Drivers.cs          board descriptions: drivers/*.json and built-in ones
            UpdateCheck.cs      GitHub release and program file check
            SettingsStore.cs, PresetStore.cs, L.cs, Notices.cs, LogFiles.cs, Catalogs.cs, BindingText.cs
  Agent/    HotkeyAgent.cs, ActionRunner.cs, RawInput.cs, Autostart.cs
            OmkService.cs       service helper in the user session (--service), tray icon
            WinService.cs       Windows service (--winservice): starts the helper, installed via UAC
  UI/       MainForm — the only window; pages SettingsPage, SetupPage, WriteCheckPage, AboutPage;
            Overlay + ThemedDialog / InputDialog — dialogs over the window; Transition — smooth transitions;
            ThemeStore / ThemePicker / ThemeBackdrop / ThemeScripts — themes, their backgrounds and sandboxed scripts;
            MonitorForm, TourOverlay, DevicePanel, BindingEditor, DriverIcon, Theme and others
  SelfTest.cs                   checks without UI
```

## Building

```bash
dotnet build MacroPadStudio
```

In VS Code open `.vscode/OshiMakiKey-Studio.code-workspace` (VS Code will suggest the recommended extensions: C# Dev Kit, C#, PowerShell, EditorConfig): it has ready launch configurations (normal, tray, service, window snapshot, self-test) and tasks (build, publish, translation check, drag test, snapshots).

In Visual Studio open `.vs/OshiMakiKey-Studio.sln` (requires the ".NET desktop development" workload); launch profiles are in `MacroPadStudio/Properties/launchSettings.json`.

After editing texts in the code, rebuild the Russian pack and check translations:

```bash
powershell -ExecutionPolicy Bypass -File tools/lang-keys.ps1
```

Portable exe (publish to a temporary folder, then put the exe into the project root):

```bash
dotnet publish MacroPadStudio -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o publish
```

Command-line options of `OshiMakiKeyStudio.exe`:

- `--selftest report.txt` — write formats (with reference frames), layouts, service keys, presets, theme, language and driver files, running without them, theme script sandbox;
- `--probe report.txt` — find the device and detect the driver (does not change settings);
- `--drop-test report.txt` — drags the action of key 5 onto every side of the knob and onto key 1 (picture, list, copy with Ctrl), PASS/FAIL;
- `--snapshot window.png [--select Key1] [--kind System] [--theme dark-blue] [--lang en] [--driver id] [--tour N] [--page settings|about|setup] [--switch-theme id] [--switch-lang code] [--preview-theme id] [--sky-hour 0..23]` — snapshot of the main window (tour step, page; `--switch-*` change the theme or language of an open window, `--preview-theme` picks a theme in the open settings, `--sky-hour` sets the hour for the sky of animated themes);
- `--form-snapshot settings0…6 | about | setup-0…2 | overwrite file.png` — snapshot of a single page or dialog;
- `--monitor-snapshot file.png` — snapshot of the key monitor with sample lines (no key presses);
- `--service` — service helper; `--winservice` — Windows service (started by the service manager); `--install-winservice` / `--remove-winservice` — install and remove the service (with administrator rights); `--tray` — start minimized to the tray.

## Limitations

- Settings cannot be read back from the device: the firmware only supports writing.
- Writing works only over a USB cable.
- Layers exist only in the k8890, k884x and k8850 formats.
- A direct board cannot do media, mouse or press sequences by itself — they need the program or the background service.
- The k884x and k8850 formats were reconstructed from the ch57x-keyboard-tool sources and have not been tested on real boards with this program yet.
- Brightness can be changed by the program only on a laptop's built-in screen (that is what Windows allows).

## Acknowledgements

The protocols were reconstructed from the open projects [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool) and [rOzzy1987/MacroPad](https://github.com/rOzzy1987/MacroPad). Theme scripts are run by [Jint](https://github.com/sebastienros/jint).
