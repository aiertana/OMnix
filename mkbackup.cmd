@echo off
setlocal

set "SRC=D:\mod\OMniX\src\OMniX.WinUI"
set "DST=D:\mod\OMniX\UI-Backup"

if not exist "%DST%" mkdir "%DST%"
if not exist "%DST%\Views" mkdir "%DST%\Views"
if not exist "%DST%\Services" mkdir "%DST%\Services"
if not exist "%DST%\ViewModels" mkdir "%DST%\ViewModels"

xcopy /S /I /Y "%SRC%\Styles" "%DST%\Styles"
xcopy /S /I /Y "%SRC%\Helpers\Xaml" "%DST%\Helpers\Xaml"

copy /Y "%SRC%\Views\ShellPage.xaml" "%DST%\Views\"
copy /Y "%SRC%\Views\ShellPage.xaml.cs" "%DST%\Views\"
copy /Y "%SRC%\Views\HomePage.xaml" "%DST%\Views\"
copy /Y "%SRC%\Views\HomePage.xaml.cs" "%DST%\Views\"

xcopy /S /I /Y "%SRC%\Views\Settings" "%DST%\Views\Settings"
xcopy /S /I /Y "%SRC%\Views\Controls" "%DST%\Views\Controls"

copy /Y "%SRC%\Services\ThemeStyleService.cs" "%DST%\Services\"
copy /Y "%SRC%\Services\PaletteService.cs" "%DST%\Services\"
copy /Y "%SRC%\Services\UiSoundService.cs" "%DST%\Services\"

copy /Y "%SRC%\ViewModels\SettingsViewModel.cs" "%DST%\ViewModels\"

copy /Y "%SRC%\App.xaml" "%DST%\"

echo.
echo --- 备份内容 ---
dir /b /s "%DST%"
endlocal
