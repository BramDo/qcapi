using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Qcapi.Tray;

internal static class Diagnostics
{
    private static readonly object _lock = new();
    private static string? _logPath;

    public static string LogPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_logPath)) return _logPath!;
            _logPath = ComputeLogPath();
            return _logPath!;
        }
        set
        {
            // Allow overriding in tests or via env var (if we add it later).
            _logPath = value;
        }
    }

    public static void Log(string message)
    {
        try
        {
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                EnsureDirExists();
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Never crash the tray app because logging failed.
        }
    }

    public static void LogException(Exception ex, string context)
    {
        Log($"{context}: {ex.GetType().Name}: {ex.Message}");
        Log(ex.ToString());
    }

    public static void LogJson(string context, JsonElement json, int maxChars = 20000)
    {
        try
        {
            var pretty = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
            if (pretty.Length > maxChars)
            {
                pretty = pretty.Substring(0, maxChars) + "\n... (truncated)";
            }
            Log($"{context} JSON:\n{pretty}");
        }
        catch (Exception ex)
        {
            LogException(ex, $"{context}: failed to serialize JSON for logging");
        }
    }

    public static void OpenLogFile()
    {
        try
        {
            EnsureDirExists();
            if (!File.Exists(LogPath))
            {
                File.WriteAllText(LogPath, "", Encoding.UTF8);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{LogPath}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Ignore.
        }
    }

    private static void EnsureDirExists()
    {
        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string ComputeLogPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "Qcapi.Tray", "qcapi-tray.log");
    }
}

