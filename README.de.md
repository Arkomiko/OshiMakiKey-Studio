# OshiMakiKey Studio

[Русский](README.md) · [Беларуская](README.be.md) · [Українська](README.uk.md) · [Қазақша](README.kk.md) · [中文](README.zh.md) · [日本語](README.ja.md) · [English (US)](README.en.md) · [English (UK)](README.en-GB.md) · **Deutsch**

Ein portables Programm zum Einrichten chinesischer Makro-Tastaturen mit **CH552 / CH57x**-Chips (USB `1189:8890`, `1189:8840`, `1189:8842`, `1189:8850`, `514C:8850`, `514C:8851` und verwandte): 3 bis 16 Tasten und bis zu 3 Drehknöpfe.
Jeder Taste und jeder Knopfaktion (nach links drehen, drücken, nach rechts drehen) lässt sich zuweisen:

- **Tasten** — eine Tastenkombination oder eine Folge von bis zu 5 Anschlägen (`Ctrl + C`, dann `Ctrl + V`);
- **Multimedia** — Lautstärke, Pause, Titel, Helligkeit, Rechner, Explorer, Browsertasten;
- **Maus** — Klick links/rechts/Mitte, Mausrad (auch mit Ctrl/Shift/Alt);
- **System** — Programm starten, Website, Datei/Ordner, Text eingeben, cmd-Befehl, Sperren, Bildschirm aus, Energie sparen, Herunterfahren, Neustart.

Jede Aktion trägt im Editor eine Markierung:

- **✓** — wird im Speicher der Tastatur abgelegt und funktioniert auch ohne das Programm, an jedem Computer;
- **⚙** — wird vom Programm oder vom Hintergrunddienst ausgeführt: Das Gerät sendet eine Diensttaste F13–F24, den Rest erledigt das Programm.

Eine Aktion lässt sich von einer Taste auf eine andere **ziehen** (im Bild der Platine oder in der Liste): einfaches Ziehen verschiebt, mit gedrückter **Ctrl**-Taste wird kopiert. Ist die Zieltaste schon belegt, warnt das Programm (die Warnung lässt sich per Häkchen abschalten).

## Start

Version **0.1.6b**. `OshiMakiKeyStudio.exe` liegt im Projektstamm. Benötigt wird die .NET 8 Desktop Runtime.

Alles geschieht in **einem Fenster**: Einrichtungsassistent, Einstellungen, Schreibprüfung und „Über“ öffnen sich anstelle der Platinenansicht. Nur der Tastenmonitor öffnet ein eigenes Fenster. Seiten, Einstellungsbereiche und Themewechsel gehen mit einem weichen Übergang ineinander über.

Beim ersten Start öffnet sich ein **Assistent**: Sprache → Theme → Platine (mit Schreibprüfung für Platinen im Direktformat), danach eine **Fenstertour**, die Bereiche hervorhebt und erklärt (**Hilfe → So funktioniert es**, F1).

1. Schließen Sie die Tastatur mit einem **USB-Kabel** an.
2. Klicken Sie links auf eine Taste oder einen Knopf und wählen Sie rechts eine Aktion.
3. Klicken Sie auf **„In das Gerät schreiben“** (F5) und **stecken Sie das Kabel** der Tastatur **neu ein**.
4. Prüfen Sie im **„Gerät → Tastenmonitor“** (F8): Er zeigt, welche Kombination oder welchen Multimedia-Befehl das Gerät sendet, welcher Taste das zugeordnet ist und wie lange die Taste gehalten wurde.

**Monitor und Datenschutz.** Um die Platine zu sehen, hört der Monitor auf die Eingaben aller Tastaturen. Tasten anderer Geräte nennt er deshalb nie (nur „Taste gedrückt“ und von welchem Gerät, und nur solange das Monitorfenster aktiv ist), und in `monitor.log` landen nur Ereignisse der Platine und Aktionen des Programms — auf einer normalen Tastatur getippter Text gelangt nie dorthin. Fenster und Protokoll behalten nur die **letzten 500 Ereignisse**, damit gehaltene Tasten weder Speicher noch Festplatte füllen.

**Ziehen und Ablegen.** Eine Aktion lässt sich auf eine andere Taste oder auf eine Seite eines Knopfs ziehen: Hervorgehoben wird genau der Teil unter dem Mauszeiger (links drehen, drücken, rechts drehen), und neben der gezogenen Aktion steht, wo sie landet.

Unter „Gerät verbunden“ sieht man, ob das aktuelle Preset in die Tastatur geschrieben ist.

## Hintergrunddienst OMK_Studio-Service

Damit Lautstärke, Titel und andere ⚙-Aktionen auch bei **geschlossenem** Programm funktionieren, installieren Sie den Hintergrunddienst (**Einstellungen → Hintergrunddienst**). Es gibt zwei Varianten:

- **Windows-Dienst** — erscheint in „Dienste“ als *OMK Studio Service* und startet mit Windows. Dienste laufen in einer eigenen Sitzung 0 ohne Zugriff auf Tastatur und Ton, deshalb startet der Dienst einen **Helfer** in der Sitzung des angemeldeten Benutzers (dieselbe exe mit `--service`) und startet ihn bei Anmeldung, Entsperren und Absturz neu. Installieren und Entfernen fragen nach Administratorrechten (UAC-Fenster). Wird der Programmordner verschoben, muss der Dienst neu installiert werden — die Einstellungen erinnern daran.
- **Autostart ohne Administratorrechte** — derselbe Helfer startet bei der Windows-Anmeldung aus `HKCU\...\Run` („Task-Manager → Autostart“).

Der Helfer merkt sich das gewählte **Dienst-Preset** und gibt die Diensttasten frei, solange das Programm geöffnet ist. Das Dienst-Preset lässt sich in den Einstellungen, im Menü des Programmsymbols im Infobereich und im Menü des Helfersymbols ändern (es ist sichtbar, wenn das Programmfenster geschlossen ist).

**Tastenwiederholung.** Solange eine Taste gehalten wird, wiederholt Windows sie wie bei jeder Tastatur. Stört das, aktivieren Sie **Einstellungen → Tasten → „Kombination nicht wiederholen, solange die Taste gehalten wird“**: Dann führt das Programm (oder der Dienst) die Kombination genau einmal pro Druck aus.

## Programmordner

Das Programm ist portabel: Im Windows-Profil wird nichts gespeichert, alles liegt neben der exe.

```
OshiMakiKeyStudio.exe
drivers/      Platinenbeschreibungen (*.json): USB-ID, Schreibformat, Belegung, Symbolfarben
themes/       Themes (Skins)
  light/, dark/      Standard: 13 helle und 13 dunkle (*.json)
  custom/            eigene: *.json, *.ts, *.zip-Pakete und Paketordner (minecraft.zip liegt bei)
language/     Sprachpakete (*.json): ru, be, uk, kk, zh, ja, en, en-GB, de
user_data/    Benutzerdaten — werden bei Updates nie überschrieben
  settings.json      allgemeine Einstellungen: Theme, Sprache, Treiber, Autostart…
  presets/*.json     Presets — Belegungen von Tasten und Knöpfen, je eine Datei
  working.json       Arbeitskopie des aktiven Presets (ungespeicherte Änderungen)
  monitor.log        Protokoll von Tastenmonitor und Schreibprüfung (letzte 500 Ereignisse)
  diagnostics-*.log  Berichte der Schreibprüfung (14 Tage, höchstens 10 Dateien)
```

Der Ordner lässt sich komplett verschieben — auf ein anderes Laufwerk, einen USB-Stick oder einen anderen Computer. Außerhalb davon schreibt das Programm nur Autostart-Einträge in `HKCU\...\Run` und den Windows-Dienst, und nur wenn Sie sie aktivieren. Der Ordner `theme` früherer Versionen wird weiterhin gelesen.

**Presets.** Das aktive Preset wird in der Kopfzeile des Fensters gewählt. Änderungen landen sofort in der Arbeitskopie, in der Preset-Datei mit der Schaltfläche **„Speichern“** (Diskette, Ctrl+S): Gibt es schon eine Datei mit diesem Namen, schlägt das Programm einen neuen Namen oder Überschreiben vor. Im Menü „⋯“ finden Sie „Preset speichern unter…“, „Liste aktualisieren“, „Preset-Ordner öffnen“; alles Weitere im Menü **Datei**.

**Themes** (**Einstellungen → Darstellung**). Theme und Sprache werden **sofort bei der Auswahl** angewendet, „Abbrechen“ stellt die vorherigen wieder her. Die Standard-Themes haben einen gemeinsamen hellen oder dunklen Hintergrund und 13 Akzente (Rot, Koralle, Orange, Bernstein, Gelb, Limette, Grün, Türkis, Himmelblau, Blau, Violett, Purpur, Rosa); in den Einstellungen sind das Kreise in Akzentfarbe in der einklappbaren Gruppe „Standard“. Darunter folgt die Gruppe „Eigene“ mit Vorschaubildern der Themes aus `themes/custom`.

Ein Theme ist `{"id", "name", "order", "dark", "colors": {…}}`; Farben, die in der Datei fehlen, werden aus den Grundfarben abgeleitet (`window`, `surface`, `text`, `accent`). Die vollständige Farbliste steht in jeder `themes/light/*.json`. Optionale Felder: `corners` (Eckenrundung, 0 — eckig), `background` (eingebaute animierte Szene, derzeit `minecraft`), `backgroundImage` (Hintergrundbild aus dem Paket), `animationFps` (Bildrate für `animate`).

Formate eigener Themes:
- `*.json` — wie die Standard-Themes;
- `*.ts` — dasselbe Objekt in TypeScript-Syntax (`export default { id: 'my', colors: { accent: '#FF8800' } };`): Schlüssel ohne Anführungszeichen, Zeichenketten in einfachen Anführungszeichen, Kommentare und nachgestellte Kommas sind erlaubt. Die Datei wird als Daten gelesen;
- `*.zip` oder ein Ordner — ein Paket: `theme.ts` (oder `theme.json`) und die Ordner `img` (Bilder: Hintergrund, Szenentexturen), `animate` (Bilder des animierten Hintergrunds, nach Namen sortiert) und `scripts` (Theme-Skripte).

**Theme-Skripte** (`scripts/*.js`) laufen in einer eingebauten JavaScript-**Sandbox** (Jint): ohne Zugriff auf Dateien, Netzwerk, Prozesse und .NET, mit Grenzen für Zeit, Speicher und Rekursionstiefe bei jedem Aufruf. Ein hängendes oder abstürzendes Skript wird mit einer Meldung abgeschaltet, das Programm läuft weiter. Skripten steht zur Verfügung:

```js
theme.onFrame(function (g, t, w, h) { … });     // in jedem Bild über den Theme-Hintergrund zeichnen (t — Sekunden)
theme.addWindow({ title, menu, width, height,     // ein eigenes Fenster — Eintrag im Menü „Hilfe“
                  onFrame(g, t, w, h), onClick(x, y) });
theme.hour(); theme.minute(); theme.log('Text');
// Leinwand g: rect(x, y, w, h, '#RRGGBB'), circle(x, y, r, Farbe), line(x1, y1, x2, y2, Farbe, Stärke),
//             text(Zeichenkette, x, y, Farbe, Größe), image('Name aus img', x, y, w, h), width, height
```

Hat ein Theme einen eigenen Hintergrund, ist er in den Lücken zwischen den Karten des Hauptfensters (bei solchen Themes breiter), in den Einstellungen und auf der Seite „Über“ zu sehen. Mitgeliefert wird das Theme **„Minecraft-Stil“** (`themes/custom/minecraft.zip`): eckige Ecken, eine Palette aus Stein und Gras, ein Himmel **nach der Uhr des Computers** (tagsüber Sonne und Vögel, bei Sonnenuntergang ein oranger Horizont, nachts Sterne, Mond und Glühwürmchen), Wolken und Gelände aus Blöcken sowie im Menü „Hilfe“ ein Fenster „Werkbank“ aus dem Theme-Skript. Das Paket wird mit `tools/make-minecraft-theme.py` gebaut.

**Sprachen** (**Einstellungen → Sprache**): `language/<Code>.json` der Form `{"code", "name", "strings": {Schlüssel: Text}}`. Russisch ist im Programm eingebaut; `ru.json` wird von `tools/lang-keys.ps1` aus dem Code erzeugt, das Skript zeigt auch, welche Schlüssel in anderen Sprachen fehlen. Fehlt ein Schlüssel in einer Übersetzung, erscheint der russische Text.

**Treiber** (**Einstellungen → Gerät** oder **Gerät → Treiber**): werden automatisch nach USB-ID und Art der Multimedia-Schnittstelle gewählt; weicht Ihre Platine vom Bild ab, wählen Sie die passende Belegung von Hand. Jeder Treiber hat ein eigenes Symbol, gezeichnet nach Belegung und Farben aus der Datei.

**Updates und Dateien** (**Hilfe → Nach Updates und Dateien suchen** oder **Einstellungen → Dateien und Hilfe**): vergleicht die Version mit dem neuesten Release auf GitHub und prüft, ob die Dateien für Treiber, Themes und Sprachen vorhanden und lesbar sind. Das Programm selbst lädt nichts herunter — gibt es eine neue Version, können Sie die Download-Seite öffnen.

**Wenn Dateien fehlen.** Werden alle Themes, Sprachen oder Treiber gelöscht, läuft das Programm mit eingebauten Einstellungen und zeigt unter der Kopfzeile einen Hinweis, nach Updates zu suchen.

## Platinen und Schreibformate

| Format | Platinen | Rahmen |
|---|---|---|
| Direkt | `1189:8890`, CH552 mit Bitmap-Multimedia-Schnittstelle (3 oder 6 Tasten + Knopf) | `[Taste] [Mod] 00 [Code1…Code6]`, am Ende `AA AA` |
| k8890 (ch57x-keyboard-tool) | `1189:8890`, Multimedia als 16-Bit-Code | `03 FE …`, `03 [Taste] [Ebene/Typ] …`, `03 AA AA` |
| Offizielles Programm, „Version 0“ | `1189:8890`, manuell | `[Taste] 01 [Anzahl] [Nr.] [Mod] [Code]`, nach jeder Taste `AA AA` |
| k884x | `1189:8840`, `8842`, `8850`, `8830`–`8833`, `8810`, `514C:8851` — 3 bis 15 Tasten und bis zu 3 Knöpfe | `03 FE [Taste] [Ebene] [Typ] …`, dann `03 AA AA`, `03 FD FE FF`, `03 AA AA` |
| k8850 (nicht auf Hardware geprüft) | `514C:8850`, 4×4-Raster | `03 FD [Taste] [Ebene] [Typ] …`, dann `03 FD FE FF` |

**Wie das Direktformat ermittelt wurde.** Eine schrittweise Diagnose zeigte: Die Firmware wertet keine Felder aus. Das erste Byte des Rahmens ist die Tastennummer, die nächsten 8 Bytes speichert sie als fertigen HID-Tastaturbericht. Deshalb landeten Rahmen mit dem Präfix `03` auf Taste 3, und Multimedia wurde zu Modifikatoren — die „Modifikator-Warteschlangen“ aus [#168](https://github.com/kriomant/ch57x-keyboard-tool/issues/168).

Eine Direktplatine speichert selbst eine Tastenkombination pro Taste; Multimedia, Maus, Folgen und Systemaktionen führt das Programm aus (⚙). Tastennummern beim CH552 mit 6 Tasten + Knopf: Tasten 1–6 — 1–6, Knopf: links — 11, drücken — 12, rechts — 13. Auf anderen Platinen im Direktformat schreibt **Gerät → Schreiben prüfen…** Testbuchstaben in die Tasten 1–30 (a–z, dann Ziffern), bittet Sie, jede Taste und jeden Knopf zu drücken, und merkt sich die gefundenen Nummern.

## Brauche ich einen Windows-Treiber?

Nein. Das Gerät ist ein Standard-USB-HID-Gerät (Tastatur, Maus, Multimedia und eine Dienstschnittstelle zum Programmieren). Das Programm schreibt die Einstellungen über die Standard-`hid.dll` in die Dienstschnittstelle, ohne Administratorrechte und ohne UsbDk/Zadig. Die „Treiber“ des Programms sind Platinenbeschreibungen im Ordner `drivers`.

```
Taste am Gerät ──► das Gerät sendet eine Diensttaste (F13…F24, Shift/Ctrl + F13…)
                                   │
                                   ▼
OshiMakiKey Studio oder Dienst-Helfer ── RegisterHotKey ──► Multimedia / Maus / Start / Text / …
```

## Projektstruktur

```
OshiMakiKeyStudio.exe           fertiges Programm (portabel)
README*.md                      Beschreibung in 9 Sprachen (Russisch ist die Hauptsprache)
drivers/ themes/ language/      Programmressourcen (siehe „Programmordner“)
tools/lang-keys.ps1             erzeugt language/ru.json aus dem Code und prüft Übersetzungen
tools/make-minecraft-theme.py   baut themes/custom/minecraft.zip (theme.ts, img, animate, scripts)
docs/drafts/                    Entwürfe noch nicht umgesetzter Ideen
.vscode/                        Arbeitsbereich (OshiMakiKey-Studio.code-workspace), Start, Aufgaben und VS-Code-Erweiterungen
.vs/OshiMakiKey-Studio.sln      Visual-Studio-Projektmappe (der Rest von .vs ist Cache, nicht in git)
.editorconfig                   gemeinsamer Codestil für Visual Studio und VS Code
MacroPadStudio/                 Quellcode; Projekt — OshiMakiKeyStudio.csproj
  Device/   *Protocol.cs        Schreibformate: direkt, k8890, v0, k884x, k8850
            ProfileCompiler.cs  Preset → Rahmen
            MacroPadDevice.cs   Dienstschnittstelle, Schreiben der Rahmen, Treibererkennung
  Model/    Profile.cs          Tasten, Aktionen, Ebenen, Diensttasten, was der Agent ausführt
            Drivers.cs          Platinenbeschreibungen: drivers/*.json und eingebaute
            UpdateCheck.cs      Prüfung von GitHub-Releases und Programmdateien
            SettingsStore.cs, PresetStore.cs, L.cs, Notices.cs, LogFiles.cs, Catalogs.cs, BindingText.cs
  Agent/    HotkeyAgent.cs, ActionRunner.cs, RawInput.cs, Autostart.cs
            OmkService.cs       Dienst-Helfer in der Benutzersitzung (--service), Symbol im Infobereich
            WinService.cs       Windows-Dienst (--winservice): startet den Helfer, Installation über UAC
  UI/       MainForm — das einzige Fenster; Seiten SettingsPage, SetupPage, WriteCheckPage, AboutPage;
            Overlay + ThemedDialog / InputDialog — Dialoge über dem Fenster; Transition — weiche Übergänge;
            ThemeStore / ThemePicker / ThemeBackdrop / ThemeScripts — Themes, ihre Hintergründe und Skripte in der Sandbox;
            MonitorForm, TourOverlay, DevicePanel, BindingEditor, DriverIcon, Theme u. a.
  SelfTest.cs                   Prüfungen ohne Oberfläche
```

## Bauen

```bash
dotnet build MacroPadStudio
```

Öffnen Sie in VS Code `.vscode/OshiMakiKey-Studio.code-workspace` (VS Code schlägt die empfohlenen Erweiterungen vor: C# Dev Kit, C#, PowerShell, EditorConfig): Dort gibt es fertige Startkonfigurationen (normal, im Infobereich, Dienst, Fensterbild, Selbsttest) und Aufgaben (Bauen, Veröffentlichen, Übersetzungsprüfung, Ziehtest, Bildschirmbilder).

Öffnen Sie in Visual Studio `.vs/OshiMakiKey-Studio.sln` (benötigt die Workload „.NET-Desktopentwicklung“); Startprofile stehen in `MacroPadStudio/Properties/launchSettings.json`.

Nach Textänderungen im Code das russische Paket neu erzeugen und die Übersetzungen prüfen:

```bash
powershell -ExecutionPolicy Bypass -File tools/lang-keys.ps1
```

Portable exe (Veröffentlichung in einen temporären Ordner, danach kommt die exe in den Projektstamm):

```bash
dotnet publish MacroPadStudio -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o publish
```

Kommandozeilenparameter von `OshiMakiKeyStudio.exe`:

- `--selftest bericht.txt` — Schreibformate (mit Referenzrahmen), Belegungen, Diensttasten, Presets, Theme-, Sprach- und Treiberdateien, Betrieb ohne sie, Sandbox der Theme-Skripte;
- `--probe bericht.txt` — Gerät suchen und Treiber erkennen (ändert keine Einstellungen);
- `--drop-test bericht.txt` — zieht die Aktion von Taste 5 auf jede Seite des Knopfs und auf Taste 1 (Bild, Liste, Kopie mit Ctrl), PASS/FAIL;
- `--snapshot fenster.png [--select Key1] [--kind System] [--theme dark-blue] [--lang en] [--driver id] [--tour N] [--page settings|about|setup] [--switch-theme id] [--switch-lang Code] [--preview-theme id] [--sky-hour 0..23]` — Bild des Hauptfensters (Tourschritt, Seite; `--switch-*` ändern Theme oder Sprache eines offenen Fensters, `--preview-theme` wählt ein Theme in den offenen Einstellungen, `--sky-hour` setzt die Stunde für den Himmel animierter Themes);
- `--form-snapshot settings0…6 | about | setup-0…2 | overwrite datei.png` — Bild einer einzelnen Seite oder eines Dialogs;
- `--monitor-snapshot datei.png` — Bild des Tastenmonitors mit Beispielzeilen (ohne Tastendrücke);
- `--service` — Dienst-Helfer; `--winservice` — Windows-Dienst (vom Dienststeuerungs-Manager gestartet); `--install-winservice` / `--remove-winservice` — Dienst installieren und entfernen (mit Administratorrechten); `--tray` — minimiert im Infobereich starten.

## Einschränkungen

- Einstellungen lassen sich nicht aus dem Gerät zurücklesen: Die Firmware unterstützt nur das Schreiben.
- Geschrieben werden kann nur per USB-Kabel.
- Ebenen gibt es nur in den Formaten k8890, k884x und k8850.
- Eine Direktplatine beherrscht selbst kein Multimedia, keine Maus und keine Tastenfolgen — dafür braucht es das Programm oder den Hintergrunddienst.
- Die Formate k884x und k8850 wurden aus dem Quellcode von ch57x-keyboard-tool rekonstruiert und mit diesem Programm noch nicht auf echten Platinen geprüft.
- Die Helligkeit kann das Programm nur beim eingebauten Bildschirm eines Laptops ändern (so erlaubt es Windows).

## Danksagung

Die Protokolle wurden nach den offenen Projekten [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool) und [rOzzy1987/MacroPad](https://github.com/rOzzy1987/MacroPad) rekonstruiert. Theme-Skripte führt [Jint](https://github.com/sebastienros/jint) aus.
