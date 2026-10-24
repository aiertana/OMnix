using System.Diagnostics;
using OMniX.Core.GamesService.Interfaces;

namespace OMniX.Core.GamesService.Models;

[DebuggerDisplay("{" + nameof(DisplayName) + "}")]
internal class Region : IRegion
{
    public Region(string internalName, string displayName)
    {
        InternalName = new InternalName(internalName);
        DisplayName = displayName;
    }

    public InternalName InternalName { get; init; }
    public string DisplayName { get; set; }

    public bool Equals(INameable? other) => InternalName.DefaultEquatable(this, other);
}