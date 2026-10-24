# 打包出来：D:\mod\OMniX_v1.0_release（文件夹）和 D:\mod\OMniX_v1.0_release.zip
# 跟 D:\mod\OMniX_1.0_release 一个路子：外层是版本目录，里面 OMniX\ 装工程文件
# 想换个版本号打包：powershell -File mkpack.ps1 -Version 1.1
param([string]$Version = '1.0')

$ErrorActionPreference = "Continue"

$root = 'D:\mod\OMniX'                  # 工程目录
$release = 'D:\mod'                     # 版本目录和压缩包放的位置
$name = 'OMniX_v' + $Version + '_release'
$src = Join-Path $root 'src\OMniX.WinUI'
$dst = Join-Path $release $name         # D:\mod\OMniX_v1.0

if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Path $dst -Force | Out-Null

$missing = @()

function Copy-To($relativeSrc, $relativeDst)
{
    $from = Join-Path $src $relativeSrc

    # 工程文件统一放进 OMniX\ 子目录，外层留给说明文件，
    # 跟 D:\mod\OMniX_1.0_release 那个目录一个路子
    $to = Join-Path $dst (Join-Path 'OMniX' $relativeDst)

    if (-not (Test-Path $from)) {
        $script:missing += $relativeSrc
        return
    }

    $dir = Split-Path $to
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    # 目录必须带 -Recurse，不然只建出空文件夹、里面的文件一个都不会过来
    Copy-Item $from $to -Force -Recurse
}

# ---- 1. 根目录文件 ----
foreach ($f in @('OMniX.WinUI.csproj', 'App.xaml', 'App.xaml.cs', 'MainWindow.xaml', 'MainWindow.xaml.cs')) {
    Copy-To $f $f
}

# ---- 2. Styles 整个 ----
Copy-To 'Styles' 'Styles'

# ---- 3. Helpers/Xaml 整个 ----
Copy-To 'Helpers\Xaml' 'Helpers\Xaml'

# ---- 4. Services 整个 ----
Copy-To 'Services' 'Services'

# ---- 5. Views：HomePage + Settings 整个 ----
Copy-To 'Views\HomePage.xaml' 'Views\HomePage.xaml'
Copy-To 'Views\HomePage.xaml.cs' 'Views\HomePage.xaml.cs'
Copy-To 'Views\Settings' 'Views\Settings'

# ---- 我认为还需要一起给的 ----
# ShellPage 是所有动效的挂载入口，改 UI 免不了要看它
Copy-To 'Views\ShellPage.xaml' 'Views\ShellPage.xaml'
Copy-To 'Views\ShellPage.xaml.cs' 'Views\ShellPage.xaml.cs'
# 自定义控件（卡片、按钮模板等）
Copy-To 'Views\Controls' 'Views\Controls'
# 动效 / 配色 / 音效三个开关的绑定都在这个 ViewModel 里
Copy-To 'ViewModels\SettingsViewModel.cs' 'ViewModels\SettingsViewModel.cs'
# 资源：光标两张图、点击音、樱花花瓣
Copy-To 'Assets\Cursors' 'Assets\Cursors'
Copy-To 'Assets\Sounds' 'Assets\Sounds'
Copy-To 'Assets\sakura-petal.png' 'Assets\sakura-petal.png'

Write-Output "=== 缺的文件 ==="
if ($missing.Count -eq 0) { Write-Output "（无）" } else { $missing | ForEach-Object { Write-Output $_ } }

Write-Output ""
Write-Output "=== 打包内容 ==="
Get-ChildItem -Path $dst -Recurse -File | Sort-Object FullName | ForEach-Object {
    Write-Output $_.FullName.Substring($dst.Length + 1)
}

Write-Output ""
Write-Output "=== Assets 现有结构（只列三级） ==="
Get-ChildItem -Path (Join-Path $src 'Assets') -Recurse -Depth 2 | ForEach-Object {
    $rel = $_.FullName.Substring($src.Length + 1)
    if ($_.PSIsContainer) { Write-Output ("[目录] " + $rel) } else { Write-Output ("       " + $rel) }
}

# ---- 改动说明 ----
Copy-Item 'D:\mod\OMniX\改动说明.md' (Join-Path $dst 'OMniX\改动说明.md') -Force

# ---- 外层两份说明 ----
$readme = @'
# OMniX_v1.0 —— UI 补丁包

鸣潮（WuWa）Mod 管理器 OMniX 的界面改动包，基于 WinUI 3 / .NET 9。

## 包里有什么

```
OMniX_v1.0_release/
├─ README.md          本文件
├─ 使用说明.txt        安装步骤
└─ OMniX/             要覆盖进工程的 UI 文件
   ├─ App.xaml / App.xaml.cs
   ├─ MainWindow.xaml / MainWindow.xaml.cs
   ├─ OMniX.WinUI.csproj
   ├─ Styles/          主题、字体、圆角
   ├─ Helpers/Xaml/    视觉辅助（樱花、玻璃、卡片光带、鼠标轨迹、主按钮珠光…）
   ├─ Services/        外观、调色板、声音、插件等
   ├─ ViewModels/
   ├─ Views/
   └─ 改动说明.md       每一轮改了什么、为什么改
```

## 怎么装

1. 先备份你自己的工程
2. 把 `OMniX\` 里的东西按同样目录结构覆盖到 `src\OMniX.WinUI\`
3. 重新编译运行

详细说明见「使用说明.txt」。
'@

Set-Content -Path (Join-Path $dst 'README.md') -Value $readme -Encoding UTF8

$guide = @'
OMniX  v1.0  UI 补丁包
======================

【这是什么】
  一份界面补丁，只动 UI（样式、视觉、界面相关的服务），
  不动 Mod 管理的核心逻辑。覆盖完重新编译即可。

【怎么装】
  1. 先备份你的 src\OMniX.WinUI 目录
  2. 解压本包，把 OMniX\ 里的所有内容，
     按原有目录结构覆盖到 src\OMniX.WinUI\
  3. 用 Visual Studio 打开 OMniX.WinUI.csproj，重新生成并运行

  注意：包里没有编译好的 exe，需要你自己编译。

【这次改了什么】
  · 界面整体：玻璃感底、卡片圆角、顶部光带
  · 鼠标：跟随轨迹的流光
  · 主按钮：表面一层珠光（像手指蘸眼影粉抹过去）
  · 樱花：背景飘落的樱花花瓣
  · Mod 总览：批量开关、冲突检查、启用预设
  · 插件专区：DLSS5 渲染链、Mod 加载开关
  · 设置：热重载、主题、Mod 文件夹路径

  每一轮改动的细节、参数，以及来回调整的过程，
  全写在 OMniX\改动说明.md 里。

【数据在哪】
  C:\Users\<你的用户名>\AppData\Local\OMniX\ApplicationData_WuWa
'@

Set-Content -Path (Join-Path $dst '使用说明.txt') -Value $guide -Encoding UTF8

# ---- 打包 ----
$zip = Join-Path $release ($name + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dst '*') -DestinationPath $zip -Force

Write-Output ""
Write-Output ("已打包: " + $zip + "  " + (Get-Item $zip).Length + " 字节")
