using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.Core.Services.ModPresetService;
using OMniX.Core.Services.ModPresetService.Models;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services.Notifications;
using Serilog;

namespace OMniX.WinUI.Services.ModHandling;

public sealed class ModPresetHandlerService(
    ILogger logger,
    ModPresetService modPresetService,
    UserPreferencesService preferencesService,
    NotificationManager notificationManager,
    ElevatorService elevatorService,
    ILocalSettingsService localSettingsService)
{
    private readonly ILogger _logger = logger.ForContext<ModPresetHandlerService>();
    private readonly ModPresetService _modPresetService = modPresetService;
    private readonly UserPreferencesService _userPreferencesService = preferencesService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ElevatorService _elevatorService = elevatorService;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;


    public Task<IEnumerable<ModPreset>> GetModPresetsAsync()
        => Task.FromResult(_modPresetService.GetPresets().OrderBy(p => p.Index).AsEnumerable());

    /// <summary>
    ///     二开：把「当前已启用的 Mod 组合」直接存成一个新预设。
    ///     createEmptyPreset: false 会让服务端把现在启用的 Mod 全部写进这个预设。
    /// </summary>
    public async Task CreatePresetFromCurrentAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        try
        {
            // 先把 3DMigoto 当前的偏好读出来，保证预设里的按键设置是最新的
            await _userPreferencesService.SaveModPreferencesAsync().ConfigureAwait(false);

            await _modPresetService.CreatePresetAsync(name, createEmptyPreset: false).ConfigureAwait(false);

            _logger.Information("Saved current mods as preset '{PresetName}'", name);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "Failed to save current mods as preset '{PresetName}'", name);
            _notificationManager.ShowNotification("保存预设失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    public async Task<Result> ApplyModPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalModPresetAsync(presetName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when applying preset {PresetName}", presetName);
            return Result.Error(new SimpleNotification("Failed to apply preset", e.Message, null));
        }
    }


    private async Task<Result> InternalModPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetName, nameof(presetName));
        await _modPresetService.ApplyPresetAsync(presetName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var preferencesResult = await _userPreferencesService
            .SetModPreferencesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!preferencesResult)
        {
            _notificationManager.ShowNotification("Could not write mod preferences to 3Dmigoto user .ini",
                "See logs for details", null);
        }


        var modPreset = _modPresetService.GetPreset(presetName);


        var simpleNotification = new SimpleNotification
        (
            "Mod preset applied",
            $"Mod preset {modPreset.Name} applied successfully",
            TimeSpan.FromSeconds(5)
        );


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
            if (modPreset.Mods.Count == 0)
                return Result.Success(simpleNotification);

            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            //await ElevatorService.RefreshGenshinMods().ConfigureAwait(false); // Wait and check for changes timout 5 seconds
            //await Task.Delay(5000).ConfigureAwait(false);
            await _elevatorService.RefreshAndWaitForUserIniChangesAsync().ConfigureAwait(false);
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
        }


        return Result.Success(simpleNotification);
    }


    private async Task<bool> CanAutoSyncAsync()
    {
        var autoSync = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key)
            .ConfigureAwait(false);

        return _elevatorService.CheckStatus() == ElevatorStatus.Running && autoSync.AutoSyncMods;
    }

    public async Task<Result> SaveActiveModPreferencesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalSaveActivePreferencesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when saving active preferences");
            return Result.Error(new SimpleNotification("Failed to save active preferences", e.Message, null));
        }
    }

    private async Task<Result> InternalSaveActivePreferencesAsync(CancellationToken cancellationToken)
    {
        await _userPreferencesService.SaveModPreferencesAsync().ConfigureAwait(false);

        return Result.Success(new SimpleNotification("Active preferences saved",
            $"Preferences stored in {Constants.UserIniFileName} have been saved for enabled mods",
            TimeSpan.FromSeconds(5)));
    }

    public async Task<Result> ApplyActiveModPreferencesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalApplyActivePreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when applying active preferences");
            return Result.Error(new SimpleNotification("Failed to apply saved preferences", e.Message, null));
        }
    }

    private async Task<Result> InternalApplyActivePreferencesAsync(CancellationToken cancellationToken)
    {
        await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new SimpleNotification("已应用保存的偏好",
            $"偏好已写入 3DMigoto 的 {Constants.UserIniFileName}",
            TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    ///     二开：把一个预设挪到最前面（置顶），并把新顺序写进磁盘。
    /// </summary>
    public async Task<bool> MovePresetToTopAsync(string presetName)
    {
        var order = _modPresetService.GetPresets()
            .OrderBy(preset => preset.Index)
            .Select(preset => preset.Name)
            .ToList();

        if (!order.Remove(presetName))
            return false;

        order.Insert(0, presetName);

        await _modPresetService.SavePresetOrderAsync(order).ConfigureAwait(false);

        _logger.Information("Moved preset '{PresetName}' to top", presetName);
        return true;
    }

    /// <summary>
    ///     二开：取消置顶 —— 把它从最前面挪回原来的位置。
    /// </summary>
    /// <param name="originalIndex">置顶之前它排在第几位。</param>
    public async Task<bool> UnpinPresetAsync(string presetName, int originalIndex)
    {
        try
        {
            var order = _modPresetService.GetPresets()
                .OrderBy(preset => preset.Index)
                .Select(preset => preset.Name)
                .ToList();

            if (!order.Remove(presetName))
                return false;

            var target = Math.Max(0, Math.Min(originalIndex, order.Count));

            order.Insert(target, presetName);

            await _modPresetService.SavePresetOrderAsync(order).ConfigureAwait(false);

            _logger.Information("Unpinned preset '{PresetName}' back to index {Index}", presetName, target);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to unpin preset '{PresetName}'", presetName);
            _notificationManager.ShowNotification("取消置顶失败", e.Message, TimeSpan.FromSeconds(8));
            return false;
        }
    }

    /// <summary>
    ///     二开：删除预设。
    /// </summary>
    public async Task<bool> DeletePresetAsync(string presetName)
    {
        try
        {
            await _modPresetService.DeletePresetAsync(presetName).ConfigureAwait(false);
            _logger.Information("Deleted preset '{PresetName}'", presetName);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete preset '{PresetName}'", presetName);
            _notificationManager.ShowNotification("删除预设失败", e.Message, TimeSpan.FromSeconds(8));
            return false;
        }
    }

    /// <summary>
    ///     二开：给预设改名。
    /// </summary>
    public async Task<bool> RenamePresetAsync(string oldName, string newName)
    {
        try
        {
            await _modPresetService.RenamePresetAsync(oldName, newName).ConfigureAwait(false);
            _logger.Information("Renamed preset '{OldName}' -> '{NewName}'", oldName, newName);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to rename preset '{OldName}'", oldName);
            _notificationManager.ShowNotification("重命名失败", e.Message, TimeSpan.FromSeconds(8));
            return false;
        }
    }

    /// <summary>
    ///     二开：用当前已启用的 Mod 组合覆盖这个预设。
    /// </summary>
    public async Task<bool> OverwritePresetWithCurrentAsync(string presetName)
    {
        try
        {
            await _userPreferencesService.SaveModPreferencesAsync().ConfigureAwait(false);
            await _modPresetService.OverwritePresetWithCurrentAsync(presetName).ConfigureAwait(false);

            _logger.Information("Overwrote preset '{PresetName}' with current mods", presetName);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to overwrite preset '{PresetName}'", presetName);
            _notificationManager.ShowNotification("保存到预设失败", e.Message, TimeSpan.FromSeconds(8));
            return false;
        }
    }
}