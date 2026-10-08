using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed partial class KerioTrafficParser
{
    [GeneratedRegex(@"linear-gradient\s*\([^)]*?(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex QuotaRegex();

    public async Task<TrafficInfo> ParseAsync(string html, CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

        var panel = document.QuerySelector("#displayfield-1032")
            ?? throw new InvalidOperationException("Блок статистики за сегодня не найден.");

        var date = ParseDate(panel);
        var spans = panel.QuerySelectorAll(".transferData .data span.unitTooltip");

        if (spans.Length < 2)
            throw new InvalidOperationException("Данные входящего/исходящего трафика не найдены.");

        var received = ParseTraffic(spans[0].GetAttribute("data-qtip"));
        var sent = ParseTraffic(spans[1].GetAttribute("data-qtip"));

        var quotaBar = panel.QuerySelector(".quotaBar")
            ?? throw new InvalidOperationException("Индикатор квоты не найден.");

        var style = quotaBar.GetAttribute("style") ?? string.Empty;
        var match = QuotaRegex().Match(style);

        if (!match.Success)
            throw new InvalidOperationException("Процент использованной квоты не найден.");

        var quota = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        return new TrafficInfo(date, received, sent, quota);
    }

    private static DateOnly ParseDate(IElement panel)
    {
        var text = panel.QuerySelector(".tranferDate .date")?.TextContent ?? string.Empty;
        var parts = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2 || !DateOnly.TryParseExact(
                parts[1], "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException("Дата статистики не распознана.");
        }

        return date;
    }

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
