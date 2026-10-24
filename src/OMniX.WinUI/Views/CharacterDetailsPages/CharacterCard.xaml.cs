using OMniX.WinUI.ViewModels.CharacterDetailsViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OMniX.WinUI.Views.CharacterDetailsPages;

public sealed partial class CharacterCard : UserControl
{
    public Grid ItemHero;
    public StackPanel SelectSkinBox;
    public Button SelectSkinButton;
    public TextBlock NoMoreSkinsText;

    public CharacterCard()
    {
        InitializeComponent();
        ItemHero = itemHero;
        SelectSkinBox = selectSkinBox;
        SelectSkinButton = SelectSkinButtonElement;
        NoMoreSkinsText = NoMoreSkinsTextBlock;
    }


    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(CharacterDetailsViewModel), typeof(CharacterCard),
        new PropertyMetadata(default(CharacterDetailsViewModel)));

    public CharacterDetailsViewModel ViewModel
    {
        get { return (CharacterDetailsViewModel)GetValue(ViewModelProperty); }
        set { SetValue(ViewModelProperty, value); }
    }
}