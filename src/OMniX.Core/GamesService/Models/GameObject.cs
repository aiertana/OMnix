using OMniX.Core.GamesService.Interfaces;

namespace OMniX.Core.GamesService.Models;

public class GameObject : BaseModdableObject, IGameObject
{
    protected internal GameObject(IModdableObject moddableObject) : base(moddableObject)
    {
    }
}

public interface IGameObject : IModdableObject
{
}