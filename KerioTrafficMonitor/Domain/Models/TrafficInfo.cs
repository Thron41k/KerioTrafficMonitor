namespace KerioTrafficMonitor.Domain.Models;

public sealed record TrafficInfo(
    DateOnly Date,
    long ReceivedBytes,
    long SentBytes,
    double QuotaUsedPercent)
{
    public long TotalBytes => ReceivedBytes + SentBytes;
    public double QuotaRemainingPercent => Math.Max(0, 100 - QuotaUsedPercent);
}
