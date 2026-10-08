using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KerioTrafficMonitor.Domain.Models.DTO;

internal sealed class KerioBatchRequest
{
    public string Jsonrpc { get; init; } = "2.0";

    public int Id { get; init; } = 1;

    public string Method { get; init; } = "Batch.run";

    public KerioBatchParams Params { get; init; } = new();
}
internal sealed class KerioBatchParams
{
    public List<KerioBatchCommand> CommandList { get; init; } = [];
}

internal sealed class KerioBatchCommand
{
    public required string Method { get; init; }
}