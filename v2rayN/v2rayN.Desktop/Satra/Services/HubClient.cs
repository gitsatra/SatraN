using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace v2rayN.Desktop.Satra.Services;

public class HubException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Client for Satra Hub (satra/v1): register, profile, devices, limits.</summary>
public class HubClient(string baseUrl, string? token = null)
{
    public string Base { get; } = baseUrl.TrimEnd('/');
    public string? Token { get; set; } = token;
    public string? Pin { get; set; }

    private async Task<JsonNode?> Call(HttpMethod m, string path, object? body = null)
    {
        using var res = await Net.SendAsync(() =>
        {
            var req = new HttpRequestMessage(m, Base + path);
            Identity.AddTo(req);
            if (Token != null) req.Headers.TryAddWithoutValidation("Authorization", "Satra " + Token);
            if (Pin != null) req.Headers.TryAddWithoutValidation("X-Satra-Pin", Pin);
            if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return req;
        }) ?? throw new HubException("network", "Satra Hub unreachable");
        var txt = await res.Content.ReadAsStringAsync();
        var node = string.IsNullOrWhiteSpace(txt) ? null : JsonNode.Parse(txt);
        if (!res.IsSuccessStatusCode)
        {
            throw new HubException(node?["error"]?["code"]?.ToString() ?? res.StatusCode.ToString(), node?["error"]?["message"]?.ToString() ?? res.ReasonPhrase ?? "error");
        }
        return node;
    }

    public static string? SubIdFromUrl(string url)
    {
        try { return new Uri(url).Segments.LastOrDefault()?.Trim('/'); } catch { return null; }
    }

    public async Task<JsonNode?> Profile(string sub) => await Call(HttpMethod.Get, "/profile?sub=" + Uri.EscapeDataString(sub));

    public async Task<HubReg> Register(string sub, string originalUrl)
    {
        var n = await Call(HttpMethod.Post, "/devices/register", new { sub, hwid = SatraState.I.Hwid, name = SatraState.I.DeviceName, os = Identity.Os, model = Environment.MachineName, app = Identity.App });
        return new HubReg
        {
            HubBase = Base, DeviceId = n?["deviceId"]?.ToString() ?? "", Token = n?["deviceToken"]?.ToString() ?? "",
            DeviceSubUrl = n?["subUrl"]?.ToString() ?? "", OriginalUrl = originalUrl, ParentSubId = sub,
        };
    }

    public async Task<JsonNode?> Me() => await Call(HttpMethod.Get, "/me");
    public async Task<JsonArray> Devices() => (await Call(HttpMethod.Get, "/devices"))?["devices"]?.AsArray() ?? [];
    public async Task<JsonArray> Limits() => (await Call(HttpMethod.Get, "/limits"))?["limits"]?.AsArray() ?? [];
    public async Task PutLimit(object limit) => await Call(HttpMethod.Put, "/limits", limit);
    public async Task DeleteLimit(string id) => await Call(HttpMethod.Delete, "/limits/" + id);
    public async Task Topup(string id, long bytes) => await Call(HttpMethod.Post, $"/limits/{id}/topup", new { bytes });
    public async Task SetPin(string? old, string @new) => await Call(HttpMethod.Post, "/pin", new { old = old ?? "", @new });
    public async Task RenameDevice(string id, string name) => await Call(HttpMethod.Patch, "/devices/" + id, new { name });
    public async Task DeleteDevice(string id) => await Call(HttpMethod.Delete, "/devices/" + id);
}
