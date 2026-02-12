using System.Text.Json;

namespace Qcapi.Tray;

internal sealed record BackendSummary(string Name, bool? Simulator, string? Status, JsonElement Raw);

