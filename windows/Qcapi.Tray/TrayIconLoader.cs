using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace Qcapi.Tray;

internal static class TrayIconLoader
{
    private const int TargetSizePx = 32;

    // Icon.FromHandle requires manual cleanup of the unmanaged HICON.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Load(out string? source)
    {
        // 1) Explicit env var override.
        var env = Environment.GetEnvironmentVariable("QCAPI_TRAY_ICON_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var icon = TryLoad(env, out var err);
            if (icon is not null)
            {
                source = env;
                Diagnostics.Log($"Loaded tray icon from QCAPI_TRAY_ICON_PATH: {env}");
                return icon;
            }
            Diagnostics.Log($"Failed to load tray icon from QCAPI_TRAY_ICON_PATH='{env}': {err}");
        }

        // 2) Look next to the executable (for portable installs).
        var baseDir = AppContext.BaseDirectory;
        foreach (var name in new[]
        {
            "tray.ico",
            "qcapi-tray.ico",
            "ibm.ico",
            "qiskit.ico",
            "tray.png",
            "qcapi-tray.png",
            "ibm.png",
            "qiskit.png",
        })
        {
            var path = Path.Combine(baseDir, name);
            var icon = TryLoad(path, out _);
            if (icon is not null)
            {
                source = path;
                Diagnostics.Log($"Loaded tray icon from file: {path}");
                return icon;
            }
        }

        // 3) Default (clone so callers can safely Dispose()).
        source = null;
        return (Icon)SystemIcons.Application.Clone();
    }

    private static Icon? TryLoad(string path, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "path is empty";
                return null;
            }
            if (!File.Exists(path))
            {
                error = "file not found";
                return null;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".ico" => LoadIco(path),
                ".png" or ".jpg" or ".jpeg" or ".bmp" => LoadRaster(path),
                _ => throw new NotSupportedException($"unsupported extension '{ext}'"),
            };
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static Icon LoadIco(string path)
    {
        // Load from bytes to avoid keeping the file locked.
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        return new Icon(ms);
    }

    private static Icon LoadRaster(string path)
    {
        using var src = new Bitmap(path);

        using var canvas = new Bitmap(TargetSizePx, TargetSizePx);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var scale = Math.Min(TargetSizePx / (float)src.Width, TargetSizePx / (float)src.Height);
            var w = (int)Math.Max(1, Math.Round(src.Width * scale));
            var h = (int)Math.Max(1, Math.Round(src.Height * scale));
            var x = (TargetSizePx - w) / 2;
            var y = (TargetSizePx - h) / 2;

            g.DrawImage(src, new Rectangle(x, y, w, h));
        }

        var hIcon = canvas.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}

