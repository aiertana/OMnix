namespace OMniX.Core.GamesService.Interfaces;

public interface ICustomMod : IModdableObject
{
    public IRarity? Rarity { get; }
    public ICollection<string> Keys { get; }
}