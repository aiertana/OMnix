using OMniX.Core.Entities;
using OMniX.Core.Entities.Mods.Contract;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels;

public record ModChangedMessage(object sender, CharacterSkinEntry SkinEntry, ModSettings? Settings);