using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.Services;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services;
using Serilog;
using Windows.Storage.Pickers;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>
///     二开：导出 / 备份面板。
///     两处入口共用：
///     · 设置页「导出 / 备份模组」—— 全量 / 仅启用 / 按角色；
///     · Mod 列表右键「导出为压缩包」—— 直接带上选中的那几个 Mod。
/// </summary>
public sealed partial class ModExportDialog : ContentDialog
{
    private const string ExportDirSettingKey = "ModExportDirectory";

    private readonly List<CharacterSkinEntry> _allEntries;
    private readonly ILocalSettingsService _localSettingsService = App.GetService<ILocalSettingsService>();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<ModExportDialog>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private readonly IReadOnlyList<ModExportRequest>? _presetRequests;
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();

    private ModExportJob? _job;
    private bool _running;

    public ModExportDialog(IReadOnlyList<ModExportRequest>? presetRequests = null)
    {
        _presetRequests = presetRequests;
        _allEntries = _skinManagerService.GetAllMods(GetOptions.All).ToList();

        InitializeComponent();

        BuildScopes();
        BuildFormats();
        LoadCharacters();

        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
        CloseButtonClick += OnCloseButtonClick;
        Loaded += OnLoaded;
    }

    public ObservableCollection<CharacterExportRow> CharacterRows { get; } = new();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        var saved = await _localSettingsService.ReadSettingAsync<string>(ExportDirSettingKey);
        TargetBox.Text = string.IsNullOrWhiteSpace(saved) ? ModExportJob.DefaultExportDirectory : saved;

        UpdateCharacterCount();
    }

    private void BuildScopes()
    {
        var scopes = _presetRequests is not null
            ? new List<string>
            {
                string.Format(
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_PresetScope", "已选的 {0} 个 Mod"),
                    _presetRequests.Count)
            }
            : new List<string>
            {
                _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_ScopeAll", "全部 Mod"),
                _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_ScopeEnabled", "只导出启用中的"),
                _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_ScopeByChar", "按角色选择")
            };

        ScopeRadio.ItemsSource = scopes;
        ScopeRadio.SelectedIndex = 0;
    }

    private void BuildFormats()
    {
        FormatRadio.Items.Add(_localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_FormatZip", "打包成一个 zip"));
        FormatRadio.Items.Add(_localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_FormatFolder", "复制成文件夹（角色 / Mod 结构）"));
        FormatRadio.SelectedIndex = 0;
    }

    private void LoadCharacters()
    {
        foreach (var group in _allEntries
                     .GroupBy(entry => entry.ModList.Character.DisplayName)
                     .OrderBy(group => group.Key, StringComparer.CurrentCulture))
        {
            var row = new CharacterExportRow
            {
                CharacterName = group.Key,
                ModCount = group.Count()
            };

            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CharacterExportRow.IsSelected))
                    UpdateCharacterCount();
            };

            CharacterRows.Add(row);
        }

        UpdateCharacterCount();
    }

    private void UpdateCharacterCount()
    {
        CharacterCountText.Text = string.Format(
            _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_CharCount", "已选 {0} / {1} 个角色"),
            CharacterRows.Count(row => row.IsSelected), CharacterRows.Count);
    }

    private void ScopeRadio_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharacterPanel is null)
            return;

        CharacterPanel.Visibility = _presetRequests is null && ScopeRadio.SelectedIndex == 2
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SelectAllCharacters_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in CharacterRows)
            row.IsSelected = true;

        UpdateCharacterCount();
    }

    private void SelectNoneCharacters_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in CharacterRows)
            row.IsSelected = false;

        UpdateCharacterCount();
    }

    private async void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
                return;

            TargetBox.Text = folder.Path;
            await _localSettingsService.SaveSettingAsync(ExportDirSettingKey, folder.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Pick export folder failed");
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 自己控制关闭时机：导完了才允许关
        args.Cancel = true;

        if (_running)
            return;

        var requests = CollectRequests();

        if (requests.Count == 0)
        {
            ShowProgress(_localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_NoRequests", "没有可导出的 Mod —— 换个范围试试，或者先勾几个角色。"));
            return;
        }

        var target = TargetBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            target = ModExportJob.DefaultExportDirectory;

        var format = FormatRadio.SelectedIndex == 1 ? ModExportFormat.Folder : ModExportFormat.Zip;

        var name = _presetRequests is not null && requests.Count == 1
            ? requests[0].ModName
            : ModExportJob.DefaultName();

        await _localSettingsService.SaveSettingAsync(ExportDirSettingKey, target!);

        await RunExportAsync(requests, format, target!, name);
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        if (_job is null)
            return;

        if (_job.IsPaused)
        {
            _job.Resume();
            SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_BtnPause", "暂停");
            ProgressText.Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_ResumeInProgress", "继续导出…");
        }
        else
        {
            _job.Pause();
            SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_BtnResume", "继续");
            ProgressText.Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_Paused", "已暂停，点「继续」接着导。");
        }
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 关掉时把后台任务停住，别让它在后台继续写文件
        _job?.Cancel();
    }

    private async Task RunExportAsync(
        IReadOnlyList<ModExportRequest> requests,
        ModExportFormat format,
        string target,
        string name)
    {
        _running = true;
        _job = new ModExportJob();

        SetRunningState(true);
        ProgressPanel.Visibility = Visibility.Visible;
        ExportBar.Value = 0;
        ProgressText.Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_Stats", "正在统计文件…");

        var progress = new Progress<ModExportProgressInfo>(info =>
        {
            ExportBar.Value = info.Percent;
            ProgressText.Text = info.FilesTotal > 0
                ? string.Format(
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_ProgressFmt", "{0:F0}% · {1} / {2} 个文件 · {3}"),
                    info.Percent, info.FilesDone, info.FilesTotal, info.CurrentText)
                : info.CurrentText;
        });

        try
        {
            var result = await _job.RunAsync(requests, format, target, name,
                RemoveSettingsCheckBox.IsChecked == true, progress);

            ExportBar.Value = result.Cancelled ? ExportBar.Value : 100;

            if (result.Cancelled)
            {
                ProgressText.Text = result.SummaryText;
                return;
            }

            ProgressText.Text = $"{result.SummaryText} → {result.OutputPath}";
            ModExportJob.RevealInExplorer(result.OutputPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Export mods failed");
            ProgressText.Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_FailedPrefix", "导出失败：") + ex.Message;
        }
        finally
        {
            _running = false;
            _job = null;
            SetRunningState(false);
        }
    }

    private void SetRunningState(bool running)
    {
        IsPrimaryButtonEnabled = !running;
        PrimaryButtonText = running
            ? _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_BtnRunning", "导出中…")
            : _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_PrimaryBtn", "开始导出");

        SecondaryButtonText = running
            ? _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_BtnPause", "暂停")
            : string.Empty;
        IsSecondaryButtonEnabled = running;

        CloseButtonText = running
            ? _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_BtnCancelClose", "取消并关闭")
            : _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_CloseBtn", "关闭");
    }

    private void ShowProgress(string message)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        ExportBar.Value = 0;
        ProgressText.Text = message;
    }

    private List<ModExportRequest> CollectRequests()
    {
        if (_presetRequests is not null)
            return _presetRequests.ToList();

        var selectedCharacters = CharacterRows
            .Where(row => row.IsSelected)
            .Select(row => row.CharacterName)
            .ToHashSet(StringComparer.CurrentCulture);

        IEnumerable<CharacterSkinEntry> source = ScopeRadio.SelectedIndex switch
        {
            0 => _allEntries,
            1 => _allEntries.Where(entry => entry.IsEnabled),
            2 => _allEntries.Where(entry => selectedCharacters.Contains(entry.ModList.Character.DisplayName)),
            _ => Enumerable.Empty<CharacterSkinEntry>()
        };

        return source.Select(entry => new ModExportRequest
        {
            CharacterName = entry.ModList.Character.DisplayName,
            ModName = entry.Mod.Name,
            FolderPath = entry.Mod.FullPath
        }).ToList();
    }
}

public partial class CharacterExportRow : ObservableObject
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public string CharacterName { get; set; } = string.Empty;
    public int ModCount { get; set; }

    [ObservableProperty] private bool _isSelected = true;

    public string Label => string.Format(
        _localizer.GetLocalizedStringOrDefault("/Dialogs/ModExport_CharRowLabel", "{0}（{1} 个 Mod）"),
        CharacterName, ModCount);
}
