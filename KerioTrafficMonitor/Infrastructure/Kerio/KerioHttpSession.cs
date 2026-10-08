using System.Net;
using System.Net.Http;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed class KerioHttpSession : IDisposable
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClientHandler _handler;

    public HttpClient Client { get; }

    public KerioHttpSession(Uri baseAddress)
    {
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        Client = new HttpClient(_handler)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15)
        };

        Client.DefaultRequestHeaders.UserAgent.ParseAdd("KerioTrafficMonitor/1.0");
    }

    public string? GetToken(Uri uri)
    {
        var cookie = _cookies.GetCookies(uri)["TOKEN_CONTROL_WEBIFACE"];
        return cookie?.Value;
    }

    public void Dispose()
    {
        Client.Dispose();
        _handler.Dispose();
    }
}
