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

// Panel order: the logged-in account first, then whoever becomes usable soonest. The 5h window
// is always the earlier of the two, so its reset time decides; a window that already reset (or
// never started counting) is usable now and sorts above one still running. Accounts with no
// recorded reset time fall to the bottom.
public static class AccountOrder
{
    public static IReadOnlyList<AccountRecord> Sort(
        IReadOnlyList<AccountRecord> records,
        string? currentAccountId,
        DateTimeOffset now)
    {
        return records
            .OrderByDescending(record => IsCurrent(record, currentAccountId))
            .ThenBy(record => ResetKey(record, now))
            .ThenByDescending(record => record.LastSeenAt)
            .ToList();
    }

    public static bool IsCurrent(AccountRecord record, string? currentAccountId)
    {
        return currentAccountId is not null &&
            string.Equals(record.AccountId, currentAccountId, StringComparison.OrdinalIgnoreCase);
    }

    private static long ResetKey(AccountRecord record, DateTimeOffset now)
    {
        if (record.FiveHourResetsAt is not { } resetsAt)
        {
            return long.MaxValue;
        }

        return Formatting.WindowIsFresh(record.FiveHourPercent, resetsAt, now) ? 0 : resetsAt;
    }
}
