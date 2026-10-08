namespace KerioTrafficMonitor.Domain.Models;

public sealed class KerioUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Username { get; init; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
}
