using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;

namespace OMniX.WinUI.Services;

public sealed class GlobalSearchService
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;

    public GlobalSearchService(IGameService gameService, ISkinManagerService skinManagerService)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
    }
}