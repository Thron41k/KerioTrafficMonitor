using System.Text.Json;

namespace KerioTrafficMonitor.Infrastructure.Kerio.Models;

internal sealed class KerioBatchParams
{
    public IReadOnlyList<KerioBatchCommand> CommandList { get; init; }
        = [];
}

internal sealed class KerioBatchCommand
{
    public required string Method { get; init; }
}

internal sealed class KerioBatchResponse
{
    public KerioBatchItem[] Result { get; set; } = [];
}

internal sealed class KerioBatchItem
{
    public JsonElement Result { get; set; }
}