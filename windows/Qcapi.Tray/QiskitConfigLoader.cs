using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Qcapi.Tray;

internal static class QiskitConfigLoader
{
    private const string DefaultApiVersion = "2026-02-01";
    private const string DefaultBaseUrlUs = "https://quantum.cloud.ibm.com/api/v1";
    private const string DefaultBaseUrlEuDe = "https://eu-de.quantum.cloud.ibm.com/api/v1";

    public static QcapiConfig Load()
    {
        var envCfg = TryLoadFromEnv();
        if (envCfg is not null) return envCfg;

        var (accountName, account, qiskitConfigPath) = LoadIbmCloudAccountFromQiskitConfig();
        var apiKey = RequireString(account, "token", $"Missing/invalid token in Qiskit account '{accountName}'");
        var serviceCrn = RequireString(account, "instance", $"Missing/invalid instance (Service CRN) in Qiskit account '{accountName}'");

        var baseUrl = Environment.GetEnvironmentVariable("QCAPI_BASE_URL") ?? InferBaseUrlFromCrn(serviceCrn);
        var apiVersion = Environment.GetEnvironmentVariable("QCAPI_API_VERSION") ?? DefaultApiVersion;

        return new QcapiConfig(
            IbmCloudApiKey: apiKey,
            ServiceCrn: serviceCrn,
            BaseUrl: baseUrl,
            ApiVersion: apiVersion,
            AccountName: accountName,
            QiskitConfigPath: qiskitConfigPath
        );
    }

    private static QcapiConfig? TryLoadFromEnv()
    {
        var apiKey = Environment.GetEnvironmentVariable("IBM_CLOUD_API_KEY");
        var crn = Environment.GetEnvironmentVariable("QCAPI_SERVICE_CRN");
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(crn))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing env var IBM_CLOUD_API_KEY");
        }
        if (string.IsNullOrWhiteSpace(crn))
        {
            throw new InvalidOperationException("Missing env var QCAPI_SERVICE_CRN");
        }

        var baseUrl = Environment.GetEnvironmentVariable("QCAPI_BASE_URL") ?? InferBaseUrlFromCrn(crn);
        var apiVersion = Environment.GetEnvironmentVariable("QCAPI_API_VERSION") ?? DefaultApiVersion;

        return new QcapiConfig(
            IbmCloudApiKey: apiKey,
            ServiceCrn: crn,
            BaseUrl: baseUrl,
            ApiVersion: apiVersion,
            AccountName: null,
            QiskitConfigPath: null
        );
    }

    private static string InferBaseUrlFromCrn(string serviceCrn)
    {
        if (serviceCrn.Contains(":eu-de:", StringComparison.Ordinal))
        {
            return DefaultBaseUrlEuDe;
        }
        return DefaultBaseUrlUs;
    }

    private static (string AccountName, JsonElement Account, string QiskitConfigPath) LoadIbmCloudAccountFromQiskitConfig()
    {
        var path = GetQiskitConfigPath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Qiskit config not found: {path}");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Unexpected Qiskit config shape (expected object): {path}");
        }

        var accounts = doc.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Object)
            .Select(p => (Name: p.Name, Cfg: p.Value))
            .ToList();

        if (accounts.Count == 0)
        {
            throw new InvalidOperationException($"No accounts found in Qiskit config: {path}");
        }

        var requested = Environment.GetEnvironmentVariable("QCAPI_QISKIT_ACCOUNT");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = accounts.FirstOrDefault(a => string.Equals(a.Name, requested, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(match.Name))
            {
                throw new InvalidOperationException($"Account '{requested}' not found in Qiskit config");
            }
            EnsureIbmCloudChannel(match.Name, match.Cfg);
            return (match.Name, match.Cfg.Clone(), path);
        }

        // Prefer is_default_account == true.
        foreach (var kvp in accounts)
        {
            var isDefault = kvp.Cfg.TryGetProperty("is_default_account", out var v) && v.ValueKind == JsonValueKind.True;
            if (isDefault)
            {
                EnsureIbmCloudChannel(kvp.Name, kvp.Cfg);
                return (kvp.Name, kvp.Cfg.Clone(), path);
            }
        }

        // Conventional names.
        foreach (var preferred in new[] { "default-ibm-cloud", "default" })
        {
            var match = accounts.FirstOrDefault(a => string.Equals(a.Name, preferred, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(match.Name))
            {
                EnsureIbmCloudChannel(match.Name, match.Cfg);
                return (match.Name, match.Cfg.Clone(), path);
            }
        }

        // First account.
        var first = accounts[0];
        EnsureIbmCloudChannel(first.Name, first.Cfg);
        return (first.Name, first.Cfg.Clone(), path);
    }

    private static void EnsureIbmCloudChannel(string accountName, JsonElement account)
    {
        var channel = account.TryGetProperty("channel", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (!string.Equals(channel, "ibm_cloud", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Selected Qiskit account is not an IBM Cloud account. " +
                $"account='{accountName}' channel='{channel}'. " +
                "Pick an account with channel 'ibm_cloud' or set IBM_CLOUD_API_KEY/QCAPI_SERVICE_CRN env vars."
            );
        }
    }

    private static string GetQiskitConfigPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("QCAPI_QISKIT_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return ExpandHome(overridePath);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".qiskit", "qiskit-ibm.json");
    }

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (path.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rest = path.Length == 1 ? "" : path.Substring(1).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.Combine(home, rest);
        }
        return path;
    }

    private static string RequireString(JsonElement obj, string propName, string error)
    {
        if (obj.TryGetProperty(propName, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        throw new InvalidOperationException(error);
    }
}
