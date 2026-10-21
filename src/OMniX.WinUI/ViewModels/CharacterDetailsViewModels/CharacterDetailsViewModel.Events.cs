namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    public event EventHandler? OnInitializingStarted;

    public event EventHandler? OnModObjectLoaded;

    public event EventHandler? OnModsLoaded;

    public event EventHandler? OnInitializingFinished;


    public event EventHandler<ModListChangedArgs>? OnModListChanged;

    public class ModListChangedArgs : EventArgs
    {
    }
}