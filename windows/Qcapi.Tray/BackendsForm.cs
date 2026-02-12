using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Qcapi.Tray;

internal sealed class BackendsForm : Form
{
    private readonly QiskitRuntimeClient _client;
    private readonly BindingList<BackendRow> _rows = new();

    private readonly DataGridView _grid;
    private readonly Button _refreshButton;
    private readonly TextBox _details;

    public BackendsForm(QiskitRuntimeClient client)
    {
        _client = client;

        Text = "QCAPI Backends (Devices)";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);

        _refreshButton = new Button
        {
            Text = "Refresh",
            Dock = DockStyle.Top,
            Height = 36,
        };
        _refreshButton.Click += async (_, _) => await RefreshAsync();

        _grid = new DataGridView
        {
            Dock = DockStyle.Left,
            Width = 420,
            ReadOnly = true,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            DataSource = _rows,
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BackendRow.Name),
            HeaderText = "Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BackendRow.Simulator),
            HeaderText = "Sim",
            Width = 50,
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BackendRow.Status),
            HeaderText = "Status",
            Width = 120,
        });

        _grid.SelectionChanged += (_, _) => UpdateDetails();

        _details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            WordWrap = false,
        };

        Controls.Add(_details);
        Controls.Add(_grid);
        Controls.Add(_refreshButton);

        Shown += async (_, _) => await RefreshAsync();
    }

    public void SetBackends(IEnumerable<BackendSummary> backends)
    {
        _rows.Clear();
        foreach (var b in backends.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            _rows.Add(new BackendRow(b));
        }
        if (_rows.Count > 0)
        {
            _grid.ClearSelection();
            _grid.Rows[0].Selected = true;
        }
        UpdateDetails();
    }

    private async Task RefreshAsync()
    {
        _refreshButton.Enabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var root = await _client.ListBackendsAsync(cts.Token);
            var backends = BackendExtractor.ExtractBackends(root);

            SetBackends(backends);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private void UpdateDetails()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            _details.Text = "";
            return;
        }

        var row = _grid.SelectedRows[0].DataBoundItem as BackendRow;
        if (row is null)
        {
            _details.Text = "";
            return;
        }

        _details.Text = JsonSerializer.Serialize(row.Raw, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class BackendRow
    {
        public string Name { get; }
        public string Simulator { get; }
        public string Status { get; }
        public JsonElement Raw { get; }

        public BackendRow(BackendSummary b)
        {
            Name = b.Name;
            Simulator = b.Simulator is null ? "" : (b.Simulator.Value ? "yes" : "no");
            Status = b.Status ?? "";
            Raw = b.Raw;
        }
    }
}
