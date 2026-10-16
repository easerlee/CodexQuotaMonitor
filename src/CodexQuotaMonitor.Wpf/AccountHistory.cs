using System.Text.Json;

namespace CodexQuotaMonitor.Wpf;

// The last reading seen for one account.
public sealed record AccountRecord(
    string AccountId,
    string? Email,
    string? PlanType,
    DateTimeOffset LastSeenAt,
    double? FiveHourPercent,
    long? FiveHourResetsAt,
    double? WeeklyPercent,
    long? WeeklyResetsAt);

// One snapshot per account, overwritten in place. Because the key is the account, a record
// keeps the last values read while that account was logged in, with no logout detection.
public sealed class AccountHistoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public AccountHistoryStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<AccountRecord> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<AccountRecord>>(File.ReadAllText(_path), Options) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Upsert(AccountRecord record)
    {
        try
        {
            var records = Load()
                .Where(existing => !string.Equals(existing.AccountId, record.AccountId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            records.Add(record);

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records, Options));
            File.Move(temp, _path, true);
        }
        catch (Exception)
        {
            // History is a convenience; never let it break a quota refresh.
        }
    }
}

// One menu block per account: the name, then 5H and weekly underneath. The columns are
// padded in display columns so the wide "已重置" keeps them lined up.
public static class AccountRow
{
    private const int LabelColumns = 6;
    private const int ValueColumns = 7;
    private const int ResetColumns = 7;

    public static IReadOnlyList<string> Lines(AccountRecord record, DateTimeOffset now, bool current)
    {
        var name = AccountIdentity.ShortName(record.Email, record.AccountId);
        return
        [
            current ? $"{name}  ← 当前" : name,
            $"{Pad("5H", LabelColumns)}{Pad(Value(record.FiveHourPercent, record.FiveHourResetsAt, now), ValueColumns)}{Pad(Reset(record.FiveHourPercent, record.FiveHourResetsAt, now, weekly: false), ResetColumns)}",
            $"{Pad("WK", LabelColumns)}{Pad(Value(record.WeeklyPercent, record.WeeklyResetsAt, now), ValueColumns)}{Pad(Reset(record.WeeklyPercent, record.WeeklyResetsAt, now, weekly: true), ResetColumns)}"
        ];
    }

    // A full window reads as 已重置 rather than a bare 100%: the server only starts counting
    // the 5h window from first use, so both the percentage and its reset time mean nothing yet.
    private static string Value(double? percent, long? resetsAt, DateTimeOffset now)
    {
        return Formatting.WindowIsFresh(percent, resetsAt, now)
            ? "已重置"
            : percent.HasValue ? $"{Math.Round(percent.Value):0}%" : "--";
    }

    private static string Reset(double? percent, long? resetsAt, DateTimeOffset now, bool weekly)
    {
        return Formatting.WindowIsFresh(percent, resetsAt, now)
            ? string.Empty
            : Formatting.WindowReset(percent, resetsAt, now, weekly);
    }

    private static string Pad(string text, int columns)
    {
        var pad = columns - Columns(text);
        return pad > 0 ? text + new string(' ', pad) : text;
    }

    // CJK glyphs occupy two columns in a monospace row.
    private static int Columns(string text)
    {
        var columns = 0;
        foreach (var character in text)
        {
            columns += character > 0x2E80 ? 2 : 1;
        }

        return columns;
    }
}
