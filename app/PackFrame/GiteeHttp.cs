using System.Net;
using System.Net.Http;
using System.Security.Authentication;

namespace PackFrame;

internal static class GiteeHttp
{
    public static HttpClient CreateClient()
    {
        var client = new HttpClient(CreateHandler());
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12
            }
        };
    }
}
