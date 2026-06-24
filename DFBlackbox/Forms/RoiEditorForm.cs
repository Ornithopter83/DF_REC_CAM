namespace DFBlackbox.Forms;

public sealed class RoiEditorForm : Form
{
    public RoiEditorForm()
    {
        Text = "ROI Editor";
        Width = 420;
        Height = 180;
        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "ROI mouse editing is reserved for the next implementation pass."
        });
    }
}
