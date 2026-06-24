namespace DFBlackbox.Forms;

public sealed class PlaybackForm : Form
{
    public PlaybackForm()
    {
        Text = "Playback";
        Width = 420;
        Height = 180;
        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Playback uses the Windows default player in this version."
        });
    }
}
