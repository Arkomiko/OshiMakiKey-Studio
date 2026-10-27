# OshiMakiKey Studio

[Русский](README.md) · [Беларуская](README.be.md) · [Українська](README.uk.md) · [Қазақша](README.kk.md) · **中文** · [日本語](README.ja.md) · [English (US)](README.en.md) · [English (UK)](README.en-GB.md) · [Deutsch](README.de.md)

**⬇ [下载安装程序（x64/x86）或便携版](https://github.com/Arkomiko/OshiMakiKey-Studio/releases/latest)**

用于配置基于 **CH552 / CH57x** 芯片的国产宏键盘的便携程序（USB `1189:8890`、`1189:8840`、`1189:8842`、`1189:8850`、`514C:8850`、`514C:8851` 及同类设备）：3 到 16 个按键，最多 3 个旋钮编码器。
每个按键和旋钮的每个动作（左转、按下、右转）都可以分配：

- **按键** —— 组合键或最多 5 次按键的序列（`Ctrl + C`，然后 `Ctrl + V`）；
- **多媒体** —— 音量、暂停、曲目、亮度、计算器、资源管理器、浏览器按钮；
- **鼠标** —— 左/右/中键单击、滚轮（也可配合 Ctrl/Shift/Alt）；
- **系统** —— 启动程序、网站、文件/文件夹、输入文本、cmd 命令、锁定、关闭屏幕、睡眠、关机、重启。

编辑器中每个动作都有标记：

- **✓** —— 写入键盘内存，即使没有本程序、在任何电脑上都能工作；
- **⚙** —— 由程序或后台服务执行：设备发送服务键 F13–F24，其余由程序完成。

动作可以从一个按钮**拖动**到另一个按钮（在电路板图上或列表中）：直接拖动为移动，按住 **Ctrl** 为复制。如果目标按钮已有动作，程序会提醒（可用复选框关闭提醒）。

## 运行

版本 **0.1.6b**。`OshiMakiKeyStudio.exe` 位于项目根目录。需要 .NET 8 Desktop Runtime。

一切都在**同一个窗口**中进行：首次运行向导、设置、写入检查和“关于”都在电路板视图的位置打开。只有按键监视器在单独窗口中打开。页面、设置分区和主题切换都以平滑过渡切换。

首次运行时会打开**向导**：语言 → 主题 → 电路板（直接格式的电路板带写入检查），随后是**窗口导览**——高亮各区域并说明其作用（**帮助 → 工作原理**，F1）。

1. 用 **USB 数据线**连接键盘。
2. 在左侧单击按键或旋钮，在右侧选择动作。
3. 点击 **“写入设备”**（F5），然后**重新插拔**键盘数据线。
4. 在 **“设备 → 按键监视器”**（F8）中检查：它显示设备发送的组合键或多媒体命令、对应的按钮以及按钮按住的时长。

**监视器与隐私。** 为了看到电路板，监视器会监听所有键盘的输入，因此它从不显示其他设备的按键名称（只显示“按下了按键”及来自哪个设备，并且仅在监视器窗口处于活动状态时），`monitor.log` 只记录电路板事件和程序动作——在普通键盘上输入的文本绝不会写入。窗口和日志都只保留**最近 500 个事件**，长按按钮不会占满内存和磁盘。

**拖放。** 动作可以拖到另一个按键或旋钮的某一侧：光标下的具体部分会高亮（左转、按下、右转），被拖动的动作旁会显示它将落在哪里。

在“设备已连接”下方可以看到当前预设是否已写入键盘。

## 后台服务 OMK_Studio-Service

要在程序**关闭**时音量、曲目等带 ⚙ 的动作仍能工作，请安装后台服务（**设置 → 后台服务**）。有两种方式：

- **Windows 服务** —— 在“服务”列表中显示为 *OMK Studio Service*，随 Windows 启动。服务运行在独立的会话 0 中，无法访问键盘和声音，因此服务会在已登录用户的会话中启动**助手**（带 `--service` 参数的同一个 exe），并在登录、解锁和崩溃时重启它。安装和删除需要管理员权限（UAC 窗口）。如果移动了程序文件夹，需要重新安装服务——设置中会提醒。
- **无需管理员权限的开机启动** —— 同一个助手在登录 Windows 时通过 `HKCU\...\Run` 启动（“任务管理器 → 启动”）。

助手会记住所选的**服务预设**，并在程序打开时让出服务键。服务预设可以在设置中、程序托盘图标菜单中以及助手自己的托盘图标菜单中更改（程序窗口关闭时它显示在托盘中）。

**自动重复。** 按住按键时，Windows 会像在任何键盘上一样重复按键。如果这有影响，请启用 **设置 → 按键 → “按住按键时不重复组合键”**：组合键将由程序（或服务）执行，每次按下只执行一次。

## 程序文件夹

程序是便携的：不在 Windows 用户配置中保存任何内容，所有文件都在 exe 旁边。

```
OshiMakiKeyStudio.exe
drivers/      电路板描述（*.json）：USB ID、写入格式、布局、图标颜色
themes/       界面主题（皮肤）
  light/, dark/      标准：13 个浅色和 13 个深色（*.json）
  custom/            自定义：*.json、*.ts、*.zip 包和包文件夹（附带 minecraft.zip）
language/     语言包（*.json）：ru、be、uk、kk、zh、ja、en、en-GB、de
user_data/    用户数据——更新时程序不会覆盖
  settings.json      通用设置：主题、语言、驱动、自动启动…
  presets/*.json     预设——按键和旋钮的布局，每个一个文件
  working.json       当前预设的工作副本（未保存的修改）
  monitor.log        按键监视器和检查向导的日志（最近 500 个事件）
  diagnostics-*.log  写入检查报告（保留 14 天，最多 10 个文件）
```

文件夹可以整体移动——到另一个磁盘、U 盘或另一台电脑。文件夹之外只会写入 `HKCU\...\Run` 中的自动启动项和 Windows 服务，而且只在你启用时。旧版本的主题文件夹 `theme` 仍会被读取。

**预设。** 当前预设在窗口顶部选择。修改会立即进入工作副本，通过 **“保存”** 按钮（软盘图标，Ctrl+S）写入预设文件：如果已存在同名文件，程序会建议改名或覆盖。“⋯”菜单中有“另存预设为…”、“刷新列表”、“打开预设文件夹”；其余功能在 **文件** 菜单中。

**主题**（**设置 → 外观**）。主题和语言**在选择时立即生效**，“取消”会恢复之前的设置。标准主题使用统一的浅色或深色背景和 13 种强调色（红、珊瑚、橙、琥珀、黄、青柠、绿、青绿、天蓝、蓝、紫罗兰、紫、粉）；在设置中它们是可折叠的“标准”分组里的强调色圆点。下方是“自定义”分组，显示 `themes/custom` 中主题的缩略图。

主题是 `{"id", "name", "order", "dark", "colors": {…}}`；文件中缺少的颜色由主要颜色（`window`、`surface`、`text`、`accent`）推导。完整颜色列表见任意 `themes/light/*.json`。可选字段：`corners`（圆角，0 为直角）、`background`（内置动画场景，目前为 `minecraft`）、`backgroundImage`（包内的背景图片）、`animationFps`（`animate` 的帧率）。

自定义主题格式：
- `*.json` —— 与标准主题相同；
- `*.ts` —— TypeScript 语法的同一对象（`export default { id: 'my', colors: { accent: '#FF8800' } };`）：键可以不加引号，字符串可用单引号，允许注释和末尾逗号。文件作为数据读取；
- `*.zip` 或文件夹 —— 主题包：`theme.ts`（或 `theme.json`）以及 `img`（图片：背景、场景纹理）、`animate`（动画背景帧，按名称排序）和 `scripts`（主题脚本）文件夹。

**主题脚本**（`scripts/*.js`）在内置的 JavaScript **沙箱**（Jint）中运行：无法访问文件、网络、进程和 .NET，每次调用都有时间、内存和递归深度限制。卡住或崩溃的脚本会被停用并给出通知，程序继续工作。脚本可使用：

```js
theme.onFrame(function (g, t, w, h) { … });     // 每帧在主题背景上绘制（t —— 秒）
theme.addWindow({ title, menu, width, height,     // 自己的窗口 —— “帮助”菜单中的一项
                  onFrame(g, t, w, h), onClick(x, y) });
theme.hour(); theme.minute(); theme.log('文本');
// 画布 g：rect(x, y, w, h, '#RRGGBB')、circle(x, y, r, 颜色)、line(x1, y1, x2, y2, 颜色, 粗细)、
//         text(字符串, x, y, 颜色, 大小)、image('img 中的名称', x, y, w, h)、width、height
```

如果主题有自己的背景，它会显示在主窗口卡片之间的间隙中（此类主题的间隙更宽）、设置页面和“关于”页面上。附带 **“Minecraft 风格”** 主题（`themes/custom/minecraft.zip`）：直角、石头与草地配色、**跟随电脑时钟**的天空（白天有太阳和飞鸟，日落时橙色地平线，夜晚有星星、月亮和萤火虫）、云朵和方块地形，并在“帮助”菜单中提供来自主题脚本的“工作台”窗口。主题包由 `tools/make-minecraft-theme.py` 生成。

**语言**（**设置 → 语言**）：`language/<代码>.json`，格式为 `{"code", "name", "strings": {键: 文本}}`。俄语内置在程序中；`ru.json` 由 `tools/lang-keys.ps1` 从代码生成，该脚本还会显示其他语言缺少哪些键。翻译中缺少的键显示俄语文本。

**驱动**（**设置 → 设备** 或 **设备 → 驱动**）：根据 USB ID 和多媒体接口类型自动选择；如果你的电路板与图示不同，请手动选择合适的布局。每个驱动都有根据文件中的布局和颜色绘制的图标。

**更新与文件**（**帮助 → 检查更新和文件** 或 **设置 → 文件与帮助**）：将版本与 GitHub 上的最新发布比较，并检查驱动、主题和语言文件是否齐全且可读取。程序本身不下载任何内容——有新版本时可以打开下载页面。

**如果缺少文件。** 如果删除了所有主题、语言或驱动，程序会使用内置设置运行，并在顶部下方显示建议检查更新的通知。

## 电路板与写入格式

| 格式 | 电路板 | 帧 |
|---|---|---|
| 直接 | `1189:8890`，带位图多媒体接口的 CH552（3 或 6 键 + 旋钮） | `[按钮] [修饰] 00 [码1…码6]`，结尾 `AA AA` |
| k8890（ch57x-keyboard-tool） | `1189:8890`，多媒体为 16 位代码 | `03 FE …`、`03 [按钮] [层/类型] …`、`03 AA AA` |
| 官方程序，“版本 0” | `1189:8890`，手动 | `[按钮] 01 [数量] [序号] [修饰] [代码]`，每个按钮后 `AA AA` |
| k884x | `1189:8840`、`8842`、`8850`、`8830`–`8833`、`8810`、`514C:8851` —— 3 到 15 键，最多 3 个旋钮 | `03 FE [按钮] [层] [类型] …`，然后 `03 AA AA`、`03 FD FE FF`、`03 AA AA` |
| k8850（未在硬件上测试） | `514C:8850`，4×4 网格 | `03 FD [按钮] [层] [类型] …`，然后 `03 FD FE FF` |

**直接格式是如何确定的。** 逐步诊断表明：固件不解析字段。帧的第一个字节是按钮编号，接下来的 8 个字节被当作现成的键盘 HID 报告保存。因此带 `03` 前缀的帧落到了按钮 3 上，多媒体变成了修饰键——这就是 [#168](https://github.com/kriomant/ch57x-keyboard-tool/issues/168) 中的“修饰键队列”。

直接格式电路板本身每个按钮只保存一个组合键；多媒体、鼠标、序列和系统动作由程序执行（⚙）。CH552 6 键 + 旋钮的按钮编号：按键 1–6 为 1–6，旋钮：左 11、按下 12、右 13。在其他直接格式电路板上，**设备 → 检查写入…** 会向按钮 1–30 写入测试字母（a–z，然后是数字），请你按下每个按键和旋钮，并记住找到的编号。

## 需要 Windows 驱动吗？

不需要。该设备是标准的 USB HID 设备（键盘、鼠标、多媒体和用于编程的服务接口）。程序通过标准 `hid.dll` 将设置写入服务接口，无需管理员权限，也不需要 UsbDk/Zadig。程序所说的“驱动”是 `drivers` 文件夹中的电路板描述。

```
设备上的按键 ──► 设备发送服务键（F13…F24、Shift/Ctrl + F13…）
                                   │
                                   ▼
OshiMakiKey Studio 或服务助手 ── RegisterHotKey ──► 多媒体 / 鼠标 / 启动 / 文本 / …
```

## 项目结构

```
OshiMakiKeyStudio.exe           成品程序（便携）
README*.md                      9 种语言的说明（俄语为主）
drivers/ themes/ language/      程序资源（见“程序文件夹”）
tools/lang-keys.ps1             从代码生成 language/ru.json 并检查翻译
tools/make-minecraft-theme.py   生成 themes/custom/minecraft.zip（theme.ts、img、animate、scripts）
docs/drafts/                    尚未实现的想法草稿
.vscode/                        工作区（OshiMakiKey-Studio.code-workspace）、启动、任务和 VS Code 扩展
.vs/OshiMakiKey-Studio.sln      Visual Studio 解决方案（.vs 的其余部分是缓存，不进入 git）
.editorconfig                   Visual Studio 和 VS Code 共用的代码风格
MacroPadStudio/                 源代码；项目 —— OshiMakiKeyStudio.csproj
  Device/   *Protocol.cs        写入格式：直接、k8890、v0、k884x、k8850
            ProfileCompiler.cs  预设 → 帧
            MacroPadDevice.cs   服务接口、写入帧、选择驱动
  Model/    Profile.cs          按钮、动作、层、服务键、代理执行的内容
            Drivers.cs          电路板描述：drivers/*.json 和内置描述
            UpdateCheck.cs      检查 GitHub 发布和程序文件
            SettingsStore.cs, PresetStore.cs, L.cs, Notices.cs, LogFiles.cs, Catalogs.cs, BindingText.cs
  Agent/    HotkeyAgent.cs, ActionRunner.cs, RawInput.cs, Autostart.cs
            OmkService.cs       用户会话中的服务助手（--service），托盘图标
            WinService.cs       Windows 服务（--winservice）：启动助手，通过 UAC 安装
  UI/       MainForm —— 唯一的窗口；页面 SettingsPage、SetupPage、WriteCheckPage、AboutPage；
            Overlay + ThemedDialog / InputDialog —— 窗口上的对话框；Transition —— 平滑过渡；
            ThemeStore / ThemePicker / ThemeBackdrop / ThemeScripts —— 主题、主题背景和沙箱脚本；
            MonitorForm、TourOverlay、DevicePanel、BindingEditor、DriverIcon、Theme 等
  SelfTest.cs                   无界面的自检
```

## 构建

```bash
dotnet build MacroPadStudio
```

在 VS Code 中打开 `.vscode/OshiMakiKey-Studio.code-workspace`（VS Code 会建议安装推荐扩展：C# Dev Kit、C#、PowerShell、EditorConfig）：其中有现成的启动配置（普通、托盘、服务、窗口截图、自检）和任务（构建、发布、翻译检查、拖放测试、截图）。

在 Visual Studio 中打开 `.vs/OshiMakiKey-Studio.sln`（需要“.NET 桌面开发”工作负载）；启动配置在 `MacroPadStudio/Properties/launchSettings.json` 中。

修改代码中的文本后，重新生成俄语包并检查翻译：

```bash
powershell -ExecutionPolicy Bypass -File tools/lang-keys.ps1
```

便携 exe（发布到临时文件夹，然后把 exe 放到项目根目录）：

```bash
dotnet publish MacroPadStudio -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o publish
```

`OshiMakiKeyStudio.exe` 的命令行参数：

- `--selftest 报告.txt` —— 写入格式（含参考帧）、布局、服务键、预设、主题/语言/驱动文件、缺少文件时的运行、主题脚本沙箱；
- `--probe 报告.txt` —— 查找设备并选择驱动（不修改设置）；
- `--drop-test 报告.txt` —— 将按键 5 的动作拖到旋钮的每一侧和按键 1（图示、列表、Ctrl 复制），PASS/FAIL；
- `--snapshot 窗口.png [--select Key1] [--kind System] [--theme dark-blue] [--lang en] [--driver id] [--tour N] [--page settings|about|setup] [--switch-theme id] [--switch-lang 代码] [--preview-theme id] [--sky-hour 0..23]` —— 主窗口截图（导览步骤、页面；`--switch-*` 更改已打开窗口的主题或语言，`--preview-theme` 在打开的设置中选择主题，`--sky-hour` 设置动画主题天空的小时）；
- `--form-snapshot settings0…6 | about | setup-0…2 | overwrite 文件.png` —— 单个页面或对话框的截图；
- `--monitor-snapshot 文件.png` —— 带示例行的按键监视器截图（无需按键）；
- `--service` —— 服务助手；`--winservice` —— Windows 服务（由服务管理器启动）；`--install-winservice` / `--remove-winservice` —— 安装和删除服务（需管理员权限）；`--tray` —— 最小化到托盘启动。

## 限制

- 无法从设备读回设置：固件只支持写入。
- 只能通过 USB 数据线写入。
- 只有 k8890、k884x 和 k8850 格式支持层。
- 直接格式电路板本身不支持多媒体、鼠标和按键序列——需要程序或后台服务。
- k884x 和 k8850 格式是根据 ch57x-keyboard-tool 源代码还原的，尚未用本程序在真实电路板上测试。
- 程序只能调节笔记本内置屏幕的亮度（这是 Windows 的限制）。

## 致谢

协议根据开源项目 [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool) 和 [rOzzy1987/MacroPad](https://github.com/rOzzy1987/MacroPad) 还原。主题脚本由 [Jint](https://github.com/sebastienros/jint) 执行。
