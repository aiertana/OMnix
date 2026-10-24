using CommunityToolkit.WinUI.UI.Controls;
using OMniX.Core.Helpers;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Views.Dialogs;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModRowVM = OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OMniX.WinUI.Views.CharacterDetailsPages;

public sealed partial class ModGrid : UserControl
{
    public DataGrid DataGrid { get; }

    public ModGrid()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
        DataGrid = ModListGrid;
    }


    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ModGridVM), typeof(ModGrid), new PropertyMetadata(default(ModGridVM)));

    public ModGridVM ViewModel
    {
        get { return (ModGridVM)GetValue(ViewModelProperty); }
        set
        {
            SetValue(ViewModelProperty, value);
            OnViewModelSetHandler(ViewModel);
        }
    }

    /// <summary>
    ///     右键某一行时把它设为选中，否则导出的是上一次选中的那个 Mod。
    /// </summary>
    private void ModListGrid_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ModRowVM row })
            ModListGrid.SelectedItem = row;
    }

    /// <summary>
    ///     二开：把选中的 Mod 丢给导出面板，默认导出目录是程序目录下的 OMniX Exports。
    /// </summary>
    public async Task ExportSelectedModsAsync()
    {
        var rows = ViewModel?.SelectedMods?.ToList() ?? new List<ModRowVM>();

        if (rows.Count == 0)
            return;

        var requests = rows.Select(row => new ModExportRequest
        {
            // Mods\{角色}\{Mod 文件夹}，父目录名就是角色目录
            CharacterName = Path.GetFileName(Path.GetDirectoryName(row.AbsFolderPath) ?? string.Empty),
            ModName = row.FolderName,
            FolderPath = row.AbsFolderPath
        }).ToList();

        await App.GetService<IWindowManagerService>().ShowDialogAsync(new ModExportDialog(requests));
    }

    /// <summary>打开选中 Mod 的文件夹。</summary>
    public void OpenSelectedModFolder()
    {
        var row = ViewModel?.SelectedMods?.FirstOrDefault();

        if (row is null)
            return;

        ModExportJob.RevealInExplorer(row.AbsFolderPath);
    }

    private void ModListGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectionChanged_EventHandler(
            e.AddedItems.OfType<ModRowVM>().ToArray(),
            e.RemovedItems.OfType<ModRowVM>().ToArray());
    }


    private void OnViewModelSetHandler(ModGridVM viewModel)
    {
        viewModel.SelectModEvent += ViewModelOnSelectModEvent;
        viewModel.SortEvent += SetSortUiEventHandler;
        viewModel.OnInitialized += ViewModel_OnInitialized;
    }

    private void ViewModel_OnInitialized(object? sender, EventArgs e)
    {
        var mods = new List<ModRowVM>(ViewModel.GridMods);
        if (mods.All(m => m.Description.IsNullOrEmpty()))
            NotesColumn.Width = DataGridLength.SizeToHeader;
    }

    private void ViewModelOnSelectModEvent(object? sender, ModGridVM.SelectModRowEventArgs e)
    {
        DataGrid.SelectedIndex = e.Index;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.SelectModEvent -= ViewModelOnSelectModEvent;
            ViewModel.SortEvent -= SetSortUiEventHandler;
            ViewModel.OnInitialized -= ViewModel_OnInitialized;
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void SetSortUiEventHandler(object? sender, SortEvent sortEvent)
    {
        var column = DataGrid.Columns.FirstOrDefault(c => c.Tag.ToString() == sortEvent.SortColumn);
        if (column is null)
        {
            return;
        }

        column.SortDirection =
            sortEvent.IsDescending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;

        // Reset other columns
        // Remove sorting indicators from other columns
        foreach (var dgColumn in ModListGrid.Columns)
            if (dgColumn.Tag.ToString() != sortEvent.SortColumn)
                dgColumn.SortDirection = null;
    }

    private void OnColumnSort(object? sender, DataGridColumnEventArgs e)
    {
        var sortColumn = e.Column.Tag.ToString() ?? "";
        var isDescending = e.Column.SortDirection == null || e.Column.SortDirection == DataGridSortDirection.Ascending;
        e.Column.SortDirection = isDescending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;


        ViewModel.SetModSorting(sortColumn, isDescending, true);


        // Reset other columns
        // Remove sorting indicators from other columns
        foreach (var dgColumn in ModListGrid.Columns)
            if (dgColumn.Tag.ToString() != sortColumn)
                dgColumn.SortDirection = null;
    }

    private async void ModListGrid_OnKeyDown(object sender, KeyRoutedEventArgs e) => await ViewModel.OnKeyDown_EventHandlerAsync(e.Key).ConfigureAwait(false);

    private void NotificationButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ModRowVM_ModNotificationVM modNotification) return;
        if (modNotification.AttentionType != AttentionType.UpdateAvailable) return;


        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void NotificationButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    private void ModListGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        var mod = (ModRowVM)e.Row.DataContext;


        if (e.Column.Tag.ToString() == nameof(ModRowVM.Author))
        {
            if (!IsValueUpdated(mod.Author, out var newValue)) return;

            var updateRequest = new UpdateSettingsRequest()
            {
                SetAuthor = newValue
            };

            _ = UpdateModSettingsAsync(updateRequest);
        }
        else if (e.Column.Tag.ToString() == nameof(ModRowVM.DisplayName))
        {
            if (!IsValueUpdated(mod.DisplayName, out var newValue)) return;

            var updateRequest = new UpdateSettingsRequest()
            {
                SetCustomName = newValue
            };

            _ = UpdateModSettingsAsync(updateRequest);
        }
        else if (e.Column.Tag.ToString() == nameof(ModRowVM.Description))
        {
            if (!IsValueUpdated(mod.Description, out var newValue)) return;

            var updateRequest = new UpdateSettingsRequest()
            {
                SetDescription = newValue
            };

            _ = UpdateModSettingsAsync(updateRequest);
        }
        else
        {
            // Unsupported edit
            //Debugger.Break();
        }

        return;

        bool IsValueUpdated(string oldValue, out string newValue)
        {
            var textBox = (TextBox)e.EditingElement;
            newValue = textBox.Text.Trim();

            return newValue != oldValue;
        }

        async Task UpdateModSettingsAsync(UpdateSettingsRequest updateRequest)
        {
            var arg = new ModGridVM.UpdateModSettingsArgument(mod, updateRequest);

            if (mod.UpdateModSettingsCommand.CanExecute(arg) == false)
                return;

            await mod.UpdateModSettingsCommand.ExecuteAsync(arg).ConfigureAwait(false);
        }
    }

    private void ModListGrid_OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        if (e.Row.DataContext is not ModRowVM mod)
            return;

        mod.TriggerPropertyChanged(string.Empty);
    }

    private void Notification_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ModRowVM_ModNotificationVM modNotification) return;
        if (modNotification.AttentionType != AttentionType.UpdateAvailable) return;

        if (ViewModel.OpenNewModsWindowCommand.CanExecute(modNotification))
            ViewModel.OpenNewModsWindowCommand.ExecuteAsync(modNotification);
    }
}