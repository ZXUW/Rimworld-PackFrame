using System.Net;
using System.Net.Http;
using PackFrame;

internal static class RetryChecks
{
    public static async Task Run()
    {
        var calls = 0;
        var value = await HttpRetry.RunAsync(
            async () =>
            {
                calls++;
                if (calls < 3)
                    throw new HttpRequestException(
                        "transient",
                        null,
                        HttpStatusCode.ServiceUnavailable);
                await Task.CompletedTask;
                return 42;
            },
            CancellationToken.None,
            delay: _ => TimeSpan.Zero);
        Require(value == 42 && calls == 3, "transient server errors are retried until success");

        calls = 0;
        var rejected = false;
        try
        {
            await HttpRetry.RunAsync<int>(
                () =>
                {
                    calls++;
                    throw new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
                },
                CancellationToken.None,
                delay: _ => TimeSpan.Zero);
        }
        catch (HttpRequestException)
        {
            rejected = true;
        }

        Require(rejected && calls == 1, "permanent client errors are not retried");

        Require(
            HttpRetry.IsTransient(new TimeoutException()) &&
            HttpRetry.IsTransient(new IOException()) &&
            HttpRetry.IsTransient(new HttpRequestException("offline")) &&
            !HttpRetry.IsTransient(new InvalidDataException("bad data")),
            "only transport failures count as transient");
    }

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
