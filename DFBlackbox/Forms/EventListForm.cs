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
        Text = "최근 이벤트";
        Width = 980;
        Height = 520;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var refresh = new Button { Text = "새로고침", Width = 90 };
        var openFile = new Button { Text = "열기", Width = 90 };
        var openFolder = new Button { Text = "폴더", Width = 90 };
        var clear = new Button { Text = "지우기", Width = 90 };
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.Id), HeaderText = "ID", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.StartTime), HeaderText = "시작", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.EndTime), HeaderText = "종료", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Models.EventLog.TriggerReason), HeaderText = "트리거", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void LoadEvents()
    {
        _grid.DataSource = _eventLogService.ReadRecent(200);
    }

    private void ClearEvents()
    {
        if (MessageBox.Show(this, "최근 이벤트 기록을 모두 지울까요?", "최근 이벤트", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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
