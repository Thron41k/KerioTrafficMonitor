using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed class KerioClientFactory(IOptionsMonitor<KerioOptions> options) : IKerioClientFactory
{
    public IKerioClient Create()
    {
        var baseUrl = options.CurrentValue.BaseUrl;

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Некорректный адрес Kerio: '{baseUrl}'.");
        }

        return new KerioClient(
            uri,
            new KerioTrafficParser());
    }
}