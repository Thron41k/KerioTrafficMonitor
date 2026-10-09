using System.Globalization;
using System.Text.RegularExpressions;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed partial class KerioTrafficParser
{
    [GeneratedRegex(@"linear-gradient\s*\([^)]*?(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex QuotaRegex();





    private static long ParseTraffic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new FormatException($"Неизвестный формат трафика: {value}");

        var amount = double.Parse(parts[0].Replace(',', '.'), CultureInfo.InvariantCulture);
        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "B" => 1d,
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => throw new FormatException($"Неизвестная единица: {parts[1]}")
        };

        return checked((long)(amount * multiplier));
    }
}
