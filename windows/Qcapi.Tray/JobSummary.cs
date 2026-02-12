using System.Text.Json;

namespace Qcapi.Tray;

internal sealed record JobSummary(string Id, string? Backend, string? Status, string? Created, JsonElement Raw);
