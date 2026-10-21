using BenchmarkDotNet.Attributes;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;

namespace OMniX.Benchmark.Benchmarks;

[SimpleJob(iterationCount: 5)]
public class Testing_Benchmark
{
    private IGameService _gameService = null!;
    private ISkinManagerService _skinManagerService = null!;
}