# OMniX

鸣潮（WuWa）Mod 管理器，桌面端 Windows 应用。

基于 [JASM](https://github.com/Jorixon/JASM) 二开，按自己的使用习惯改了不少地方：单独的**角色管理**页面、**Mod 预设**、自定义角色与头像、启动器缺失时的下载引导等。

仓库：<https://github.com/aiertana/OMnix>

版本：1.0.0

---

## 它能做什么

- **角色管理**：所有角色以头像墙形式列出，支持搜索、置顶、隐藏；角色可以自己新建（含内部名、关键字、稀有度、属性、头像）
- **Mod 开关**：单个 Mod 或整个角色一键启用/禁用，实质是给 Mod 文件夹加 `DISABLED_` 前缀
- **Mod 预设**：把一套 Mod 组合存成预设，切换时批量应用；预设可保存到文件、可重命名、可复制、可导出
- **Mod 编辑**：改 Mod 的名称、作者、描述、星级，调整 Mod 内部的分段与优先级，导入/替换预览图和 Mod 文件
- **Mod 预设与角色回收**：删掉的 Mod 会进回收站，误删可以恢复
- **Mod 更新**：本地校验 Mod 文件时间戳，标出有变动的 Mod
- **Mod 修复**：集成 Wuwa Mod Fixer，可直接在程序内下载并运行
- **启动器 / 游戏**：配置好路径后可以直接从程序里拉起 XXMI Launcher 和游戏本体；启动器没装会提示下载或手动指定位置
- **迁移**：可以把 Mod 迁移到别的角色，或在角色之间复制

## 目录结构

```
OMniX/
├─ 启动OMniX.exe        无窗口启动器（图标取自 OMniX.png）
├─ README.md
├─ CHANGELOG.md
├─ src/
│  ├─ OMniX.WinUI/      主程序（WinUI 3 / .NET 9）
│  ├─ OMniX.Core/       核心逻辑（Mod 处理、GameBanana 客户端、游戏服务等）
│  ├─ OMniX.AutoUpdater/自动更新器
│  ├─ OMniXLauncher/    启动器源码
│  ├─ Elevator/         提权辅助（需要 VS 的 C++ 桌面开发组件才能发布）
│  └─ ...
├─ Build/Release.py     官方发布脚本（publish + 7z 打包）
└─ .backup/             本地备份（不上传）
```

## 编译与运行

日常直接双击 `启动OMniX.exe` 即可，它会按下面的顺序找主程序并启动：

1. `src\OMniX.WinUI\bin\x64\Debug\net9.0-windows10.0.22621.0\OMniX.exe`
2. `src\OMniX.WinUI\bin\Release\Publish\OMniX.exe`
3. `OMniX\OMniX.exe`

全部是相对路径，整个目录搬到别的盘也能用。

启动器支持参数：

| 命令 | 作用 |
|---|---|
| `启动OMniX.exe` | 直接启动 |
| `启动OMniX.exe build` | 先编译 Debug/x64 再启动 |
| `启动OMniX.exe fresh` | 清掉 Debug 本地配置后启动（会重新进入首次启动页） |

手动编译：

```bat
dotnet build src\OMniX.WinUI\OMniX.WinUI.csproj -c Debug -p:Platform=x64
```

发布自包含版本（不需要目标机器装 .NET 运行时）：

```bat
dotnet publish src\OMniX.WinUI\OMniX.WinUI.csproj -c Release /p:PublishProfile=FolderProfileSelfContained.pubxml
```

## 首次使用

1. 第一次启动会进入引导页，选择游戏（鸣潮）
2. **启动器文件夹**：选 XXMI Launcher 所在的目录（例如 `D:\mod\XXMI Launcher`），程序会自动识别里面的启动程序并推导 Mods 文件夹
3. 没有装 XXMI Launcher 的话，点工具栏的「启动器」按钮，会提示去 <https://github.com/SpectrumQT/XXMI-Launcher/releases/latest> 下载，或者自己手动指定路径

## 数据存在哪

个人设置与角色数据都在：

```
C:\Users\<用户名>\AppData\Local\OMniX\
├─ ApplicationData_WuWa\   鸣潮的数据（角色、预设、自定义头像）
│  ├─ GameService.json     角色定义
│  ├─ LocalSettings.json   设置（含 Mods 文件夹路径）
│  ├─ commands.json        启动器 / 游戏启动命令
│  └─ Presets\             预设
└─ game.json               当前选中的游戏
```

Mod 文件本身**不在**这个目录里，它们在 XXMI Launcher 的 `WWMI\Mods` 下，管理器只是引用。

## 说明

- 本仓库只包含管理器本体，不含 XXMI Launcher、3Dmigoto，也不含任何 Mod 文件
- Elevator 这次没有发布，缺了它只影响「游戏内刷新 Mod」按钮，其它功能正常
- 界面里通往 GameBanana 的入口暂时移除

## 许可

沿用上游 [JASM](https://github.com/Jorixon/JASM) 的许可：GNU General Public License v3.0，详见 [LICENSE](LICENSE)。
