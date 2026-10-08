namespace KerioTrafficMonitor.Application.Options;

public sealed class MonitoringOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public double SwitchThresholdPercent { get; set; } = 95;
    public bool AutomaticSwitching { get; set; } = true;
}
