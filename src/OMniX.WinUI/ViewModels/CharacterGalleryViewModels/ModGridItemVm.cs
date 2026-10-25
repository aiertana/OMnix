using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.GamesService.Interfaces;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models;
using Microsoft.UI.Xaml;
using OMniX.WinUI.Services;
using WinRT.Interop;
using System.Collections.Generic;
using System.IO;
using Windows.Storage;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.Core.Contracts.Services;

namespace OMniX.WinUI.ViewModels.CharacterGalleryViewModels
{
    public class ModGridItemVm : ObservableObject
    {
        private readonly ModModel _modModel;
        private Uri? _originalImagePath;

        /// <summary>
        ///     二开：UI 线程队列，由 CharacterGalleryViewModel 注入
        /// </summary>
        public DispatcherQueue? UiDispatcher { get; set; }

        /// <summary>
        ///     二开：兜底。属性变更通知一律在 UI 线程发出。
        ///     画廊视图的后台刷新（文件监听 / 重载）会在线程池线程改 IsEnabled、图片等，
        ///     绑定控件在错误线程收到通知会让进程直接崩（0xc000027b）。
        /// </summary>
        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            var queue = UiDispatcher;

            if (queue is not null && !queue.HasThreadAccess)
            {
                var name = e.PropertyName;
                queue.TryEnqueue(() => base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(name)));
                return;
            }

            base.OnPropertyChanged(e);
        }

        public Guid Id => _modModel.Id;
        public IModdableObject Character => _modModel.Character;
        public string Name => _modModel.Name;
        public string Author => _modModel.Author;
        public DateTime DateAdded => _modModel.DateAdded;
        public string DateAddedView => $"Added to OMniX on: {DateAdded}";
        public TimeSpan TimeSinceAdded => DateTime.Now - DateAdded;
        public string TimeSinceFormated => FormaterHelpers.FormatTimeSinceAdded(TimeSinceAdded);
        public Uri ImagePath => _modModel.ImagePath;
        public Uri? ModUrl => string.IsNullOrWhiteSpace(_modModel.ModUrl) ? null : new Uri(_modModel.ModUrl);
        public bool HasModUrl => ModUrl is not null;
        public string NameTooltip => $"Custom Name: {Name}\nFolder Name: {FolderName}";
        public string ButtonText => _modModel.IsEnabled ? "Disable" : "Enable";

        public string FolderName
        {
            get => _modModel.FolderName;
            set
            {
                _modModel.FolderName = value;
                OnPropertyChanged(nameof(NameTooltip));
                OnPropertyChanged();
            }
        }

        public string FolderPath
        {
            get => _modModel.FolderPath;
            set
            {
                _modModel.FolderPath = value;
                OnPropertyChanged(nameof(NameTooltip));
                OnPropertyChanged();
            }
        }

        public Style? ButtonStyle => _modModel.IsEnabled
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : (Style)Application.Current.Resources["DefaultButtonStyle"];

        public bool IsEnabled
        {
            get => _modModel.IsEnabled;
            set
            {
                _modModel.IsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ButtonText));
                OnPropertyChanged(nameof(ButtonStyle));
            }
        }

        public bool CanSaveImage => ImagePath != _originalImagePath;

        public ModGridItemVm(
            ModModel modModel,
            IAsyncRelayCommand toggleModCommand,
            IAsyncRelayCommand openModFolderCommand,
            IAsyncRelayCommand openModUrlCommand,
            IAsyncRelayCommand deleteModCommand
        )
        {
            _modModel = modModel;
            _originalImagePath = modModel.ImagePath;
            ToggleModCommand = toggleModCommand;
            OpenModFolderCommand = openModFolderCommand;
            OpenModUrlCommand = openModUrlCommand;
            DeleteModCommand = deleteModCommand;
        }


        public IAsyncRelayCommand ToggleModCommand { get; }

        public IAsyncRelayCommand OpenModFolderCommand { get; }

        public IAsyncRelayCommand OpenModUrlCommand { get; }

        public IAsyncRelayCommand DeleteModCommand { get; }

        public IAsyncRelayCommand PasteImageFromClipboardCommand => new AsyncRelayCommand(async () =>
        {
            var imageHandlerService = App.GetService<ImageHandlerService>();

            var clipboardHasValidImageResult = await imageHandlerService.ClipboardContainsImageAsync();
            if (!clipboardHasValidImageResult.Result)
            {
                return;
            }

            var imagePath = await imageHandlerService.GetImageFromClipboardAsync(clipboardHasValidImageResult.DataPackage);
            if (imagePath == null)
            {
                return;
            }

            _modModel.ImagePath = imagePath;
            OnPropertyChanged(nameof(ImagePath));
            OnPropertyChanged(nameof(CanSaveImage));
        });

        public IRelayCommand ClearImageCommand => new RelayCommand(() =>
        {
            _modModel.ImagePath = ImageHandlerService.StaticPlaceholderImageUri;
            OnPropertyChanged(nameof(ImagePath));
            OnPropertyChanged(nameof(CanSaveImage));
        });

        public IAsyncRelayCommand SaveImageCommand => new AsyncRelayCommand(async () =>
        {
            var modSettingsService = App.GetService<ModSettingsService>();
            var notificationService = App.GetService<NotificationManager>();

            try
            {
                var updateRequest = new UpdateSettingsRequest
                {
                    SetImagePath = ImagePath
                };

                var result = await modSettingsService.SaveSettingsAsync(Id, updateRequest);

                if (result?.Notification is not null)
                    notificationService.ShowNotification(result.Notification);

                _originalImagePath = ImagePath;
                OnPropertyChanged(nameof(CanSaveImage));
            }
            catch (Exception e)
            {
                notificationService.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_SaveImageFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_SaveImageFailed", "保存图片失败")), e.Message, null);
            }
        },
        () => CanSaveImage);
    }
}