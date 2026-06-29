using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using OpenCvSharp;

namespace DFBlackbox.Forms;

public sealed class CameraSettingsForm : Form
{
    private readonly CameraService _cameraService;
    private readonly UsbCameraSettings _settings;
    private readonly Dictionary<string, (VideoCaptureProperties Property, Func<UsbCameraSettings, double?> Getter, Action<UsbCameraSettings, double?> Setter)> _properties;
    private readonly TableLayoutPanel _grid = new() { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };

    public CameraSettingsForm(CameraService cameraService, UsbCameraSettings settings)
    {
        _cameraService = cameraService;
        _settings = settings;
        Text = Localization.T("CameraProps.Title");
        Width = 520;
        Height = 360;
        KeyPreview = true;

        _properties = new()
        {
            [Localization.T("CameraProps.Exposure")] = (VideoCaptureProperties.Exposure, s => s.Exposure, (s, v) => s.Exposure = v),
            [Localization.T("CameraProps.Gain")] = (VideoCaptureProperties.Gain, s => s.Gain, (s, v) => s.Gain = v),
            [Localization.T("CameraProps.Brightness")] = (VideoCaptureProperties.Brightness, s => s.Brightness, (s, v) => s.Brightness = v),
            [Localization.T("CameraProps.Contrast")] = (VideoCaptureProperties.Contrast, s => s.Contrast, (s, v) => s.Contrast = v),
            [Localization.T("CameraProps.Saturation")] = (VideoCaptureProperties.Saturation, s => s.Saturation, (s, v) => s.Saturation = v),
            [Localization.T("CameraProps.WhiteBalance")] = (VideoCaptureProperties.WhiteBalanceBlueU, s => s.WhiteBalance, (s, v) => s.WhiteBalance = v),
            [Localization.T("CameraProps.Focus")] = (VideoCaptureProperties.Focus, s => s.Focus, (s, v) => s.Focus = v)
        };

        Build();
    }

    private void Build()
    {
        _grid.RowStyles.Clear();
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        Controls.Add(_grid);

        int row = 0;
        foreach (var item in _properties)
        {
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var value = item.Value.Getter(_settings) ?? _cameraService.GetProperty(item.Value.Property);
            var input = new NumericUpDown
            {
                DecimalPlaces = 2,
                Minimum = -100000,
                Maximum = 100000,
                Value = (decimal)Math.Clamp(value, -100000, 100000),
                Dock = DockStyle.Fill
            };
            var apply = new Button { Text = Localization.T("Button.Apply"), Dock = DockStyle.Fill, Tag = (item.Value, input) };
            apply.Click += Apply_Click;
            _grid.Controls.Add(new Label { Text = item.Key, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            _grid.Controls.Add(input, 1, row);
            _grid.Controls.Add(apply, 2, row);
            row++;
        }
    }

    private void Apply_Click(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<(VideoCaptureProperties Property, Func<UsbCameraSettings, double?> Getter, Action<UsbCameraSettings, double?> Setter), NumericUpDown> tag })
        {
            return;
        }

        double value = (double)tag.Item2.Value;
        _cameraService.SetProperty(tag.Item1.Property, value);
        tag.Item1.Setter(_settings, value);
    }
}
