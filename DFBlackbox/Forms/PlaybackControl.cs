namespace DFBlackbox.Forms;

public sealed class PlaybackControl : Control
{
    private readonly Rectangle[] _buttonRects = new Rectangle[3];
    private Rectangle _timelineRect;
    private bool _draggingTimeline;

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

    public PlaybackControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Black;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        MinimumSize = new Size(360, 116);
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Black);

        int contentWidth = Math.Min(Width - 40, 560);
        int left = (Width - contentWidth) / 2;
        _timelineRect = new Rectangle(left, 24, contentWidth, 18);
        DrawTimeline(g);
        DrawTimeLabels(g, left, contentWidth);
        DrawButtons(g, left, contentWidth);
    }

    private void DrawTimeline(Graphics g)
    {
        Rectangle outer = new Rectangle(_timelineRect.Left, _timelineRect.Top, _timelineRect.Width, _timelineRect.Height);
        Rectangle inner = Rectangle.Inflate(outer, -5, -6);
        int progressWidth = Maximum <= Minimum
            ? 0
            : (int)Math.Round(inner.Width * ((Value - Minimum) / (double)(Maximum - Minimum)));

        using var outerPen = new Pen(Color.FromArgb(210, 230, 230, 230), 2F);
        using var innerBack = new SolidBrush(Color.FromArgb(48, 255, 255, 255));
        using var progressBrush = new SolidBrush(Color.FromArgb(230, 245, 245, 245));
        DrawRoundedRectangle(g, outerPen, outer, outer.Height / 2);
        FillRoundedRectangle(g, innerBack, inner, inner.Height / 2);
        if (progressWidth > 0)
        {
            Rectangle progress = new Rectangle(inner.Left, inner.Top, Math.Max(3, progressWidth), inner.Height);
            FillRoundedRectangle(g, progressBrush, progress, progress.Height / 2);
        }
    }

    private void DrawTimeLabels(Graphics g, int left, int contentWidth)
    {
        using var textBrush = new SolidBrush(Color.FromArgb(235, 245, 245, 245));
        using var smallFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        g.DrawString(CurrentTimeText, smallFont, textBrush, left + 12, 48);
        SizeF totalSize = g.MeasureString(TotalTimeText, smallFont);
        g.DrawString(TotalTimeText, smallFont, textBrush, left + contentWidth - totalSize.Width - 12, 48);
    }

    private void DrawButtons(Graphics g, int left, int contentWidth)
    {
        int centerX = left + contentWidth / 2;
        int y = 72;
        _buttonRects[0] = new Rectangle(centerX - 102, y, 46, 46);
        _buttonRects[1] = new Rectangle(centerX - 23, y - 6, 56, 56);
        _buttonRects[2] = new Rectangle(centerX + 66, y, 46, 46);

        DrawCircleButton(g, _buttonRects[0], "<<", "[ - ]", 10F, 8F);
        DrawCircleButton(g, _buttonRects[1], IsPlaying ? "||" : ">", "[ / ]", 16F, 9F);
        DrawCircleButton(g, _buttonRects[2], ">>", "[ + ]", 10F, 8F);
    }

    private void DrawCircleButton(Graphics g, Rectangle rect, string symbol, string hint, float symbolSize, float hintSize)
    {
        using var fill = new SolidBrush(Color.FromArgb(155, 130, 130, 130));
        using var hoverFill = new SolidBrush(Color.FromArgb(190, 160, 160, 160));
        using var textBrush = new SolidBrush(Color.White);
        bool mouseOver = rect.Contains(PointToClient(MousePosition));
        g.FillEllipse(mouseOver ? hoverFill : fill, rect);

        using var symbolFont = new Font("Segoe UI", symbolSize, FontStyle.Bold);
        using var hintFont = new Font("Segoe UI", hintSize, FontStyle.Bold);
        SizeF symbolSizeMeasured = g.MeasureString(symbol, symbolFont);
        SizeF hintSizeMeasured = g.MeasureString(hint, hintFont);
        int gap = rect.Height >= 54 ? 1 : 0;
        float totalHeight = symbolSizeMeasured.Height + hintSizeMeasured.Height + gap;
        float top = rect.Top + (rect.Height - totalHeight) / 2 - 1;
        g.DrawString(
            symbol,
            symbolFont,
            textBrush,
            rect.Left + (rect.Width - symbolSizeMeasured.Width) / 2,
            top);
        g.DrawString(
            hint,
            hintFont,
            textBrush,
            rect.Left + (rect.Width - hintSizeMeasured.Width) / 2,
            top + symbolSizeMeasured.Height + gap - 2);
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
        Cursor = _timelineRect.Contains(e.Location)
            || _buttonRects.Any(rect => rect.Contains(e.Location))
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

        Rectangle inner = Rectangle.Inflate(_timelineRect, -5, -6);
        double ratio = Math.Clamp((x - inner.Left) / (double)Math.Max(1, inner.Width), 0, 1);
        int requested = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
        SeekRequested?.Invoke(this, requested);
    }

    private static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
    {
        using var path = RoundedPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = RoundedPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
