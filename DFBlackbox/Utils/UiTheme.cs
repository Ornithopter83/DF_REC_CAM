using Krypton.Toolkit;

namespace DFBlackbox.Utils;

public static class UiTheme
{
    private static KryptonManager? _kryptonManager;
    private static readonly Font ButtonFont = new("Segoe UI", 9.75F, FontStyle.Bold);
    public static readonly Color WindowBack = Color.FromArgb(246, 247, 251);
    public static readonly Color PanelBack = Color.White;
    public static readonly Color PanelAltBack = Color.FromArgb(241, 243, 248);
    public static readonly Color Border = Color.FromArgb(184, 190, 218);
    public static readonly Color Text = Color.FromArgb(34, 40, 63);
    public static readonly Color MutedText = Color.FromArgb(95, 102, 127);
    public static readonly Color Accent = Color.FromArgb(46, 74, 171);
    public static readonly Color AccentDark = Color.FromArgb(31, 48, 124);
    public static readonly Color AccentSoft = Color.FromArgb(235, 239, 255);
    public static readonly Color AccentHover = Color.FromArgb(64, 98, 202);
    public static readonly Color AccentPressed = Color.FromArgb(23, 35, 95);
    public static readonly Color DisabledBack = Color.FromArgb(236, 239, 245);
    public static readonly Color DisabledBorder = Color.FromArgb(209, 214, 230);
    public static readonly Color DisabledText = Color.FromArgb(138, 145, 168);

    public static void ApplyGlobalPalette()
    {
        _kryptonManager ??= new KryptonManager();
        _kryptonManager.GlobalPaletteMode = PaletteMode.Office2013White;
    }

    public static void ApplyFormTheme(Form form)
    {
        form.BackColor = WindowBack;
        form.ForeColor = Text;
        form.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        ApplyControlTree(form);
    }

    public static void ApplyControlTree(Control root)
    {
        foreach (Control control in root.Controls)
        {
            ApplyControlTheme(control);
            if (control.Controls.Count > 0)
            {
                ApplyControlTree(control);
            }
        }
    }

    public static void ApplyControlTheme(Control control)
    {
        switch (control)
        {
            case PictureBox:
                return;
            case KryptonPanel kryptonPanel:
                kryptonPanel.StateCommon.Color1 = PanelBack;
                kryptonPanel.StateCommon.Color2 = PanelBack;
                return;
            case KryptonButton kryptonButton:
                ApplyKryptonButtonTheme(kryptonButton);
                return;
            case KryptonTextBox kryptonTextBox:
                ApplyKryptonInputTheme(kryptonTextBox);
                return;
            case KryptonComboBox kryptonComboBox:
                ApplyKryptonInputTheme(kryptonComboBox);
                return;
            case KryptonNumericUpDown kryptonNumericUpDown:
                ApplyKryptonInputTheme(kryptonNumericUpDown);
                return;
            case KryptonCheckBox or KryptonRadioButton:
                control.BackColor = PanelBack;
                control.ForeColor = Text;
                return;
            case MenuStrip menuStrip:
                menuStrip.BackColor = PanelBack;
                menuStrip.ForeColor = Text;
                return;
            case StatusStrip statusStrip:
                statusStrip.BackColor = PanelAltBack;
                statusStrip.ForeColor = MutedText;
                return;
            case Button button:
                ApplyButtonTheme(button);
                return;
            case TextBox textBox:
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = Color.White;
                textBox.ForeColor = Text;
                return;
            case ComboBox comboBox:
                comboBox.BackColor = Color.White;
                comboBox.ForeColor = Text;
                return;
            case NumericUpDown numericUpDown:
                numericUpDown.BackColor = Color.White;
                numericUpDown.ForeColor = Text;
                return;
            case CheckBox or RadioButton:
                control.BackColor = PanelBack;
                control.ForeColor = Text;
                return;
            case Label label:
                label.BackColor = PanelBack;
                label.ForeColor = label.Font.Bold ? AccentDark : MutedText;
                return;
            case FlowLayoutPanel or Panel:
                control.BackColor = PanelBack;
                control.ForeColor = Text;
                return;
        }
    }

    public static void ApplyButtonTheme(Button button)
    {
        button.Height = Math.Max(button.Height, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.Font = ButtonFont;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = AccentPressed;
        button.UseVisualStyleBackColor = false;
        button.EnabledChanged -= ApplyButtonEnabledTheme;
        button.EnabledChanged += ApplyButtonEnabledTheme;
        ApplyButtonEnabledTheme(button, EventArgs.Empty);
    }

    private static void ApplyButtonEnabledTheme(object? sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.Enabled)
        {
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = AccentDark;
            return;
        }

        button.BackColor = DisabledBack;
        button.ForeColor = DisabledText;
        button.FlatAppearance.BorderColor = DisabledBorder;
    }

    public static void ApplyPrimaryButtonTheme(Button button)
    {
        ApplyButtonTheme(button);
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = AccentDark;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = AccentPressed;
    }

    public static void ApplyKryptonButtonTheme(KryptonButton button)
    {
        button.Height = Math.Max(button.Height, 34);
        button.Margin = new Padding(button.Margin.Left, 4, button.Margin.Right, 4);
        button.StateCommon.Back.Color1 = Accent;
        button.StateCommon.Back.Color2 = Accent;
        button.StateCommon.Border.Color1 = AccentDark;
        button.StateCommon.Border.Color2 = AccentDark;
        button.StateCommon.Border.Rounding = 4;
        button.StateCommon.Border.Width = 1;
        button.StateCommon.Content.ShortText.Color1 = Color.White;
        button.StateCommon.Content.ShortText.Font = ButtonFont;
        button.StateTracking.Back.Color1 = AccentHover;
        button.StateTracking.Back.Color2 = AccentHover;
        button.StateTracking.Border.Color1 = AccentDark;
        button.StateTracking.Border.Color2 = AccentDark;
        button.StateTracking.Content.ShortText.Color1 = Color.White;
        button.StatePressed.Back.Color1 = AccentPressed;
        button.StatePressed.Back.Color2 = AccentPressed;
        button.StatePressed.Content.ShortText.Color1 = Color.White;
        button.OverrideDefault.Back.Color1 = Accent;
        button.OverrideDefault.Back.Color2 = Accent;
        button.OverrideDefault.Border.Color1 = AccentDark;
        button.OverrideDefault.Border.Color2 = AccentDark;
        button.StateDisabled.Back.Color1 = DisabledBack;
        button.StateDisabled.Back.Color2 = DisabledBack;
        button.StateDisabled.Border.Color1 = DisabledBorder;
        button.StateDisabled.Border.Color2 = DisabledBorder;
        button.StateDisabled.Content.ShortText.Color1 = DisabledText;
    }

    public static void ApplyPrimaryKryptonButtonTheme(KryptonButton button)
    {
        ApplyKryptonButtonTheme(button);
        button.StateCommon.Back.Color1 = AccentDark;
        button.StateCommon.Back.Color2 = AccentDark;
        button.StateCommon.Border.Color1 = AccentPressed;
        button.StateCommon.Border.Color2 = AccentPressed;
        button.StateCommon.Content.ShortText.Color1 = Color.White;
        button.StateTracking.Back.Color1 = Accent;
        button.StateTracking.Back.Color2 = Accent;
        button.StateTracking.Border.Color1 = AccentPressed;
        button.StateTracking.Border.Color2 = AccentPressed;
        button.StateTracking.Content.ShortText.Color1 = Color.White;
        button.StatePressed.Back.Color1 = AccentPressed;
        button.StatePressed.Back.Color2 = AccentPressed;
        button.OverrideDefault.Back.Color1 = AccentDark;
        button.OverrideDefault.Back.Color2 = AccentDark;
        button.OverrideDefault.Border.Color1 = AccentPressed;
        button.OverrideDefault.Border.Color2 = AccentPressed;
    }

    private static void ApplyKryptonInputTheme(KryptonTextBox textBox)
    {
        textBox.Height = Math.Max(textBox.Height, 30);
        textBox.StateCommon.Back.Color1 = Color.White;
        textBox.StateCommon.Border.Color1 = Border;
        textBox.StateCommon.Border.Rounding = 3;
        textBox.StateCommon.Border.Width = 1;
        textBox.StateCommon.Content.Color1 = Text;
        textBox.StateActive.Back.Color1 = Color.White;
        textBox.StateActive.Border.Color1 = Border;
        textBox.StateDisabled.Back.Color1 = DisabledBack;
        textBox.StateDisabled.Border.Color1 = DisabledBorder;
        textBox.StateDisabled.Content.Color1 = DisabledText;
    }

    private static void ApplyKryptonInputTheme(KryptonComboBox comboBox)
    {
        comboBox.Height = Math.Max(comboBox.Height, 30);
        comboBox.StateCommon.ComboBox.Back.Color1 = Color.White;
        comboBox.StateCommon.ComboBox.Border.Color1 = Border;
        comboBox.StateCommon.ComboBox.Border.Rounding = 3;
        comboBox.StateCommon.ComboBox.Border.Width = 1;
        comboBox.StateCommon.ComboBox.Content.Color1 = Text;
        comboBox.StateActive.ComboBox.Back.Color1 = Color.White;
        comboBox.StateActive.ComboBox.Border.Color1 = Border;
        comboBox.StateDisabled.ComboBox.Back.Color1 = DisabledBack;
        comboBox.StateDisabled.ComboBox.Border.Color1 = DisabledBorder;
        comboBox.StateDisabled.ComboBox.Content.Color1 = DisabledText;
    }

    private static void ApplyKryptonInputTheme(KryptonNumericUpDown numericUpDown)
    {
        numericUpDown.Height = Math.Max(numericUpDown.Height, 30);
        numericUpDown.StateCommon.Back.Color1 = Color.White;
        numericUpDown.StateCommon.Border.Color1 = Border;
        numericUpDown.StateCommon.Border.Rounding = 3;
        numericUpDown.StateCommon.Border.Width = 1;
        numericUpDown.StateCommon.Content.Color1 = Text;
        numericUpDown.StateActive.Back.Color1 = Color.White;
        numericUpDown.StateActive.Border.Color1 = Border;
        numericUpDown.StateDisabled.Back.Color1 = DisabledBack;
        numericUpDown.StateDisabled.Border.Color1 = DisabledBorder;
        numericUpDown.StateDisabled.Content.Color1 = DisabledText;
    }
}
