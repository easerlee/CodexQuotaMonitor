using System.Text.Json;

namespace CodexQuotaMonitor.Wpf;

// Which account is logged in, read from the Codex CLI's own auth file. Strictly read-only:
// the monitor never writes, refreshes or logs credentials.
public sealed record AccountIdentity(string Id, string? Email, string? PlanType)
{
    public string Display => ShortName(Email, Id);

    // Menu labels stay compact: only the part before the @.
    public static string ShortName(string? email, string id)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var at = email!.IndexOf('@');
            return at > 0 ? email[..at] : email;
        }

        return Shorten(id);
    }

    public static AccountIdentity? Read(string codexHome)
    {
        try
        {
            var path = Path.Combine(codexHome, "auth.json");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            // Missing, locked or half-written auth file simply means "unknown account".
            return null;
        }
    }

    public static AccountIdentity? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tokens", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = Text(tokens, "account_id");
            var claims = Claims(Text(tokens, "id_token"));
            var email = claims.HasValue ? Text(claims.Value, "email") : null;
            string? plan = null;
            if (claims.HasValue &&
                claims.Value.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                auth.ValueKind == JsonValueKind.Object)
            {
                plan = Text(auth, "chatgpt_plan_type");
            }

            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            return new AccountIdentity(string.IsNullOrWhiteSpace(id) ? email! : id!, email, plan);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The id token is a JWT; only the payload is needed and the signature is irrelevant here.
    private static JsonElement? Claims(string? token)
    {
        var parts = token?.Split('.');
        if (parts is null || parts.Length < 2)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        while (payload.Length % 4 != 0)
        {
            payload += "=";
        }

        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.Clone();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string Shorten(string id)
    {
        return id.Length <= 10 ? id : $"{id[..4]}…{id[^4..]}";
    }
}
