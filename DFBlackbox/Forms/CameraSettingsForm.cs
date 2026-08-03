using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using Krypton.Toolkit;
using OpenCvSharp;

namespace DFBlackbox.Forms;

public sealed class CameraSettingsForm : KryptonForm
{
    private readonly UsbCameraSettings _settings;
    private readonly UsbCameraPropertySession? _session;
    private readonly List<PropertyEditor> _editors = new();
    private bool _initializing = true;
    private bool _synchronizingValue;
    private bool _defaultsStaged;
    private bool _accepted;
    private bool _restored;

    public CameraSettingsForm(UsbCameraSettings settings)
    {
        _settings = settings;
        _session = UsbCameraPropertySession.TryOpen(settings.DeviceIndex);
        Text = Localization.T("CameraProps.Title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new System.Drawing.Size(660, 430);
        Build();
        UiTheme.ApplyFormTheme(this);
        ApplyEditorTheme();
        _initializing = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_accepted)
        {
            RestoreOriginalValues();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        root.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F),
            Text = Localization.T("CameraProps.LiveHint"),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var grid = new TableLayoutPanel
        {
            AutoScroll = true,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            RowCount = 1
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
        AddHeader(grid, 0, Localization.T("CameraProps.Property"));
        AddHeader(grid, 1, Localization.T("CameraProps.Value"));
        AddHeader(grid, 2, Localization.T("CameraProps.Auto"));
        AddHeader(grid, 3, Localization.T("CameraProps.Support"));

        foreach (PropertyDefinition definition in GetDefinitions())
        {
            AddPropertyRow(grid, definition);
        }

        root.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        var apply = new KryptonButton { Text = Localization.T("Button.Apply"), Width = 100, Height = 36 };
        var cancel = new KryptonButton { Text = Localization.T("Button.Cancel"), Width = 100, Height = 36 };
        var restoreDefaults = new KryptonButton
        {
            Enabled = _editors.Any(item => item.Capability.Supported),
            Text = Localization.T("Settings.RestoreDefaults"),
            Width = 120,
            Height = 36
        };
        apply.Click += (_, _) => ApplyAndClose();
        cancel.Click += (_, _) => CancelAndClose();
        restoreDefaults.Click += (_, _) => StageDefaultValues();
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(restoreDefaults);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private void AddPropertyRow(TableLayoutPanel grid, PropertyDefinition definition)
    {
        UsbCameraPropertyCapability capability = _session?.GetCapability(definition.Property)
            ?? new UsbCameraPropertyCapability(definition.Property, false, 0, 0, 1, 0, 0, false, false, false);
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        var name = new Label
        {
            Dock = DockStyle.Fill,
            Text = Localization.T(definition.LocalizationKey),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var slider = new TrackBar
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Enabled = capability.Supported && capability.SupportsManual && !capability.IsAutomatic,
            Minimum = capability.Supported ? capability.Minimum : 0,
            Maximum = capability.Supported ? Math.Max(capability.Minimum, capability.Maximum) : 0,
            SmallChange = capability.Supported ? capability.Step : 1,
            LargeChange = capability.Supported ? GetLargeChange(capability.Step, capability.Minimum, capability.Maximum) : 1,
            TickStyle = TickStyle.None,
            Value = capability.Supported ? Math.Clamp(capability.CurrentValue, capability.Minimum, capability.Maximum) : 0
        };
        var value = new NumericUpDown
        {
            DecimalPlaces = 0,
            Dock = DockStyle.Fill,
            Enabled = capability.Supported && capability.SupportsManual && !capability.IsAutomatic,
            Increment = capability.Supported ? Math.Max(1, capability.Step) : 1,
            Minimum = capability.Supported ? capability.Minimum : 0,
            Maximum = capability.Supported ? Math.Max(capability.Minimum, capability.Maximum) : 0,
            Value = capability.Supported ? Math.Clamp(capability.CurrentValue, capability.Minimum, capability.Maximum) : 0
        };
        var automatic = new CheckBox
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            Checked = capability.Supported && capability.IsAutomatic,
            Enabled = capability.Supported && capability.SupportsAutomatic,
            Text = string.Empty
        };
        var support = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = capability.Supported ? UiTheme.Success : UiTheme.DisabledText,
            Text = capability.Supported ? Localization.T("CameraProps.Supported") : Localization.T("CameraProps.Unsupported"),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var valuePanel = BuildValuePanel(capability, slider, value);
        var editor = new PropertyEditor(definition, capability, slider, value, automatic, support);
        _editors.Add(editor);
        slider.ValueChanged += (_, _) => SynchronizeAndApply(editor, slider.Value);
        value.ValueChanged += (_, _) => SynchronizeAndApply(editor, Decimal.ToInt32(value.Value));
        automatic.CheckedChanged += (_, _) =>
        {
            slider.Enabled = capability.SupportsManual && !automatic.Checked;
            value.Enabled = capability.SupportsManual && !automatic.Checked;
            ApplyLive(editor);
        };

        grid.Controls.Add(name, 0, row);
        grid.Controls.Add(valuePanel, 1, row);
        grid.Controls.Add(automatic, 2, row);
        grid.Controls.Add(support, 3, row);
    }

    private static Control BuildValuePanel(
        UsbCameraPropertyCapability capability,
        TrackBar slider,
        NumericUpDown value)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
        panel.Controls.Add(CreateRangeLabel(capability.Supported ? capability.Minimum.ToString() : "-"), 0, 0);
        panel.Controls.Add(slider, 1, 0);
        panel.Controls.Add(CreateRangeLabel(capability.Supported ? capability.Maximum.ToString() : "-"), 2, 0);
        panel.Controls.Add(value, 3, 0);
        return panel;
    }

    private static Label CreateRangeLabel(string text) => new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 8F),
        Text = text,
        TextAlign = ContentAlignment.MiddleCenter
    };

    private void SynchronizeAndApply(PropertyEditor editor, int requestedValue)
    {
        if (_synchronizingValue || !editor.Capability.Supported)
        {
            return;
        }

        int value = SnapToStep(editor.Capability, requestedValue);
        try
        {
            _synchronizingValue = true;
            if (editor.Slider.Value != value)
            {
                editor.Slider.Value = value;
            }

            if (editor.Value.Value != value)
            {
                editor.Value.Value = value;
            }
        }
        finally
        {
            _synchronizingValue = false;
        }

        ApplyLive(editor);
    }

    private static int SnapToStep(UsbCameraPropertyCapability capability, int requestedValue)
    {
        int clamped = Math.Clamp(requestedValue, capability.Minimum, capability.Maximum);
        long offset = (long)clamped - capability.Minimum;
        long steps = (offset + (capability.Step / 2L)) / capability.Step;
        long snapped = capability.Minimum + (steps * capability.Step);
        return (int)Math.Clamp(snapped, capability.Minimum, capability.Maximum);
    }

    private static int GetLargeChange(int step, int minimum, int maximum)
    {
        long range = (long)maximum - minimum;
        return (int)Math.Clamp(Math.Max(step, range / 10L), 1L, int.MaxValue);
    }

    private void ApplyLive(PropertyEditor editor)
    {
        if (_initializing || _defaultsStaged || !editor.Capability.Supported || _session is null)
        {
            return;
        }

        bool automatic = editor.Automatic.Enabled && editor.Automatic.Checked;
        bool applied = _session.TrySet(editor.Definition.Property, Decimal.ToInt32(editor.Value.Value), automatic);
        editor.Support.Text = applied ? Localization.T("CameraProps.Supported") : Localization.T("CameraProps.ApplyFailed");
        editor.Support.ForeColor = applied ? UiTheme.Success : UiTheme.Danger;
    }

    private void ApplyAndClose()
    {
        bool appliedAll = true;
        foreach (PropertyEditor editor in _editors.Where(item => item.Capability.Supported))
        {
            bool automatic = editor.Automatic.Enabled && editor.Automatic.Checked;
            bool applied = _session?.TrySet(
                editor.Definition.Property,
                Decimal.ToInt32(editor.Value.Value),
                automatic) == true;
            editor.Support.Text = applied ? Localization.T("CameraProps.Supported") : Localization.T("CameraProps.ApplyFailed");
            editor.Support.ForeColor = applied ? UiTheme.Success : UiTheme.Danger;
            appliedAll &= applied;
        }

        if (!appliedAll)
        {
            return;
        }

        foreach (PropertyEditor editor in _editors.Where(item => item.Capability.Supported))
        {
            editor.Definition.Setter(_settings, (double)editor.Value.Value);
        }

        _accepted = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void StageDefaultValues()
    {
        _defaultsStaged = true;
        try
        {
            _synchronizingValue = true;
            foreach (PropertyEditor editor in _editors.Where(item => item.Capability.Supported))
            {
                int defaultValue = SnapToStep(editor.Capability, editor.Capability.DefaultValue);
                editor.Slider.Value = defaultValue;
                editor.Value.Value = defaultValue;
                editor.Support.Text = Localization.T("CameraProps.Supported");
                editor.Support.ForeColor = UiTheme.Success;
            }
        }
        finally
        {
            _synchronizingValue = false;
        }
    }

    private void CancelAndClose()
    {
        RestoreOriginalValues();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void RestoreOriginalValues()
    {
        if (_restored || _session is null)
        {
            return;
        }

        _restored = true;
        foreach (PropertyEditor editor in _editors.Where(item => item.Capability.Supported))
        {
            _session.TrySet(
                editor.Definition.Property,
                editor.Capability.CurrentValue,
                editor.Capability.IsAutomatic);
        }
    }

    private static void AddHeader(TableLayoutPanel grid, int column, string text)
    {
        grid.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter
        }, column, 0);
    }

    private void ApplyEditorTheme()
    {
        foreach (PropertyEditor editor in _editors)
        {
            editor.Slider.BackColor = Color.White;
            editor.Value.BackColor = Color.White;
            editor.Automatic.BackColor = Color.White;
        }
    }

    private static IReadOnlyList<PropertyDefinition> GetDefinitions() => new PropertyDefinition[]
    {
        new("CameraProps.Exposure", VideoCaptureProperties.Exposure, (settings, value) => settings.Exposure = value),
        new("CameraProps.Gain", VideoCaptureProperties.Gain, (settings, value) => settings.Gain = value),
        new("CameraProps.Brightness", VideoCaptureProperties.Brightness, (settings, value) => settings.Brightness = value),
        new("CameraProps.Contrast", VideoCaptureProperties.Contrast, (settings, value) => settings.Contrast = value),
        new("CameraProps.Saturation", VideoCaptureProperties.Saturation, (settings, value) => settings.Saturation = value),
        new("CameraProps.WhiteBalance", VideoCaptureProperties.WhiteBalanceBlueU, (settings, value) => settings.WhiteBalance = value),
        new("CameraProps.Focus", VideoCaptureProperties.Focus, (settings, value) => settings.Focus = value)
    };

    private sealed record PropertyDefinition(
        string LocalizationKey,
        VideoCaptureProperties Property,
        Action<UsbCameraSettings, double?> Setter);

    private sealed record PropertyEditor(
        PropertyDefinition Definition,
        UsbCameraPropertyCapability Capability,
        TrackBar Slider,
        NumericUpDown Value,
        CheckBox Automatic,
        Label Support);
}
