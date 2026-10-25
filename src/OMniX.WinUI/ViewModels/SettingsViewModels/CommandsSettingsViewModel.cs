using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Services.CommandService;
using OMniX.Core.Services.CommandService.Models;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Views.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.ViewModels.SettingsViewModels;

public sealed partial class CommandsSettingsViewModel(
    CommandService commandService,
    IWindowManagerService windowManagerService,
    NotificationManager notificationManager,
    ILocalSettingsService localSettingsService,
    CommandHandlerService commandHandlerService,
    ILanguageLocalizer localizer)
    : ObservableRecipient, INavigationAware
{
    private readonly CommandService _commandService = commandService;
    private readonly IWindowManagerService _windowManagerService = windowManagerService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly CommandHandlerService _commandHandlerService = commandHandlerService;
    private readonly ILanguageLocalizer _localizer = localizer;

    public ObservableCollection<CommandDefinitionVM> CommandDefinitions { get; } = new();

    public ObservableCollection<CommandVM> RunningCommands { get; } = new();


    [RelayCommand]
    private async Task OpenCreateCommandAsync()
    {
        var key = "ShowCommandWarningDialogKey";

        var showCommandWarningDialog = await _localSettingsService.ReadSettingAsync<bool?>(key);

        if (showCommandWarningDialog is null or true)
        {
            var commandWarningDialog = new ContentDialog
            {
                Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdWarnTitle", "提个醒"),

                Content = new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdWarnContent",
                        "建命令的时候留个心。命令能拿来跑你电脑上的任何程序，" +
                        "只加你信得过的来源。\n" +
                        "OMniX 本身也不完美，挡不住恶意脚本，也挡不住自己的 bug。\n\n" +
                        "点「我知道了」就表示你清楚这些风险。"),
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.WrapWholeWords
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdWarnPrimary", "我知道了"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消")
            };

            var result = await _windowManagerService.ShowDialogAsync(commandWarningDialog);

            if (result == ContentDialogResult.Primary)
            {
                await _localSettingsService.SaveSettingAsync(key, false);
            }
            else
            {
                return;
            }
        }

        var window = App.MainWindow;
        var page = new CreateCommandView();

        await _windowManagerService.ShowFullScreenDialogAsync(page, window.Content.XamlRoot, window);
        await RefreshCommandDefinitionsAsync().ConfigureAwait(false);
    }


    [RelayCommand]
    private async Task KillRunningCommandAsync(CommandVM? command)
    {
        if (command is null || command.IsKilling)
            return;
        command.IsKilling = true;

        var runningCommands = await _commandService.GetRunningCommandsAsync();
        var runningCommand = runningCommands.FirstOrDefault(x => x.RunId == command.RunId);
        if (runningCommand is { IsRunning: true })
        {
            try
            {
                await runningCommand.KillAsync();
            }
            catch (Exception e)
            {
                _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdKillFailedTitle", "结束进程失败"), e.Message, TimeSpan.FromSeconds(5));
                return;
            }
        }
        else
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdAlreadyGone", "这个进程已经不在跑了"), string.Empty, TimeSpan.FromSeconds(2));
            await RefreshRunningCommandsAsync();
            return;
        }

        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdKilled", "进程已结束"), string.Empty, TimeSpan.FromSeconds(2));
    }

    private bool CanEditCommand(CommandDefinitionVM? commandVM)
    {
        return commandVM is { IsDeleting: false, HasTargetPathVariable: false } &&
               RunningCommands.ToArray().All(r => r.Id != commandVM.Id);
    }

    [RelayCommand(CanExecute = nameof(CanEditCommand))]
    private async Task EditAsync(CommandDefinitionVM? commandDefinition)
    {
        if (commandDefinition is null)
            return;

        var window = App.MainWindow;

        var existingCommand = await _commandService.GetCommandDefinitionAsync(commandDefinition.Id);

        if (existingCommand is null)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdReadFailedTitle", "读取命令失败"),
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdReadFailedBody", "找不到这条命令"),
                TimeSpan.FromSeconds(5));
            return;
        }

        var options = CreateCommandOptions.EditCommand(existingCommand);
        var page = new CreateCommandView(options: options);

        await _windowManagerService.ShowFullScreenDialogAsync(page, window.Content.XamlRoot, window);
        await RefreshCommandDefinitionsAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task DeleteCommandAsync(CommandDefinitionVM? commandDefinition)
    {
        if (commandDefinition is null || commandDefinition.IsDeleting)
            return;
        commandDefinition.IsDeleting = true;

        try
        {
            await _commandService.DeleteCommandDefinitionAsync(commandDefinition.Id);
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdDeleteFailedTitle", "删除命令失败"), e.Message, TimeSpan.FromSeconds(5));
            return;
        }
        finally
        {
            await RefreshCommandDefinitionsAsync();
        }

        _notificationManager.ShowNotification(
            string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdDeleted", "命令「{0}」已删除"),
                commandDefinition.CommandDisplayName),
            string.Empty, TimeSpan.FromSeconds(2));
    }

    private bool CanRunCommand(CommandDefinitionVM? commandVM)
    {
        return commandVM is { IsDeleting: false, HasTargetPathVariable: false } &&
               RunningCommands.ToArray().All(r => r.Id != commandVM.Id);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RunAsync(CommandDefinitionVM? commandVM)
    {
        if (commandVM is null || !CanRunCommand(commandVM))
            return;

        var result = await Task.Run(() => _commandHandlerService.RunCommandAsync(commandVM.Id, null));

        if (result.HasNotification)
        {
            _notificationManager.ShowNotification(result.Notification);
        }
    }

    public async void OnNavigatedTo(object parameter)
    {
        await RefreshRunningCommandsAsync();
        _commandService.RunningCommandsChanged += RunningCommandsChangedHandler;
        await RefreshCommandDefinitionsAsync().ConfigureAwait(false);
    }

    public void OnNavigatedFrom()
    {
        _commandService.RunningCommandsChanged -= RunningCommandsChangedHandler;
    }

    private async Task RefreshCommandDefinitionsAsync()
    {
        CommandDefinitions.Clear();
        var commandDefinitions = await _commandService.GetCommandDefinitionsAsync();

        foreach (var commandDefinition in commandDefinitions.Reverse())
        {
            var commandDefinitionVM = new CommandDefinitionVM(commandDefinition, _localizer)
            {
                DeleteCommand = DeleteCommandCommand,
                RunCommand = RunCommand,
                EditCommand = EditCommand
            };
            CommandDefinitions.Add(commandDefinitionVM);
        }
    }

    private async Task RefreshRunningCommandsAsync()
    {
        RunningCommands.Clear();
        var runningCommands = await _commandService.GetRunningCommandsAsync();
        foreach (var runningCommand in runningCommands)
        {
            RunningCommands.Add(new CommandVM(runningCommand)
            {
                KillCommand = KillRunningCommandCommand
            });
        }
    }


    private void RunningCommandsChangedHandler(object? sender,
        RunningCommandChangedEventArgs runningCommandChangedEventArgs)
    {
        switch (runningCommandChangedEventArgs.ChangeType)
        {
            case RunningCommandChangedEventArgs.CommandChangeType.Added:
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    RunningCommands.Insert(0, new CommandVM(runningCommandChangedEventArgs.Command)
                    {
                        KillCommand = KillRunningCommandCommand
                    });
                });

                break;
            case RunningCommandChangedEventArgs.CommandChangeType.Removed:
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    var command =
                        RunningCommands.FirstOrDefault(x => x.RunId == runningCommandChangedEventArgs.Command.RunId);
                    if (command != null)
                    {
                        RunningCommands.Remove(command);
                    }
                });
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        RunCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
    }
}

public partial class CommandVM : ObservableObject
{
    public CommandVM(ProcessCommand processCommand)
    {
        Id = processCommand.CommandDefinitionId;
        RunId = processCommand.RunId;
        CommandDisplayName = processCommand.DisplayName;
        FullCommand = processCommand.FullCommand;
    }

    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public string CommandDisplayName { get; set; }

    public string FullCommand { get; set; }

    public bool IsKilling { get; set; }
    public required IAsyncRelayCommand KillCommand { get; init; }
}

public partial class CommandDefinitionVM : ObservableObject
{
    public CommandDefinitionVM(CommandDefinition commandDefinition, ILanguageLocalizer localizer)
    {
        Id = commandDefinition.Id;
        CommandDisplayName = commandDefinition.CommandDisplayName;
        Executable = commandDefinition.ExecutionOptions.Command;
        Arguments = commandDefinition.ExecutionOptions.Arguments ?? string.Empty;
        WorkingDirectory = commandDefinition.ExecutionOptions.WorkingDirectory ?? App.ROOT_DIR;
        HasTargetPathVariable =
            commandDefinition.ExecutionOptions.HasAnySpecialVariables([SpecialVariables.TargetPath]);


        var attributes = new List<string>();

        const string separator = " | ";

        // 这几个标签会直接印在「已建命令」的卡片上，所以写中文，
        // 原来拿 nameof 拼，界面上会露出 RunAsAdmin、UseShellExecute 这种枚举名
        if (commandDefinition.KillOnMainAppExit)
            attributes.Add(localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdAttrKillOnExit", "退出时结束进程") + separator);

        if (commandDefinition.ExecutionOptions.RunAsAdmin)
            attributes.Add(localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdAttrRunAsAdmin", "管理员运行") + separator);

        if (commandDefinition.ExecutionOptions.UseShellExecute)
            attributes.Add(localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdAttrUseShell", "交给系统启动") + separator);

        if (attributes.Count != 0)
        {
            attributes.Insert(0, localizer.GetLocalizedStringOrDefault("/SettingsUi/CmdAttrPrefix", "选项： "));
            var lastItem = attributes.Last().TrimEnd('|', ' ');
            attributes[^1] = lastItem;
        }

        Attributes = attributes;
    }

    public Guid Id { get; set; }
    public string CommandDisplayName { get; set; }

    public string Executable { get; set; }
    public string Arguments { get; set; }
    public string WorkingDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isDeleting;

    public bool CanDelete => !IsDeleting;
    public required IAsyncRelayCommand DeleteCommand { get; init; }

    public List<string> Attributes { get; }

    public bool HasTargetPathVariable { get; }

    public bool HasNoTargetPathVariable => !HasTargetPathVariable;


    public required IAsyncRelayCommand RunCommand { get; init; }


    public required IAsyncRelayCommand EditCommand { get; init; }
}
