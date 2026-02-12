using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Qcapi.Tray;

internal static class BackendExtractor
{
    public static List<BackendSummary> ExtractBackends(JsonElement root)
    {
        // Supported shapes:
        // - array of backend objects
        // - object with property "backends"/"devices"/"items" that is an array
        // Some API variants return { "devices": [...] }.
        var arr = TryGetArray(root);
        if (arr is null)
        {
            return new List<BackendSummary>();
        }

        var list = new List<BackendSummary>();
        foreach (var item in arr.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var name = TryGetString(item, "name")
                ?? TryGetString(item, "backend_name")
                ?? TryGetString(item, "id")
                ?? TryGetString(item, "backend")
                ?? "(unknown)";

            bool? simulator = null;
            if (item.TryGetProperty("simulator", out var sim) && sim.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                simulator = sim.ValueKind == JsonValueKind.True;
            }

            string? status = null;
            if (item.TryGetProperty("status", out var st))
            {
                status = st.ValueKind switch
                {
                    JsonValueKind.String => st.GetString(),
                    JsonValueKind.Object => ExtractStatusString(st),
                    _ => null
                };
            }

            list.Add(new BackendSummary(name, simulator, status, item.Clone()));
        }

        return list;
    }

    private static JsonElement? TryGetArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;

        foreach (var prop in new[] { "backends", "devices", "items", "results", "data" })
        {
            if (root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            {
                return v;
            }
        }

        // If the object has exactly one array property, treat that as the payload.
        JsonElement? single = null;
        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Array) continue;
            if (single is not null) return null;
            single = p.Value;
        }
        return single;
    }

    private static string? ExtractStatusString(JsonElement st)
    {
        // Observed shapes:
        // - "online"
        // - { "status": "online" }
        // - { "state": "online" }
        // - { "name": "online", "reason": "available" }
        // - { "name": "...", "message": "..." }
        if (st.ValueKind != JsonValueKind.Object) return null;

        var name = TryGetString(st, "name")
            ?? TryGetString(st, "status")
            ?? TryGetString(st, "state")
            ?? TryGetString(st, "value");

        var reason = TryGetString(st, "reason")
            ?? TryGetString(st, "message")
            ?? TryGetString(st, "detail");

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(reason))
        {
            // Avoid "online (online)" if APIs repeat.
            if (string.Equals(name, reason, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
            return $"{name} ({reason})";
        }

        return name ?? reason;
    }

    private static string? TryGetString(JsonElement obj, string propName)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(propName, out var v) || v.ValueKind != JsonValueKind.String) return null;
        return v.GetString();
    }
}
