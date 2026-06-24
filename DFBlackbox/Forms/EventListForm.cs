using System.Diagnostics;
using DFBlackbox.Core;

namespace DFBlackbox.Forms;

public sealed class EventListForm : Form
{
    private readonly EventLogService _eventLogService;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };

    public EventListForm(EventLogService eventLogService)
    {
        _eventLogService = eventLogService;
        Text = "Recent Events";
        Width = 980;
        Height = 520;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var refresh = new Button { Text = "Refresh", Width = 90 };
        var openFile = new Button { Text = "Open", Width = 90 };
        var openFolder = new Button { Text = "Folder", Width = 90 };
        var clear = new Button { Text = "Clear", Width = 90 };
        refresh.Click += (_, _) => LoadEvents();
        openFile.Click += (_, _) => OpenSelectedFile(false);
        openFolder.Click += (_, _) => OpenSelectedFile(true);
        clear.Click += (_, _) => ClearEvents();
        toolbar.Controls.AddRange([refresh, openFile, openFolder, clear]);
        Controls.Add(_grid);
        Controls.Add(toolbar);
        ConfigureGrid();
        LoadEvents();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.Id), HeaderText = "Id", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.StartTime), HeaderText = "Start", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.EndTime), HeaderText = "End", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.TriggerReason), HeaderText = "Trigger", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void LoadEvents()
    {
        _grid.DataSource = _eventLogService.ReadRecent(200);
    }

    private void ClearEvents()
    {
        if (MessageBox.Show(this, "Clear all recent event records?", "Recent Events", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _eventLogService.Clear();
        LoadEvents();
    }

    private void OpenSelectedFile(bool folder)
    {
        if (_grid.CurrentRow?.DataBoundItem is not Models.EventLog log || string.IsNullOrWhiteSpace(log.FilePath))
        {
            return;
        }

        var target = folder ? Path.GetDirectoryName(log.FilePath) : log.FilePath;
        if (!string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
    }
}
