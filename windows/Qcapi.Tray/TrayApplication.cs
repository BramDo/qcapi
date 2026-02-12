using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Qcapi.Tray;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _trayIcon;
    private readonly string? _trayIconSource;
    private readonly ToolStripMenuItem _backendsMenu;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _latestJobItem;
    private readonly ToolStripMenuItem _openWindowItem;
    private readonly ToolStripMenuItem _showConfigItem;
    private readonly ToolStripMenuItem _openLogItem;
    private readonly ToolStripMenuItem _showLastErrorItem;
    private readonly ToolStripMenuItem _testVersionsItem;
    private readonly ToolStripMenuItem _exitItem;

    private readonly QiskitRuntimeClient? _client;
    private readonly string? _configLoadError;
    private readonly System.Windows.Forms.Timer? _startupTimer;
    private BackendsForm? _backendsForm;
    private string? _lastError;

    public TrayApplication()
    {
        Diagnostics.Log("QCAPI Tray starting...");

        _menu = new ContextMenuStrip();

        _menu.Items.Add(new ToolStripMenuItem("QCAPI Tray") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        _refreshItem = new ToolStripMenuItem("Refresh backends (devices)");
        _refreshItem.Click += async (_, _) => await RefreshBackendsAsync(userInitiated: true);
        _menu.Items.Add(_refreshItem);

        _latestJobItem = new ToolStripMenuItem("Show latest quantum job...");
        _latestJobItem.Click += async (_, _) => await ShowLatestQuantumJobAsync();
        _menu.Items.Add(_latestJobItem);

        _backendsMenu = new ToolStripMenuItem("Backends");
        _menu.Items.Add(_backendsMenu);

        _menu.Items.Add(new ToolStripSeparator());

        _openWindowItem = new ToolStripMenuItem("Open backends window...");
        _openWindowItem.Click += (_, _) => OpenBackendsWindow();
        _menu.Items.Add(_openWindowItem);

        _showConfigItem = new ToolStripMenuItem("Show config...");
        _showConfigItem.Click += (_, _) => ShowConfig();
        _menu.Items.Add(_showConfigItem);

        _testVersionsItem = new ToolStripMenuItem("Test connection (GET /versions)");
        _testVersionsItem.Click += async (_, _) => await TestVersionsAsync();
        _menu.Items.Add(_testVersionsItem);

        _openLogItem = new ToolStripMenuItem("Open log file...");
        _openLogItem.Click += (_, _) => Diagnostics.OpenLogFile();
        _menu.Items.Add(_openLogItem);

        _showLastErrorItem = new ToolStripMenuItem("Show last error...");
        _showLastErrorItem.Click += (_, _) => ShowLastError();
        _menu.Items.Add(_showLastErrorItem);

        _menu.Items.Add(new ToolStripSeparator());

        _exitItem = new ToolStripMenuItem("Exit");
        _exitItem.Click += (_, _) => ExitThread();
        _menu.Items.Add(_exitItem);

        try
        {
            var cfg = QiskitConfigLoader.Load();
            _client = new QiskitRuntimeClient(cfg);
            _configLoadError = null;
            Diagnostics.Log($"Loaded config: account={cfg.AccountName ?? "(env)"} base_url={cfg.BaseUrl} api_version={cfg.ApiVersion} qiskit_config_path={cfg.QiskitConfigPath ?? "(n/a)"}");
        }
        catch (Exception ex)
        {
            _client = null;
            _configLoadError = ex.Message;
            _lastError = ex.ToString();
            Diagnostics.LogException(ex, "Failed to load config");

            MessageBox.Show(
                $"Failed to load IBM Quantum credentials.\n\n{ex.Message}\n\n" +
                "Fix your %USERPROFILE%\\.qiskit\\qiskit-ibm.json (channel: ibm_cloud) or set IBM_CLOUD_API_KEY and QCAPI_SERVICE_CRN.",
                "QCAPI Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            _refreshItem.Enabled = false;
            _latestJobItem.Enabled = false;
            _backendsMenu.Enabled = false;
            _openWindowItem.Enabled = false;
            _testVersionsItem.Enabled = false;
        }

        _trayIcon = TrayIconLoader.Load(out _trayIconSource);
        Diagnostics.Log($"Tray icon source: {_trayIconSource ?? "(default)"}");

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "QCAPI Tray",
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenBackendsWindow();

        // Initial population (best-effort). Use a WinForms timer so we run after the message loop
        // starts (otherwise async continuations may run on a threadpool thread).
        if (_client is not null)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1, Enabled = true };
            _startupTimer = timer;
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                await RefreshBackendsAsync(userInitiated: false);
            };
        }
        else
        {
            _startupTimer = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
            _startupTimer?.Dispose();
            _backendsForm?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OpenBackendsWindow()
    {
        if (_client is null)
        {
            MessageBox.Show(
                "No IBM Quantum credentials loaded. See the tray menu item 'Show config...' for details.",
                "QCAPI Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_backendsForm is null || _backendsForm.IsDisposed)
        {
            _backendsForm = new BackendsForm(_client);
            _backendsForm.FormClosed += (_, _) =>
            {
                // Keep tray app alive; just release the reference.
                _backendsForm = null;
            };
        }

        _backendsForm.Show();
        _backendsForm.BringToFront();
        _backendsForm.Activate();
    }

    private void ShowConfig()
    {
        if (_client is null)
        {
            MessageBox.Show(
                $"QCAPI Tray is not configured.\n\nError:\n{_configLoadError ?? "(unknown)"}\n\n" +
                "Expected: a Qiskit IBM Cloud account in %USERPROFILE%\\.qiskit\\qiskit-ibm.json (channel: ibm_cloud), " +
                "or set env vars IBM_CLOUD_API_KEY and QCAPI_SERVICE_CRN.",
                "QCAPI Tray config",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var cfg = _client.Config;
        var crn = cfg.ServiceCrn ?? "";
        var crnHint = crn.StartsWith("crn:", StringComparison.OrdinalIgnoreCase) && crn.Length > 12
            ? $"crn:...{crn[^12..]}"
            : crn;

        MessageBox.Show(
            $"account_name: {cfg.AccountName ?? "(env)"}\n" +
            $"api_version: {cfg.ApiVersion}\n" +
            $"base_url: {cfg.BaseUrl}\n" +
            $"service_crn_hint: {crnHint}\n" +
            $"qiskit_config_path: {cfg.QiskitConfigPath ?? "(n/a)"}\n" +
            $"tray_icon_source: {_trayIconSource ?? "(default)"}\n" +
            $"log_path: {Diagnostics.LogPath}",
            "QCAPI Tray config",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowLastError()
    {
        var msg = string.IsNullOrWhiteSpace(_lastError) ? "(no errors recorded)" : _lastError;
        MessageBox.Show(msg, "QCAPI Tray - Last error", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task TestVersionsAsync()
    {
        if (_client is null)
        {
            MessageBox.Show(
                "No IBM Quantum credentials loaded. Use 'Show config...' for details.",
                "QCAPI Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _testVersionsItem.Enabled = false;
        try
        {
            Diagnostics.Log("Testing connection: GET /versions");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var root = await _client.GetVersionsAsync(cts.Token);
            Diagnostics.LogJson("GET /versions response", root);

            var pretty = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            if (pretty.Length > 20000) pretty = pretty.Substring(0, 20000) + "\n... (truncated)";
            MessageBox.Show(pretty, "GET /versions", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            Diagnostics.LogException(ex, "GET /versions failed");
            MessageBox.Show(ex.Message, "GET /versions failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _testVersionsItem.Enabled = true;
        }
    }

    private async Task ShowLatestQuantumJobAsync()
    {
        if (_client is null)
        {
            MessageBox.Show(
                "No IBM Quantum credentials loaded. Use 'Show config...' for details.",
                "QCAPI Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _latestJobItem.Enabled = false;
        try
        {
            _lastError = null;
            Diagnostics.Log("Loading latest quantum job...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var backendsRoot = await _client.ListBackendsAsync(cts.Token);
            var jobsRoot = await _client.ListJobsAsync(limit: 20, skip: null, backend: null, programId: null, pending: "false", ct: cts.Token);
            Diagnostics.LogJson("GET /jobs response", jobsRoot);

            var latestQuantum = JobExtractor.ExtractLatestQuantumJob(backendsRoot, jobsRoot);
            var latest = latestQuantum ?? JobExtractor.ExtractLatestJob(jobsRoot);
            if (latest is null)
            {
                MessageBox.Show(
                    "No jobs found in GET /jobs response.",
                    "QCAPI Tray - Latest job",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (latestQuantum is null)
            {
                MessageBox.Show(
                    "No recent quantum job found. Showing latest job without backend type filter.\n\n" + FormatJobMessage(latest),
                    "QCAPI Tray - Latest job",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(
                FormatJobMessage(latestQuantum),
                "QCAPI Tray - Latest quantum job",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            Diagnostics.LogException(ex, "Show latest quantum job failed");
            MessageBox.Show(ex.Message, "QCAPI Tray - Latest job failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _latestJobItem.Enabled = true;
        }
    }

    private async Task RefreshBackendsAsync(bool userInitiated)
    {
        if (_client is null)
        {
            return;
        }

        _refreshItem.Enabled = false;
        _backendsMenu.DropDownItems.Clear();
        _backendsMenu.DropDownItems.Add(new ToolStripMenuItem("Loading...") { Enabled = false });

        try
        {
            _lastError = null;
            var cfg = _client.Config;
            Diagnostics.Log($"Refreshing backends... userInitiated={userInitiated} base_url={cfg.BaseUrl} api_version={cfg.ApiVersion} account={cfg.AccountName ?? "(env)"}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var root = await _client.ListBackendsAsync(cts.Token);
            Diagnostics.LogJson("GET /backends response", root);
            var backends = BackendExtractor.ExtractBackends(root);
            Diagnostics.Log($"Parsed backends: {backends.Count}");

            _backendsMenu.DropDownItems.Clear();
            if (backends.Count == 0)
            {
                _backendsMenu.DropDownItems.Add(new ToolStripMenuItem("(no backends)") { Enabled = false });
                if (userInitiated)
                {
                    MessageBox.Show(
                        "Request succeeded but no backends were parsed from the response.\n\n" +
                        "Open 'Open log file...' to inspect the raw JSON and errors.",
                        "QCAPI Tray - No backends parsed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else
            {
                foreach (var b in backends.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var label = b.Name;
                    if (b.Simulator is true) label += " (sim)";
                    if (!string.IsNullOrWhiteSpace(b.Status)) label += $" [{b.Status}]";

                    var item = new ToolStripMenuItem(label);
                    item.Click += (_, _) => ShowBackendJson(b);
                    _backendsMenu.DropDownItems.Add(item);
                }
            }

            _notifyIcon.Text = $"QCAPI Tray ({backends.Count} backends)";
            _backendsForm?.SetBackends(backends);
        }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            Diagnostics.LogException(ex, "Refresh backends failed");

            _backendsMenu.DropDownItems.Clear();
            _backendsMenu.DropDownItems.Add(new ToolStripMenuItem("(error loading backends)") { Enabled = false });

            if (userInitiated)
            {
                MessageBox.Show(ex.Message, "QCAPI Tray - Refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _refreshItem.Enabled = true;
        }
    }

    private void ShowBackendJson(BackendSummary backend)
    {
        var pretty = JsonSerializer.Serialize(backend.Raw, new JsonSerializerOptions { WriteIndented = true });
        MessageBox.Show(pretty, backend.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatJobMessage(JobSummary job)
    {
        var backend = string.IsNullOrWhiteSpace(job.Backend) ? "(unknown)" : job.Backend;
        var status = string.IsNullOrWhiteSpace(job.Status) ? "(unknown)" : job.Status;
        var created = string.IsNullOrWhiteSpace(job.Created) ? "(unknown)" : job.Created;

        return $"id: {job.Id}\nbackend: {backend}\nstatus: {status}\ncreated: {created}";
    }
}
