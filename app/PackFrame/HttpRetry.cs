using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

internal static class HttpRetry
{
    internal const int DefaultAttempts = 4;

    internal static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException or IOException) return true;
        if (exception is not HttpRequestException http) return false;
        if (http.StatusCode is null) return true;
        var code = (int)http.StatusCode.Value;
        return code is 408 or 429 or >= 500;
    }

    internal static async Task<T> RunAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        int attempts = DefaultAttempts,
        Func<int, TimeSpan>? delay = null)
    {
        delay ??= attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception exception) when (
                attempt < attempts &&
                !cancellationToken.IsCancellationRequested &&
                IsTransient(exception))
            {
                await Task.Delay(delay(attempt), cancellationToken);
            }
        }
    }
}
