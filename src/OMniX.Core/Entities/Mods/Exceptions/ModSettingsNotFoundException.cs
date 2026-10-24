using OMniX.Core.Contracts.Entities;

namespace OMniX.Core.Entities.Mods.Exceptions;

public class ModSettingsNotFoundException : Exception
{
    public ModSettingsNotFoundException(string message) : base(message)
    {
    }

    public ModSettingsNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ModSettingsNotFoundException(ISkinMod mod) : base(
        $"Could not find mod settings for mod '{mod.GetDisplayName()}' (ModPath: {mod.FullPath})")
    {
    }
}