using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Qcapi.Tray;

internal sealed class IbmIamTokenProvider
{
    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly Uri _iamUri;

    private string? _accessToken;
    private DateTimeOffset _expiresAtUtc;

    public IbmIamTokenProvider(string apiKey, HttpClient httpClient, string iamUrl = "https://iam.cloud.ibm.com/identity/token")
    {
        _apiKey = apiKey;
        _http = httpClient;
        _iamUri = new Uri(iamUrl);
        _expiresAtUtc = DateTimeOffset.MinValue;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // Refresh with some slack so long-running requests don't race expiry.
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _expiresAtUtc.AddSeconds(-60))
        {
            return _accessToken!;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _iamUri);
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "urn:ibm:params:oauth:grant-type:apikey"),
            new KeyValuePair<string, string>("apikey", _apiKey),
        });

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"IAM token request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenProp) || tokenProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("IAM token response missing access_token");
        }

        var token = tokenProp.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("IAM token response contained empty access_token");
        }

        var expiresIn = 3600;
        if (doc.RootElement.TryGetProperty("expires_in", out var expiresProp) && expiresProp.ValueKind == JsonValueKind.Number)
        {
            if (expiresProp.TryGetInt32(out var v) && v > 0) expiresIn = v;
        }

        _accessToken = token;
        _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        return token;
    }
}

