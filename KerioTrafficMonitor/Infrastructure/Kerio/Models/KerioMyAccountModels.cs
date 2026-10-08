namespace KerioTrafficMonitor.Infrastructure.Kerio.Models;

internal sealed class KerioMyAccount
{
    public KerioQuota? Quota { get; set; }

    public KerioUserInfo? UserInfo { get; set; }
}

internal sealed class KerioQuota
{
    public KerioQuotaPeriod? Day { get; set; }

    public KerioQuotaPeriod? Week { get; set; }

    public KerioQuotaPeriod? Month { get; set; }
}

internal sealed class KerioQuotaPeriod
{
    public bool Enabled { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Value { get; set; } = "0";

    public string Down { get; set; } = "0";

    public string Up { get; set; } = "0";

    public int Since { get; set; }
}

internal sealed class KerioUserInfo
{
    public int Id { get; set; }

    public string Uuid { get; set; } = string.Empty;

    public string Fullname { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhotoUrl { get; set; } = string.Empty;

    public string FirewallName { get; set; } = string.Empty;
}