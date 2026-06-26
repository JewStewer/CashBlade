using Finora.Web.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finora.Web.Services;

// Talks to the Stripe Issuing backend (server/stripe-issuing) — Evergrove
// itself never holds a Stripe secret key. Stripe is the source of truth for
// card state and activity; this service just relays calls and maps the
// JSON shape back into the app's models.
public class StripeCardService(HttpClient http)
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string? LastError { get; private set; }

    public async Task<string?> CreateCardholderAsync(string baseUrl, string name, string email, string phoneNumber,
        string line1, string city, string state, string postalCode, string country)
    {
        LastError = null;
        try
        {
            var resp = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/api/cardholder", new
            {
                name,
                email,
                phoneNumber,
                address = new { line1, city, state, postalCode, country }
            });
            if (!resp.IsSuccessStatusCode) { LastError = await ReadErrorAsync(resp); return null; }
            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(_opts);
            return result.GetProperty("id").GetString();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<IssuedCard?> CreateCardAsync(string baseUrl, string cardholderId, int limitCents, string currency = "usd")
    {
        LastError = null;
        try
        {
            var resp = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/api/card", new { cardholderId, limitCents, currency });
            if (!resp.IsSuccessStatusCode) { LastError = await ReadErrorAsync(resp); return null; }
            return await resp.Content.ReadFromJsonAsync<IssuedCard>(_opts);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<IssuedCard?> GetCardAsync(string baseUrl, string cardId)
    {
        LastError = null;
        try
        {
            var resp = await http.GetAsync($"{baseUrl.TrimEnd('/')}/api/card?cardId={Uri.EscapeDataString(cardId)}");
            if (!resp.IsSuccessStatusCode) { LastError = await ReadErrorAsync(resp); return null; }
            return await resp.Content.ReadFromJsonAsync<IssuedCard>(_opts);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<int?> TopUpAsync(string baseUrl, string cardId, int amountCents)
    {
        LastError = null;
        try
        {
            var resp = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/api/topup", new { cardId, amountCents });
            if (!resp.IsSuccessStatusCode) { LastError = await ReadErrorAsync(resp); return null; }
            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(_opts);
            return result.GetProperty("limitCents").GetInt32();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<List<CardActivityEntry>> GetActivityAsync(string baseUrl, string cardId, int limit = 20)
    {
        LastError = null;
        try
        {
            var resp = await http.GetAsync($"{baseUrl.TrimEnd('/')}/api/activity?cardId={Uri.EscapeDataString(cardId)}&limit={limit}");
            if (!resp.IsSuccessStatusCode) { LastError = await ReadErrorAsync(resp); return new(); }
            return await resp.Content.ReadFromJsonAsync<List<CardActivityEntry>>(_opts) ?? new();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new();
        }
    }

    public async Task<bool> CancelCardAsync(string baseUrl, string cardId)
    {
        LastError = null;
        try
        {
            var resp = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/api/cancel-card", new { cardId });
            if (!resp.IsSuccessStatusCode) { LastError = await ReadErrorAsync(resp); return false; }
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp)
    {
        try
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("error", out var err)) return err.GetString() ?? resp.ReasonPhrase ?? "Request failed.";
        }
        catch { /* fall through to generic message below */ }
        return resp.ReasonPhrase ?? "Request failed.";
    }
}
