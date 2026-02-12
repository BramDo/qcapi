using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Qcapi.Tray;

internal sealed class QiskitRuntimeClient
{
    private readonly QcapiConfig _cfg;
    private readonly HttpClient _http;
    private readonly IbmIamTokenProvider _tokenProvider;

    public QiskitRuntimeClient(QcapiConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _tokenProvider = new IbmIamTokenProvider(cfg.IbmCloudApiKey, _http);
    }

    public QcapiConfig Config => _cfg;

    public Task<JsonElement> GetVersionsAsync(CancellationToken ct) =>
        SendJsonAsync(HttpMethod.Get, "/versions", needAuth: false, needCrn: false, includeApiVersionHeader: false, jsonBody: null, ct: ct);

    public Task<JsonElement> ListBackendsAsync(CancellationToken ct) =>
        SendJsonAsync(HttpMethod.Get, "/backends", needAuth: true, needCrn: true, includeApiVersionHeader: true, jsonBody: null, ct: ct);

    public Task<JsonElement> GetBackendStatusAsync(string backendName, CancellationToken ct) =>
        SendJsonAsync(HttpMethod.Get, $"/backends/{Uri.EscapeDataString(backendName)}/status", needAuth: true, needCrn: true, includeApiVersionHeader: true, jsonBody: null, ct: ct);

    public Task<JsonElement> GetBackendPropertiesAsync(string backendName, CancellationToken ct) =>
        SendJsonAsync(HttpMethod.Get, $"/backends/{Uri.EscapeDataString(backendName)}/properties", needAuth: true, needCrn: true, includeApiVersionHeader: true, jsonBody: null, ct: ct);

    public Task<JsonElement> ListJobsAsync(
        int? limit,
        int? skip,
        string? backend,
        string? programId,
        string? pending,
        CancellationToken ct)
    {
        var query = new Dictionary<string, string?>();
        if (limit is not null) query["limit"] = limit.Value.ToString(CultureInfo.InvariantCulture);
        if (skip is not null) query["skip"] = skip.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(backend)) query["backend"] = backend;
        if (!string.IsNullOrWhiteSpace(programId)) query["program_id"] = programId;
        if (!string.IsNullOrWhiteSpace(pending)) query["pending"] = pending;

        return SendJsonAsync(HttpMethod.Get, "/jobs", needAuth: true, needCrn: true, includeApiVersionHeader: true, jsonBody: null, ct: ct, queryParams: query);
    }

    private async Task<JsonElement> SendJsonAsync(
        HttpMethod method,
        string path,
        bool needAuth,
        bool needCrn,
        bool includeApiVersionHeader,
        JsonElement? jsonBody,
        CancellationToken ct,
        IReadOnlyDictionary<string, string?>? queryParams = null)
    {
        var url = new Uri(BuildUrl(_cfg.BaseUrl, path, queryParams));

        using var req = new HttpRequestMessage(method, url);
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.UserAgent.ParseAdd("qcapi-tray/0.1.0");

        if (includeApiVersionHeader && !string.IsNullOrWhiteSpace(_cfg.ApiVersion))
        {
            req.Headers.TryAddWithoutValidation("IBM-API-Version", _cfg.ApiVersion);
        }
        if (needCrn)
        {
            req.Headers.TryAddWithoutValidation("Service-CRN", _cfg.ServiceCrn);
        }
        if (needAuth)
        {
            var token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (jsonBody is not null)
        {
            var raw = JsonSerializer.Serialize(jsonBody.Value);
            req.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        }

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var rawJson = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nurl: {url}\nbody:\n{rawJson}");
        }

        using var doc = JsonDocument.Parse(rawJson);
        // Clone the root element so callers can use it after JsonDocument is disposed.
        return doc.RootElement.Clone();
    }

    private static string BuildUrl(string baseUrl, string path, IReadOnlyDictionary<string, string?>? queryParams)
    {
        var sb = new StringBuilder(baseUrl.TrimEnd('/'))
            .Append('/')
            .Append(path.TrimStart('/'));

        if (queryParams is null || queryParams.Count == 0)
        {
            return sb.ToString();
        }

        var first = true;
        foreach (var kv in queryParams)
        {
            var value = kv.Value;
            if (string.IsNullOrWhiteSpace(value)) continue;

            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }
}
