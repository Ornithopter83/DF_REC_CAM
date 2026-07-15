using System.Drawing.Drawing2D;

namespace DFBlackbox.Forms;

public sealed class PlaybackControl : Control
{
    private readonly Rectangle[] _buttonRects = new Rectangle[3];
    private Rectangle _timelineRect;
    private bool _draggingTimeline;
    private string _fpsText = "FPS: 0";
    private string _sourceText = "녹화 파일 재생";

    public event EventHandler? PreviousClicked;
    public event EventHandler? PlayPauseClicked;
    public event EventHandler? NextClicked;
    public event EventHandler<int>? SeekRequested;

    public int Minimum { get; set; }
    public int Maximum { get; set; }

    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            Invalidate();
        }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            Invalidate();
        }
    }

    public string CurrentTimeText { get; set; } = "00:00";
    public string TotalTimeText { get; set; } = "00:00";

    public string FpsText
    {
        get => _fpsText;
        set
        {
            _fpsText = value;
            Invalidate();
        }
    }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            _sourceText = value;
            Invalidate();
        }
    }

    public PlaybackControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.FromArgb(245, 247, 250);
        ForeColor = Color.FromArgb(17, 35, 61);
        Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        MinimumSize = new Size(320, 228);
        Cursor = Cursors.Default;
    }

    public void SetRange(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = Math.Max(minimum, maximum);
        Value = Math.Clamp(Value, Minimum, Maximum);
        Invalidate();
    }

    public void SetPosition(int value, string currentTime, string totalTime)
    {
        CurrentTimeText = currentTime;
        TotalTimeText = totalTime;
        Value = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        Rectangle card = new(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
        using (var shadow = new SolidBrush(Color.FromArgb(24, 22, 44, 82)))
        {
            FillRoundedRectangle(g, shadow, new Rectangle(card.Left + 1, card.Top + 2, card.Width, card.Height), 12);
        }

        using (var cardBrush = new SolidBrush(Color.White))
        using (var cardPen = new Pen(Color.FromArgb(218, 225, 236), 1F))
        {
            FillRoundedRectangle(g, cardBrush, card, 12);
            DrawRoundedRectangle(g, cardPen, card, 12);
        }

        DrawTimeLabels(g, card);
        _timelineRect = new Rectangle(card.Left + 20, card.Top + 42, Math.Max(1, card.Width - 40), 22);
        DrawTimeline(g);
        DrawButtons(g, card);
        DrawStatusPills(g, card);
    }

    private void DrawTimeLabels(Graphics g, Rectangle card)
    {
        using var textBrush = new SolidBrush(ForeColor);
        using var timeFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        g.DrawString(CurrentTimeText, timeFont, textBrush, card.Left + 20, card.Top + 14);
        SizeF totalSize = g.MeasureString(TotalTimeText, timeFont);
        g.DrawString(TotalTimeText, timeFont, textBrush, card.Right - totalSize.Width - 20, card.Top + 14);
    }

    private void DrawTimeline(Graphics g)
    {
        Rectangle track = new(_timelineRect.Left, _timelineRect.Top + 7, _timelineRect.Width, 8);
        double ratio = Maximum <= Minimum ? 0 : (Value - Minimum) / (double)(Maximum - Minimum);
        int progressWidth = (int)Math.Round(track.Width * ratio);

        using var trackBrush = new SolidBrush(Color.FromArgb(224, 230, 239));
        using var progressBrush = new SolidBrush(Color.FromArgb(45, 103, 230));
        FillRoundedRectangle(g, trackBrush, track, 4);
        if (progressWidth > 0)
        {
            FillRoundedRectangle(g, progressBrush, new Rectangle(track.Left, track.Top, Math.Max(4, progressWidth), track.Height), 4);
        }

        int thumbX = track.Left + Math.Clamp(progressWidth, 0, track.Width);
        Rectangle thumb = new(thumbX - 8, track.Top - 5, 18, 18);
        using var thumbFill = new SolidBrush(Color.White);
        using var thumbPen = new Pen(Color.FromArgb(66, 120, 235), 2F);
        g.FillEllipse(thumbFill, thumb);
        g.DrawEllipse(thumbPen, thumb);
    }

    private void DrawButtons(Graphics g, Rectangle card)
    {
        int centerX = card.Left + card.Width / 2;
        const int sideSize = 58;
        const int playSize = 74;
        int playTop = card.Top + 78;
        _buttonRects[0] = new Rectangle(centerX - 116, playTop + 8, sideSize, sideSize);
        _buttonRects[1] = new Rectangle(centerX - playSize / 2, playTop, playSize, playSize);
        _buttonRects[2] = new Rectangle(centerX + 58, playTop + 8, sideSize, sideSize);

        DrawSeekButton(g, _buttonRects[0], backwards: true);
        DrawPlayButton(g, _buttonRects[1]);
        DrawSeekButton(g, _buttonRects[2], backwards: false);
    }

    private void DrawSeekButton(Graphics g, Rectangle rect, bool backwards)
    {
        DrawButtonSurface(g, rect, emphasized: false);
        bool hover = rect.Contains(PointToClient(MousePosition));
        Color iconColor = hover ? Color.FromArgb(25, 83, 206) : Color.FromArgb(45, 103, 230);
        using var pen = new Pen(iconColor, 2.4F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        Rectangle arc = Rectangle.Inflate(rect, -15, -15);
        g.DrawArc(pen, arc, backwards ? 205 : -25, 285);

        PointF tip = backwards
            ? new PointF(arc.Left - 1, arc.Top + arc.Height * 0.45F)
            : new PointF(arc.Right + 1, arc.Top + arc.Height * 0.45F);
        PointF[] arrow = backwards
            ? new[] { tip, new PointF(tip.X + 8, tip.Y - 4), new PointF(tip.X + 7, tip.Y + 5) }
            : new[] { tip, new PointF(tip.X - 8, tip.Y - 4), new PointF(tip.X - 7, tip.Y + 5) };
        using var iconBrush = new SolidBrush(iconColor);
        g.FillPolygon(iconBrush, arrow);

        using var numberFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        string number = "5";
        SizeF size = g.MeasureString(number, numberFont);
        g.DrawString(number, numberFont, iconBrush, rect.Left + (rect.Width - size.Width) / 2, rect.Top + (rect.Height - size.Height) / 2 + 1);
    }

    private void DrawPlayButton(Graphics g, Rectangle rect)
    {
        DrawButtonSurface(g, rect, emphasized: true);
        using var iconBrush = new SolidBrush(Color.FromArgb(45, 103, 230));
        if (IsPlaying)
        {
            int barWidth = 7;
            int barHeight = 28;
            int top = rect.Top + (rect.Height - barHeight) / 2;
            g.FillRectangle(iconBrush, rect.Left + rect.Width / 2 - 12, top, barWidth, barHeight);
            g.FillRectangle(iconBrush, rect.Left + rect.Width / 2 + 5, top, barWidth, barHeight);
        }
        else
        {
            PointF[] triangle =
            {
                new(rect.Left + rect.Width * 0.42F, rect.Top + rect.Height * 0.30F),
                new(rect.Left + rect.Width * 0.42F, rect.Top + rect.Height * 0.70F),
                new(rect.Left + rect.Width * 0.70F, rect.Top + rect.Height * 0.50F)
            };
            g.FillPolygon(iconBrush, triangle);
        }
    }

    private void DrawButtonSurface(Graphics g, Rectangle rect, bool emphasized)
    {
        bool hover = rect.Contains(PointToClient(MousePosition));
        using var fill = new SolidBrush(hover ? Color.FromArgb(239, 245, 255) : Color.White);
        using var border = new Pen(emphasized ? Color.FromArgb(134, 170, 245) : Color.FromArgb(221, 228, 239), emphasized ? 2.2F : 1.2F);
        using var shadow = new SolidBrush(Color.FromArgb(22, 22, 44, 82));
        Rectangle shadowRect = new(rect.Left + 1, rect.Top + 3, rect.Width, rect.Height);
        g.FillEllipse(shadow, shadowRect);
        g.FillEllipse(fill, rect);
        g.DrawEllipse(border, rect);
    }

    private void DrawStatusPills(Graphics g, Rectangle card)
    {
        int y = card.Bottom - 48;
        int sourceWidth = Math.Min(160, Math.Max(125, card.Width / 2 - 18));
        Rectangle source = new(card.Left + 16, y, sourceWidth, 34);
        Rectangle fps = new(card.Right - 112, y, 96, 34);
        DrawPill(g, source, SourceText, showStatusDot: false);
        DrawPill(g, fps, FpsText, showStatusDot: true);
    }

    private void DrawPill(Graphics g, Rectangle rect, string text, bool showStatusDot)
    {
        using var fill = new SolidBrush(Color.FromArgb(248, 250, 253));
        using var border = new Pen(Color.FromArgb(219, 226, 237), 1F);
        using var textBrush = new SolidBrush(Color.FromArgb(31, 58, 101));
        FillRoundedRectangle(g, fill, rect, 12);
        DrawRoundedRectangle(g, border, rect, 12);

        int iconSpace = showStatusDot ? 0 : 20;
        if (!showStatusDot)
        {
            using var cameraPen = new Pen(Color.FromArgb(45, 103, 230), 2F);
            Rectangle body = new(rect.Left + 11, rect.Top + 11, 13, 12);
            g.DrawRectangle(cameraPen, body);
            g.DrawLine(cameraPen, body.Right, body.Top + 3, body.Right + 6, body.Top);
            g.DrawLine(cameraPen, body.Right + 6, body.Top, body.Right + 6, body.Bottom);
            g.DrawLine(cameraPen, body.Right + 6, body.Bottom, body.Right, body.Bottom - 3);
        }

        using var pillFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        Rectangle textRect = new(rect.Left + 10 + iconSpace, rect.Top, rect.Width - 20 - iconSpace - (showStatusDot ? 13 : 0), rect.Height);
        TextRenderer.DrawText(g, text, pillFont, textRect, textBrush.Color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (showStatusDot)
        {
            using var dot = new SolidBrush(Color.FromArgb(29, 165, 125));
            g.FillEllipse(dot, rect.Right - 17, rect.Top + 13, 8, 8);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_timelineRect.Contains(e.Location))
        {
            _draggingTimeline = true;
            RequestSeekFromPoint(e.X);
            return;
        }

        if (_buttonRects[0].Contains(e.Location))
        {
            PreviousClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (_buttonRects[1].Contains(e.Location))
        {
            PlayPauseClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (_buttonRects[2].Contains(e.Location))
        {
            NextClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _timelineRect.Contains(e.Location) || _buttonRects.Any(rect => rect.Contains(e.Location))
            ? Cursors.Hand
            : Cursors.Default;
        if (_draggingTimeline)
        {
            RequestSeekFromPoint(e.X);
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _draggingTimeline = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
        _draggingTimeline = false;
        Invalidate();
    }

    private void RequestSeekFromPoint(int x)
    {
        if (Maximum <= Minimum)
        {
            return;
        }

        double ratio = Math.Clamp((x - _timelineRect.Left) / (double)Math.Max(1, _timelineRect.Width), 0, 1);
        int requested = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
        SeekRequested?.Invoke(this, requested);
    }

    private static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
    {
        using GraphicsPath path = RoundedPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using GraphicsPath path = RoundedPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
