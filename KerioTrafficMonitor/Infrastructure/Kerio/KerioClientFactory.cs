using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed class KerioClientFactory : IKerioClientFactory
{
    private readonly KerioOptions _options;

    public KerioClientFactory(IOptions<KerioOptions> options)
    {
        _options = options.Value;
    }

    public IKerioClient Create()
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Kerio:BaseUrl имеет некорректный формат.");

        return new KerioClient(uri, new KerioTrafficParser());
    }
}
