using DFBlackbox.Core;
using DFBlackbox.Models;
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
        Text = "카메라 속성";
        Width = 520;
        Height = 360;
        KeyPreview = true;

        _properties = new()
        {
            ["노출"] = (VideoCaptureProperties.Exposure, s => s.Exposure, (s, v) => s.Exposure = v),
            ["게인"] = (VideoCaptureProperties.Gain, s => s.Gain, (s, v) => s.Gain = v),
            ["밝기"] = (VideoCaptureProperties.Brightness, s => s.Brightness, (s, v) => s.Brightness = v),
            ["대비"] = (VideoCaptureProperties.Contrast, s => s.Contrast, (s, v) => s.Contrast = v),
            ["채도"] = (VideoCaptureProperties.Saturation, s => s.Saturation, (s, v) => s.Saturation = v),
            ["화이트밸런스"] = (VideoCaptureProperties.WhiteBalanceBlueU, s => s.WhiteBalance, (s, v) => s.WhiteBalance = v),
            ["초점"] = (VideoCaptureProperties.Focus, s => s.Focus, (s, v) => s.Focus = v)
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

        var row = 0;
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
            var apply = new Button { Text = "적용", Dock = DockStyle.Fill, Tag = (item.Value, input) };
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

        var value = (double)tag.Item2.Value;
        _cameraService.SetProperty(tag.Item1.Property, value);
        tag.Item1.Setter(_settings, value);
    }
}
