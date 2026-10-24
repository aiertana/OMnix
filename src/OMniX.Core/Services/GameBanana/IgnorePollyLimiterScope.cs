using OMniX.Core.Helpers;

namespace OMniX.Core.Services.GameBanana;

public static class IgnorePollyLimiterScope
{
    private static readonly AsyncLocal<bool> _ignorePollyLimiterScope = new();

    public static bool IsIgnored => _ignorePollyLimiterScope.Value;

    public static IDisposable Ignore()
    {
        _ignorePollyLimiterScope.Value = true;

        return new DisposableAction(() => _ignorePollyLimiterScope.Value = false);
    }
}