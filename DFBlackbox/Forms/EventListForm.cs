using System.Diagnostics;
using DFBlackbox.Core;
using DFBlackbox.Utils;
using Krypton.Toolkit;

namespace DFBlackbox.Forms;

public sealed class EventListForm : KryptonForm
{
    private readonly EventLogService _eventLogService;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };

    public EventListForm(EventLogService eventLogService)
    {
        _eventLogService = eventLogService;
        Text = Localization.T("Event.Title");
        StartPosition = FormStartPosition.CenterParent;
        Width = 980;
        Height = 520;
        MinimumSize = new Size(760, 420);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var refresh = new Button { Text = Localization.T("Event.Refresh"), Width = 90 };
        var openFile = new Button { Text = Localization.T("Event.Open"), Width = 90 };
        var openFolder = new Button { Text = Localization.T("Event.Folder"), Width = 90 };
        var clear = new Button { Text = Localization.T("Event.Clear"), Width = 90 };
        refresh.Click += (_, _) => LoadEvents();
        openFile.Click += (_, _) => OpenSelectedFile(false);
        openFolder.Click += (_, _) => OpenSelectedFile(true);
        clear.Click += (_, _) => ClearEvents();
        toolbar.Controls.AddRange([refresh, openFile, openFolder, clear]);
        Controls.Add(_grid);
        Controls.Add(toolbar);
        ConfigureGrid();
        LoadEvents();
        UiTheme.ApplyFormTheme(this);
        ApplyGridTheme();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.Id), HeaderText = "ID", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.StartTime), HeaderText = Localization.T("Event.Start"), Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.EndTime), HeaderText = Localization.T("Event.End"), Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.TriggerReason), HeaderText = Localization.T("Event.Trigger"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ApplyGridTheme()
    {
        _grid.BackgroundColor = UiTheme.WindowBack;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.HeaderBack;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _grid.ColumnHeadersHeight = 34;
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = UiTheme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = UiTheme.AccentSoft;
        _grid.DefaultCellStyle.SelectionForeColor = UiTheme.AccentDark;
        _grid.RowHeadersVisible = false;
        _grid.RowTemplate.Height = 30;
    }

    private void LoadEvents()
    {
        _grid.DataSource = _eventLogService.ReadRecent(200);
    }

    private void ClearEvents()
    {
        if (MessageBox.Show(this, Localization.T("Event.ClearConfirm"), Localization.T("Event.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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
