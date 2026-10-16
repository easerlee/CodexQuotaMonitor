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
