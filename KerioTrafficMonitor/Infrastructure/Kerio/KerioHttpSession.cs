using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

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
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,

            // В локальной сети Kerio прокси не нужен.
            UseProxy = false
        };

        Client = new HttpClient(_handler)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(60)
        };

        Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "KerioTrafficMonitor/1.0");

        Client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("*/*"));

        // Критично для некоторых старых HTTP-серверов.
        Client.DefaultRequestHeaders.ExpectContinue = false;
    }

    public string? GetToken(Uri uri)
    {
        var cookies = _cookies.GetCookies(uri);

        return cookies["TOKEN_CONTROL_WEBIFACE"]?.Value;
    }

    public CookieContainer Cookies => _cookies;

    public void Dispose()
    {
        Client.Dispose();
        _handler.Dispose();
    }
}