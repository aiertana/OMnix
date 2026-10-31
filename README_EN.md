# OMniX

A multi-game Mod manager (Wuthering Waves / Zenless Zone Zero / Genshin Impact / Honkai: Star Rail).
Windows desktop app built with WinUI 3 / .NET 9.

中文说明：[README.md](README.md)　·　完整使用说明书（中文）：[使用说明书.md](使用说明书.md)　·　Disclaimer: [DISCLAIMER.md](DISCLAIMER.md)　·　License: [GPL-3.0](LICENSE)

Version: **1.4.1**

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
| **Game Launch** | The launch method is detected per game: WuWa via its official launcher, ZZZ via XXMI, Genshin / Star Rail via the HoYo launcher; if detection fails you can point to any exe manually |
| **Migration** | Move or copy mods between characters |
| **Feature Packs** | Each game's management tools ship as a separate resource pack: switch games by clicking an avatar, download online or import an offline zip. Third parties just ship a `plugin.json` and get detected automatically |
| **Multi-game Switching** | Switch game kernels from the home page (WuWa / ZZZ / Genshin / Star Rail); kernels that aren't downloaded yet are greyed out |

Characters, presets and enable states are stored locally, so you can copy them to another machine.

> The shell (home page, settings, notifications, about) is game-agnostic; each game plus its
> management tools lives in a downloadable "kernel". Wuthering Waves is built in,
> the other three are one download away in the feature pack area.

---

## First run

1. Unzip and double click `启动OMniX.exe` (no console window). The first launch takes a bit longer
   because the self-contained build unpacks the runtime; no separate .NET install needed.
2. The app lands straight on the home page. Wuthering Waves is built in and works out of the box;
   for ZZZ / Genshin / Star Rail, switch the game from the top-right corner of the home page and
   download its kernel (offline zip import is available in the feature pack area too).
3. Click "配置游戏" (Configure game) on the home page to set up paths: point WuWa at its official
   launcher or game folder, point ZZZ at the XXMI Launcher folder (e.g. `D:\mod\XXMI Launcher`).
   The launcher executable and the Mods folder are detected automatically.
4. No XXMI Launcher yet? Click the launcher button and the app will point you to
   <https://github.com/SpectrumQT/XXMI-Launcher/releases/latest>, or let you pick a path manually.

> Mod files are not stored inside OMniX: for WuWa they live in XXMI Launcher's `WWMI\Mods`,
> other games use their own Mods folders. This tool only references them and ships no mods.

The full illustrated walkthrough — installation, UI tour, every feature, settings and FAQ — lives in
**[使用说明书.md](使用说明书.md)** *(Chinese only)*.

---

## Where the data lives

Since 1.4, data lives next to the program and is separated per game:

```
OMniX1.4.1.x\
├─ Core\            the app itself (OMniX.exe lives here)
├─ GamesData\       game data (relocatable from Settings)
│  ├─ WuWa\         Wuthering Waves: Presets\ Plugins\ Cache\ Logs\
│  ├─ ZZZ\          Zenless Zone Zero
│  ├─ GI\           Genshin Impact
│  └─ HSR\          Honkai: Star Rail
├─ 启动OMniX.exe
└─ 手动更新.exe
```

Data from older versions used to sit in `C:\Users\<user>\AppData\Local\OMniX\`.
When upgrading, the app offers to migrate it in one click; you can also migrate manually from
Settings. Whether to migrate — and whether to delete the old copy afterwards — is up to you.

---

## Source code and building

This repository holds the **source code**. If you just want the app, grab `OMniX1.4.1.zip`
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
Kuro Game, the Wuthering Waves team, miHoYo / HoYoverse or any of their games** in any way.

- Using mods may violate the game's Terms of Service and **may result in an account ban**.
  You use this tool entirely at your own risk
- This repository contains no game assets, mod files or cracked content — it is a local file manager
- Do not use it for any commercial purpose

See [DISCLAIMER.md](DISCLAIMER.md) for the full text.

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE).
