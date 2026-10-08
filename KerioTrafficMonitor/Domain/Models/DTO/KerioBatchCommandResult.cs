using System.Text.Json;

namespace KerioTrafficMonitor.Domain.Models.DTO;

internal sealed class KerioBatchCommandResult
{
    public JsonElement Result { get; set; }
}