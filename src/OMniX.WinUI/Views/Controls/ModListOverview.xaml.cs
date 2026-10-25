using Microsoft.UI.Xaml.Controls;
using OMniX.Core.Contracts.Services;


namespace OMniX.WinUI.Views.Controls;

public sealed partial class ModListOverview : UserControl
{
    public ModListOverview()
    {
        this.InitializeComponent();
        _nameColumn.Header = App.GetService<ILanguageLocalizer>()
            .GetLocalizedStringOrDefault("/ModList/Ctl_ModListNameHeader", "Mod 名称");
    }
}