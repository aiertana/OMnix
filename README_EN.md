# OMniX

A Mod manager for Wuthering Waves (WuWa). Windows desktop app built with WinUI 3 / .NET 9.

中文说明：[README.md](README.md)　·　完整使用说明书（中文）：[使用说明书.md](使用说明书.md)　·　Disclaimer: [DISCLAIMER.md](DISCLAIMER.md)　·　License: [GPL-3.0](LICENSE)

Version: **1.4.0**

![UI Preview](Images/screenshot-home.png)

---

## ⚠️ Read this first: this software is free

OMniX is **completely free and open source**. There is no paid tier, no license key,
and no feature behind a paywall — not now, not later.

**If anyone asks you to pay for a copy of this app, they are scamming you.**
Refuse, and please report it in the group chat or on GitHub so the next person doesn't get caught.

There are only two legitimate places to get it:

1. [Releases](https://github.com/aiertana/OMnix/releases) of this repository
2. The group file section of the community chat

Anything downloaded elsewhere should not be used.

<img src="src/OMniX.WinUI/Assets/About/group-qr.jpg" width="200" alt="Community QR code" />

*(The group chat is Chinese-language.)*

---

## Features

| Feature | Description |
|---|---|
| **Character Management** | Every character shown as an avatar wall; search, pin, hide. You can also create custom characters (internal name, keywords, rarity, element, avatar) |
| **Mod Toggle** | Enable / disable a single mod or a whole character in one click — implemented by adding a `DISABLED_` prefix to the folder |
| **Mod Presets** | Save a set of enabled mods as a preset, apply it in bulk; presets can be renamed, duplicated and exported |
| **Mod Editing** | Edit name, author, description, star rating; adjust mod internals and priority; import / replace preview images |
| **Recycle Bin** | Deleted mods go to a recycle bin and can be restored |
| **Update Detection** | Compares local mod file timestamps and flags the ones that changed |
| **Mod Fixer** | Built-in Wuwa Mod Fixer, downloadable and runnable from inside the app |
| **Game Launch** | Launch XXMI Launcher and the game directly; if the launcher isn't installed you get a download hint or can point to it manually |
| **Migration** | Move or copy mods between characters |

Characters, presets and enable states are stored locally, so you can copy them to another machine.

---

## First run

1. Unzip and double click `启动OMniX.exe` (no console window). The first launch takes a bit longer
   because the self-contained build unpacks the runtime; no separate .NET install needed.
2. The setup wizard asks you to pick a game — choose Wuthering Waves.
3. It then asks for the **XXMI Launcher folder**, e.g. `D:\mod\XXMI Launcher`.
   The launcher executable and the Mods folder are detected automatically.
4. No XXMI Launcher yet? Click the launcher button and the app will point you to
   <https://github.com/SpectrumQT/XXMI-Launcher/releases/latest>, or let you pick a path manually.

> Mod files live in XXMI Launcher's `WWMI\Mods`. This tool only references them and ships no mods.

The full illustrated walkthrough — installation, UI tour, every feature, settings and FAQ — lives in
**[使用说明书.md](使用说明书.md)** *(Chinese only)*.

---

## Where the data lives

```
C:\Users\<user>\AppData\Local\OMniX\
├─ ApplicationData_WuWa\
│  ├─ GameService.json      character definitions
│  ├─ LocalSettings.json    settings (including the Mods folder path)
│  ├─ commands.json         launcher / game launch commands
│  └─ Presets\              presets
└─ game.json                currently selected game
```

---

## Source code and building

This repository holds the **source code**. If you just want the app, grab `OMniX1.4.0.zip`
from [Releases](https://github.com/aiertana/OMnix/releases) and unzip it.

### Prerequisites

- Windows 10 2004 or newer (build 10.0.19041)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **.NET desktop development**
  and **Windows application development** workloads
- The **Desktop development with C++** workload as well: `Elevator` is a C++ project,
  the solution won't build without it

### Build and run

Open `src/OMniX.sln`, set `OMniX.WinUI` as the startup project, pick the **x64** platform, hit F5.

Command line works too:

```bat
dotnet build src\OMniX.WinUI\OMniX.WinUI.csproj -c Debug -p:Platform=x64
```

### Produce a release build (self-contained, no .NET needed on the target machine)

```bat
dotnet publish src\OMniX.WinUI\OMniX.WinUI.csproj -c Release -p:Platform=x64 /p:PublishProfile=FolderProfileSelfContained
```

Output lands in `src\OMniX.WinUI\bin\Release\Publish\`.

### What's in this repo

```
OMniX/
├─ src/                   source code
│  ├─ OMniX.WinUI/           main app (WinUI 3 / .NET 9), all UI lives here
│  ├─ OMniX.Core/            mod handling, GameBanana client, game services
│  ├─ OMniX.AutoUpdater/     auto updater
│  ├─ OMniXLauncher/         launcher source
│  ├─ Elevator/              elevation helper (C++)
│  └─ GenshinGenerator/  Tools/  UpdateGenshinAssets/    asset generation helpers
├─ Images/                UI preview screenshot
├─ README.md              this page
├─ README_EN.md           English version
├─ DISCLAIMER.md          disclaimer
├─ LICENSE                GPL-3.0
├─ 使用说明.txt           quick start (Chinese)
└─ 使用说明书.md          full walkthrough (Chinese)
```

> **Note:** anything outside `src/` — build output, packaging assets, local screenshots —
> stays local. It isn't tracked and isn't in the mirror.

---

## Support the author

The app is and will stay free. If you find it useful and feel like buying me a bubble tea,
the QR codes are entirely optional — nothing is locked behind them.

<p>
  <img src="src/OMniX.WinUI/Assets/About/wechat-pay.png" width="170" alt="WeChat Pay" />
  &nbsp;&nbsp;&nbsp;
  <img src="src/OMniX.WinUI/Assets/About/alipay-pay.png" width="170" alt="Alipay" />
</p>

---

## ⚠️ Disclaimer

This is an unofficial, fan-made tool. It is **not affiliated with, endorsed by, or authorized by
Kuro Game or the Wuthering Waves team** in any way.

- Using mods may violate the game's Terms of Service and **may result in an account ban**.
  You use this tool entirely at your own risk
- This repository contains no game assets, mod files or cracked content — it is a local file manager
- Do not use it for any commercial purpose

See [DISCLAIMER.md](DISCLAIMER.md) for the full text.

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE).
