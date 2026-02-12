using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Qcapi.Tray;

internal static class JobExtractor
{
    private static readonly string[] BackendItemKeys = ["backends", "devices", "items", "results", "data"];
    private static readonly string[] JobItemKeys = ["jobs", "items", "results", "data"];

    public static JobSummary? ExtractLatestQuantumJob(JsonElement backendsRoot, JsonElement jobsRoot)
    {
        var quantumBackends = ExtractQuantumBackendNames(backendsRoot);
        if (quantumBackends.Count == 0)
        {
            return null;
        }

        foreach (var job in ExtractItems(jobsRoot, JobItemKeys))
        {
            var backendName = JobBackendName(job);
            if (string.IsNullOrWhiteSpace(backendName) || !quantumBackends.Contains(backendName))
            {
                continue;
            }

            var jobId = FirstString(job, "id", "job_id", "jobId");
            if (string.IsNullOrWhiteSpace(jobId))
            {
                continue;
            }

            return BuildSummary(job, jobId, backendName);
        }

        return null;
    }

    public static JobSummary? ExtractLatestJob(JsonElement jobsRoot)
    {
        foreach (var job in ExtractItems(jobsRoot, JobItemKeys))
        {
            var jobId = FirstString(job, "id", "job_id", "jobId");
            if (string.IsNullOrWhiteSpace(jobId))
            {
                continue;
            }

            return BuildSummary(job, jobId, JobBackendName(job));
        }

        return null;
    }

    private static JobSummary BuildSummary(JsonElement job, string jobId, string? backendName)
    {
        var status = FirstString(job, "status", "state");
        var created = FirstString(job, "created", "created_at", "creation_date");
        return new JobSummary(jobId, backendName, status, created, job.Clone());
    }

    private static HashSet<string> ExtractQuantumBackendNames(JsonElement backendsRoot)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var backend in ExtractItems(backendsRoot, BackendItemKeys))
        {
            var name = FirstString(backend, "name", "backend_name", "backend", "id");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (IsSimulatorBackend(backend))
            {
                continue;
            }

            names.Add(name);
        }

        return names;
    }

    private static List<JsonElement> ExtractItems(JsonElement payload, IReadOnlyList<string> candidateKeys)
    {
        var items = new List<JsonElement>();

        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in payload.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    items.Add(item);
                }
            }
            return items;
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return items;
        }

        foreach (var key in candidateKeys)
        {
            if (!payload.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    items.Add(item);
                }
            }
            return items;
        }

        return items;
    }

    private static string? JobBackendName(JsonElement job)
    {
        if (job.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (job.TryGetProperty("backend", out var backend))
        {
            if (backend.ValueKind == JsonValueKind.String)
            {
                var value = backend.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            if (backend.ValueKind == JsonValueKind.Object)
            {
                var nested = FirstString(backend, "name", "backend_name", "id");
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return FirstString(job, "backend_name", "device", "target");
    }

    private static bool IsSimulatorBackend(JsonElement backend)
    {
        if (backend.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var key in new[] { "simulator", "is_simulator" })
        {
            if (backend.TryGetProperty(key, out var raw))
            {
                return AsBool(raw);
            }
        }

        var name = FirstString(backend, "name", "backend_name", "backend", "id");
        return !string.IsNullOrWhiteSpace(name) && name.Contains("simulator", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AsBool(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ParseBoolString(value.GetString()),
            JsonValueKind.Number => ParseBoolNumber(value),
            _ => false,
        };
    }

    private static bool ParseBoolString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" => true,
            "true" => true,
            "yes" => true,
            "y" => true,
            _ => false,
        };
    }

    private static bool ParseBoolNumber(JsonElement value)
    {
        if (value.TryGetInt64(out var i))
        {
            return i != 0;
        }
        if (value.TryGetDouble(out var d))
        {
            return Math.Abs(d) > double.Epsilon;
        }
        return false;
    }

    private static string? FirstString(JsonElement item, params string[] keys)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = value.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
        }

        return null;
    }
}
