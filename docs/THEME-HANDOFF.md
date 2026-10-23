# OMniX 主题交接说明（给另一个 AI）

## 一、主题用的是什么格式

**XAML 资源字典（ResourceDictionary），不是 HTML，不是 CSS。**

项目是 **WinUI 3（WindowsAppSDK 1.7）+ C# / .NET 9** 的桌面应用。
HTML 只是我照着抄配色和动效参数的**参考稿**，不参与编译，交付时不需要 HTML。

一个主题 = 一个 `OmniXTheme.xaml` 文件里的颜色表（加上少量 C# 里的动效数值）。

## 二、文件位置与分工

```
src/OMniX.WinUI/
├─ Styles/
│  ├─ OmniXTheme.xaml        ← 主题本体（颜色 + 画刷 + 卡片样式），改主题主要动这里
│  ├─ FontSizes.xaml         字号
│  ├─ FontTypes.xaml         字体（GenshinFont 指向 Assets/Fonts/zh-cn2.ttf）
│  ├─ TextBlock.xaml         文本样式
│  └─ Thickness.xaml         间距 / 圆角
├─ App.xaml                  把上面几个字典合并进 Application.Resources
├─ Helpers/Xaml/
│  ├─ GlassFx.cs             悬停抬起、按压回弹（数值在 C# 里）
│  ├─ ClickFx.cs             点击粒子飞散
│  └─ MotionFx.cs            把上面两个铺到全界面
└─ Services/
   ├─ ThemeStyleService.cs   标准版 / 简易版（只切换是否启用动效）
   └─ UiSoundService.cs      交互音效（内存合成波形，无音频文件）
```

## 三、硬性约束（违反会编译失败或运行时崩）

1. **XML 注释里不能出现连续两个短横线 `--`**
   我踩过：写了 `<!-- 参考稿的 --bg-1 -->`，`XamlCompiler.exe` 直接退出码 1，
   **而且不打印任何错误信息**，只能二分定位。要写 CSS 变量名就去掉那两个横线。

2. **颜色是 ARGB 8 位十六进制**，不是 CSS 的 `#rrggbb`
   `rgba(255,158,199,.28)` → `#47FF9EC7`（0.28 × 255 ≈ 71 = 0x47）
   完全不透明要写 `#FF` 开头。

3. **Light 和 Dark 两套的键名必须完全一致**
   深色模式少一个键，就会回落去取浅色值，界面直接花掉。

4. **不要改键名，不要删键**
   全项目几百处 XAML 通过 `{ThemeResource OmniXCardBackgroundBrush}` 引用，
   改名字 = 全项目引用失效。

5. **资源字典里不能有 `x:Bind`、事件、代码**
   只能是 `Color` / `Brush` / `Style` / `CornerRadius` 这类静态资源。

6. **Brush 基本不用改**
   它们长这样：`<SolidColorBrush x:Key="OmniXCardBackgroundBrush" Color="{ThemeResource OmniXCardBackgroundColor}" />`
   改了 Color，Brush 自动跟着变。**只交颜色值就够了。**

## 四、需要交付的颜色键（照着填就行）

### 浅色（Light）

| Key | 当前值 | 说明 |
|---|---|---|
| SystemAccentColor | `#FFFF6B8A` | 系统强调色，WinUI 控件默认取它 |
| SystemAccentColorLight1 | `#FFFF8FA8` | |
| SystemAccentColorLight2 | `#FFFFB0C2` | |
| SystemAccentColorLight3 | `#FFFFD0DA` | |
| SystemAccentColorDark1 | `#FFE04A6B` | |
| SystemAccentColorDark2 | `#FFB83654` | |
| SystemAccentColorDark3 | `#FF8C2741` | |
| AccentFillColorDefaultBrush | `#FFFF6B8A` | 主按钮底色 |
| AccentFillColorSecondaryBrush | `#E6FF6B8A` | 悬停 |
| AccentFillColorTertiaryBrush | `#CCFF6B8A` | 按压 |
| AccentFillColorDisabledBrush | `#33FF6B8A` | 禁用 |
| AccentTextFillColorPrimaryBrush | `#FFFF6B8A` | 强调文字 |
| AccentTextFillColorSecondaryBrush | `#FFFF6B8A` | |
| AccentTextFillColorTertiaryBrush | `#FFFF6B8A` | |
| OmniXAccentColor | `#FFFF6B8A` | 主色（粉） |
| OmniXAccentSoftColor | `#FFFFF1F5` | 主色浅底 |
| OmniXAccentSoftHoverColor | `#FFFFE4EC` | |
| OmniXAccentSoftPressedColor | `#FFFFD8E4` | |
| OmniXAccentStrongColor | `#FFFF8FA8` | 主色亮一档 |
| OmniXAuxPinkColor | `#FFFF9EC7` | 点缀粉 |
| OmniXAuxPurpleColor | `#FFB58BFF` | 点缀紫 |
| OmniXAuxBlueColor | `#FF8CCBFF` | 点缀蓝 |
| OmniXCardAltBackgroundColor | `#9EFFFFFF` | 次要卡片（62% 白） |
| OmniXCardBackgroundColor | `#B8FFFFFF` | 主卡片（72% 白） |
| OmniXCardStrokeColor | `#29150E6E` | 卡片描边（淡紫） |
| OmniXCardStrokeHoverColor | `#52966ED8` | 悬停描边 |
| OmniXControlBackgroundColor | `#B3FFFFFF` | 输入框 / 下拉 |
| OmniXControlBorderColor | `#29150E6E` | |
| OmniXControlBorderPointerOverColor | `#668CCBFF` | |
| OmniXGlassTintColor | `#FFFFFFFF` | 玻璃底色 |
| OmniXHeroBackgroundColor | `#D9FFFFFF` | Hero 卡 |
| OmniXNavGlassTintColor | `#FFFFFFFF` | 侧栏玻璃 |
| OmniXNavItemPointerOverColor | `#B8FFFFFF` | 导航项悬停 |
| OmniXNavItemPressedColor | `#CCFFFFFF` | 导航项按压 |
| OmniXNavItemSelectedColor | `#2EFF6B8A` | 导航项选中（18% 主色） |
| OmniXNavItemSelectedPointerOverColor | `#45FF6B8A` | |
| OmniXNavItemSelectedPressedColor | `#5CFF6B8A` | |
| OmniXNavPaneBackgroundColor | `#99FFFFFF` | 侧栏底 |
| OmniXPageBackgroundColor | `#FFFFFFFF` | 页面底 |
| OmniXPureWhiteColor | `#FFFFFFFF` | |
| OmniXSidebarBackgroundColor | `#9EFFFFFF` | |
| OmniXTextOnHeroColor | `#FFFFFFFF` | Hero 上的文字 |
| OmniXTextPrimaryColor | `#FF4B3D6B` | 主文字（紫灰） |
| OmniXTextSecondaryColor | `#FF8A7DA8` | 副文字 |
| OmniXTextSecondaryOnHeroColor | `#FFFFE3EC` | |
| OmniXWarmColor | `#FFFF8A6B` | 警示 / 暖色 |
| OmniXWarmDeepColor | `#FFD84A2F` | |
| OmniXDialogBackgroundColor | `#EBFFFFFF` | 弹窗底（92% 白） |
| OmniXDialogBorderColor | `#29150E6E` | 弹窗描边 |

另外两个渐变（不填就沿用现在的）：

```
OmniXPageBackgroundBrush    页面底渐变：#FFFFF7FC → #FFFAF9FE → #FFF0F7FF（淡粉→淡蓝）
OmniXCardBeamBrush          卡片顶部流光条：粉 → 紫 → 蓝（两端透明）
```

### 深色（Dark）

键名和上面**完全一致**，当前值：

| Key | 当前值 |
|---|---|
| SystemAccentColor | `#FFFF6B75` |
| SystemAccentColorLight1 | `#FFFF8A92` |
| SystemAccentColorLight2 | `#FFFFA8AE` |
| SystemAccentColorLight3 | `#FFFFC6CA` |
| SystemAccentColorDark1 | `#FFE5484D` |
| SystemAccentColorDark2 | `#FFC9172A` |
| SystemAccentColorDark3 | `#FFA51020` |
| AccentFillColorDefaultBrush | `#FFFF6B75` |
| AccentFillColorSecondaryBrush | `#E6FF6B75` |
| AccentFillColorTertiaryBrush | `#CCFF6B75` |
| AccentFillColorDisabledBrush | `#33FF6B75` |
| AccentTextFillColorPrimaryBrush | `#FFFF8A92` |
| AccentTextFillColorSecondaryBrush | `#FFFF8A92` |
| AccentTextFillColorTertiaryBrush | `#FFFF8A92` |
| OmniXAccentColor | `#FFFF8FA8` |
| OmniXAccentSoftColor | `#FF3A2430` |
| OmniXAccentSoftHoverColor | `#FF4A2F3E` |
| OmniXAccentSoftPressedColor | `#FF553646` |
| OmniXAccentStrongColor | `#FFFFB0C2` |
| OmniXAuxPinkColor | `#FFFF9EC7` |
| OmniXAuxPurpleColor | `#FFB58BFF` |
| OmniXAuxBlueColor | `#FF8CCBFF` |
| OmniXCardAltBackgroundColor | `#12FFFFFF` |
| OmniXCardBackgroundColor | `#1AFFFFFF` |
| OmniXCardStrokeColor | `#2EFFFFFF` |
| OmniXCardStrokeHoverColor | `#66FF8FA8` |
| OmniXControlBackgroundColor | `#1FFFFFFF` |
| OmniXControlBorderColor | `#2AFFFFFF` |
| OmniXControlBorderPointerOverColor | `#668CCBFF` |
| OmniXGlassTintColor | `#FF1F1B26` |
| OmniXHeroBackgroundColor | `#99202026` |
| OmniXNavGlassTintColor | `#FF1B1822` |
| OmniXNavItemPointerOverColor | `#1FFFFFFF` |
| OmniXNavItemPressedColor | `#2BFFFFFF` |
| OmniXNavItemSelectedColor | `#33FF8FA8` |
| OmniXNavItemSelectedPointerOverColor | `#45FF8FA8` |
| OmniXNavItemSelectedPressedColor | `#57FF8FA8` |
| OmniXNavPaneBackgroundColor | `#B3141418` |
| OmniXPageBackgroundColor | `#990E0E11` |
| OmniXPureWhiteColor | `#FFFFFFFF` |
| OmniXSidebarBackgroundColor | `#B3141418` |
| OmniXTextOnHeroColor | `#FFFFFFFF` |
| OmniXTextPrimaryColor | `#FFF3EFFA` |
| OmniXTextSecondaryColor | `#FFB9AED0` |
| OmniXTextSecondaryOnHeroColor | `#FFE6D9E4` |
| OmniXWarmColor | `#FFFF9E80` |
| OmniXWarmDeepColor | `#FFE85A40` |
| OmniXDialogBackgroundColor | `#B31A1A20` |
| OmniXDialogBorderColor | `#59FFFFFF` |

## 五、动效数值（可选交付，不改就沿用）

这些在 C# 里，如果只做配色可以整段跳过：

```
悬停      translateY(-4px) scale(1.015)   260ms 缓出
按压      translateY(-1px) scale(0.98)     90ms
页面切换   opacity 0→1 + scale 0.94→1      380ms
卡片流光条 顶部 2px 渐变条横扫               3.6s 循环
点击粒子   10 颗 6~20px 圆点，粉/紫/蓝/白     0.9s 飞散 + 消失
点击涟漪   160px 径向渐变圆，scale 0→2       0.45s
音效·点击   sine     960Hz → 1420Hz         0.12s，增益 0.18 指数衰减
音效·弹窗   triangle 720Hz → 1080Hz         0.2s
```

## 六、推荐交付格式（二选一）

### 方式 A：只交颜色表（推荐，最安全）

直接把上面两张表填好发给我，例如：

```json
{
  "light": { "OmniXAccentColor": "#FF7C5CFF", "OmniXCardBackgroundColor": "#B8FFFFFF", ... },
  "dark":  { "OmniXAccentColor": "#FF9B84FF", "OmniXCardBackgroundColor": "#1AFFFFFF", ... }
}
```

或 Markdown 表格都行。**我负责写回 XAML 并保证不破坏键名。**

### 方式 B：交完整 XAML

给我一整份 `OmniXTheme.xaml`。要求：

- 包含**全部**上面列出的键（Light + Dark 各一套，键名一致）
- 保留所有 `<SolidColorBrush x:Key="OmniX...Brush" Color="{ThemeResource ...}" />` 的引用结构
- 保留 `OmniXPageBackgroundBrush`、`OmniXCardBeamBrush` 两个渐变
- 保留文件末尾的 Style（`OmniXGlassCardStyle`、`OmniXHeroButtonStyle` 等）不动
- 注释里不要出现 `--`

## 七、拿到之后我会做什么

1. 写回 `OmniXTheme.xaml`（方式 A 由我合并，方式 B 整份替换）
2. 编译确认 0 error（重点盯 XAML 编译器那个不报错的坑）
3. 启动实测：首页 / 角色列表 / Mod 总览 / 设置 / 插件专区
4. 提交并打 tag 留回退点
