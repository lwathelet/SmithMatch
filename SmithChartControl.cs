using System.Drawing.Drawing2D;
using System.Numerics;

namespace SmithMatch;

/// <summary>Draws an impedance (red) / admittance (blue) Smith chart, datapoints and element traces.</summary>
public class SmithChartControl : Control
{
    public double Z0 = 50;
    public bool ShowImpedance = true;
    public bool ShowAdmittance = true;
    public bool ShowLabels = true;     // grid values
    public bool ShowTraces = true;     // component traces and TP points
    public bool ShowHints = true;      // +/− arrows
    public bool ShowPoints = true;     // datapoints
    public bool ShowInfo = true;       // name / VSWR box
    public float Zoom = 1f;

    public List<DataPoint> Points = new();
    public List<List<Complex>> Traces = new();   // one polyline per element
    public List<Complex> ChainPoints = new();
    public string InfoText = "";   // shown in the top-left corner (VSWR of the network)
    public List<(List<Complex> path, string label)> Hints = new();  // arrows: effect of increasing (+) / decreasing (−) a value    // impedance after each element
    public Complex? CursorGamma;                      // reflection coefficient under the mouse
    public (Complex center, double radius)? Locus;   // circle (in Γ plane) followed by the component being placed; radius 0 = real axis

    public event Action<Complex> GammaHover;     // Γ under mouse
    public event Action<Complex> GammaClick;     // Γ on left click

    static readonly double[] RValues = { 0.2, 0.5, 1, 2, 5, 10 };
    static readonly double[] RMinor = { 0.1, 0.3, 0.4, 0.6, 0.7, 0.8, 0.9, 1.5, 3, 4, 20 };
    static readonly double[] XValues = { 0.2, 0.5, 1, 2, 5, 10 };
    static readonly double[] XMinor = { 0.1, 0.3, 0.4, 0.6, 0.7, 0.8, 0.9, 1.5, 3, 4, 20 };

    static readonly Color[] TraceColors = { Color.Magenta, Color.DarkOrange, Color.Green, Color.Purple, Color.Teal, Color.Brown };

    public SmithChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    float Radius => Math.Min(Width, Height) / 2f * 0.85f * Zoom;
    PointF Center => new(Width / 2f, Height / 2f);

    public PointF ToScreen(Complex g) => new((float)(Center.X + g.Real * Radius), (float)(Center.Y - g.Imaginary * Radius));
    public Complex ToGamma(Point p) => new((p.X - Center.X) / Radius, -(p.Y - Center.Y) / Radius);

    RectangleF CircleRect(double cx, double cy, double r)
    {
        var c = ToScreen(new Complex(cx, cy));
        float rr = (float)(r * Radius);
        return new RectangleF(c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var g = ToGamma(e.Location);
        if (g.Magnitude <= 1) { CursorGamma = g; GammaHover?.Invoke(g); } else CursorGamma = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var g = ToGamma(e.Location);
        if (e.Button == MouseButtons.Left && g.Magnitude < 1) GammaClick?.Invoke(g);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Zoom = Math.Clamp(Zoom * (e.Delta > 0 ? 1.1f : 1 / 1.1f), 0.3f, 10f);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var unit = CircleRect(0, 0, 1);
        using var clip = new GraphicsPath();
        clip.AddEllipse(unit);

        var state = g.Save();
        g.SetClip(clip);

        using var redMinor = new Pen(Color.FromArgb(255, 232, 232), 1);
        using var red = new Pen(Color.FromArgb(245, 170, 170), 1);
        using var blueMinor = new Pen(Color.FromArgb(232, 240, 255), 1);
        using var blue = new Pen(Color.FromArgb(165, 200, 245), 1);

        if (ShowAdmittance)
        {
            foreach (var v in RMinor) DrawG(g, blueMinor, v);
            foreach (var v in XMinor) DrawB(g, blueMinor, v);
            foreach (var v in RValues) DrawG(g, blue, v);
            foreach (var v in XValues) DrawB(g, blue, v);
        }
        if (ShowImpedance)
        {
            foreach (var v in RMinor) DrawR(g, redMinor, v);
            foreach (var v in XMinor) DrawX(g, redMinor, v);
            foreach (var v in RValues) DrawR(g, red, v);
            foreach (var v in XValues) DrawX(g, red, v);
            g.DrawLine(red, ToScreen(-1), ToScreen(1));
        }
        g.Restore(state);

        using var outer = new Pen(Color.Black, 2);
        g.DrawEllipse(outer, unit);

        if (Locus is var (lc, lr))
        {
            using var lp = new Pen(Color.FromArgb(0, 160, 0), 2.5f) { DashStyle = DashStyle.Dash };
            var st = g.Save();
            g.SetClip(clip);
            if (lr <= 0) g.DrawLine(lp, ToScreen(-1), ToScreen(1));
            else g.DrawEllipse(lp, CircleRect(lc.Real, lc.Imaginary, lr));
            g.Restore(st);
        }

        if (ShowLabels) DrawLabels(g);
        if (ShowTraces) DrawTraces(g);
        if (ShowHints) DrawHints(g);
        if (ShowPoints) DrawPoints(g);

        if (ShowInfo && InfoText.Length > 0)
        {
            using var f = new Font("Segoe UI", 13f, FontStyle.Bold);
            var size = g.MeasureString(InfoText, f);
            using var bg = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.FillRectangle(bg, 8, 8, size.Width + 8, size.Height + 4);
            g.DrawRectangle(Pens.LightGray, 8, 8, size.Width + 8, size.Height + 4);
            g.DrawString(InfoText, f, Brushes.Black, 12, 10);
        }

        if (CursorGamma is Complex c)
        {
            using var cp = new Pen(Color.Gray, 1) { DashStyle = DashStyle.Dot };
            double m = c.Magnitude;
            g.DrawEllipse(cp, CircleRect(0, 0, m)); // constant VSWR circle
        }
    }

    // Impedance grid: constant r circle center (r/(1+r),0) radius 1/(1+r)
    void DrawR(Graphics g, Pen p, double r) => g.DrawEllipse(p, CircleRect(r / (1 + r), 0, 1 / (1 + r)));
    // constant x circle center (1, 1/x) radius 1/x
    void DrawX(Graphics g, Pen p, double x)
    {
        g.DrawEllipse(p, CircleRect(1, 1 / x, 1 / x));
        g.DrawEllipse(p, CircleRect(1, -1 / x, 1 / x));
    }
    // Admittance grid is the impedance grid mirrored through the origin
    void DrawG(Graphics g, Pen p, double r) => g.DrawEllipse(p, CircleRect(-r / (1 + r), 0, 1 / (1 + r)));
    void DrawB(Graphics g, Pen p, double x)
    {
        g.DrawEllipse(p, CircleRect(-1, 1 / x, 1 / x));
        g.DrawEllipse(p, CircleRect(-1, -1 / x, 1 / x));
    }

    void DrawLabels(Graphics g)
    {
        using var f = new Font("Segoe UI", 8f);
        using var redB = new SolidBrush(Color.FromArgb(220, 20, 60));
        using var blueB = new SolidBrush(Color.FromArgb(30, 120, 230));
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        if (ShowImpedance)
        {
            foreach (var r in RValues)
            {
                var p = ToScreen(Rf.Gamma(r, 1));
                g.DrawString(Eng.Format(r * Z0), f, redB, p.X + 2, p.Y - 14);
            }
            foreach (var x in XValues)
            {
                foreach (int s in new[] { 1, -1 })
                {
                    var gm = Rf.Gamma(new Complex(0, s * x), 1);
                    var p = ToScreen(gm * 1.07);
                    g.DrawString(Eng.Format(x * Z0), f, redB, p, sf);
                }
            }
        }
        if (ShowAdmittance)
        {
            double y0 = 1 / Z0;
            foreach (var r in RValues)
            {
                var p = ToScreen(-Rf.Gamma(r, 1));
                g.DrawString(Eng.Format(r * y0), f, blueB, p.X + 2, p.Y + 2);
            }
            foreach (var x in XValues)
            {
                foreach (int s in new[] { 1, -1 })
                {
                    var gm = -Rf.Gamma(new Complex(0, s * x), 1);
                    var p = ToScreen(gm * 1.14);
                    g.DrawString(Eng.Format(x * y0) + "S", f, blueB, p, sf);
                }
            }
        }
    }

    void DrawTraces(Graphics g)
    {
        for (int i = 0; i < Traces.Count; i++)
        {
            var pts = Traces[i].Select(z => ToScreen(Rf.Gamma(z, Z0))).ToArray();
            if (pts.Length < 2) continue;
            using var p = new Pen(TraceColors[i % TraceColors.Length], 2.5f);
            g.DrawLines(p, pts);
        }
        using var tf = new Font("Segoe UI", 8f, FontStyle.Bold);
        for (int i = 0; i < ChainPoints.Count; i++)
        {
            var p = ToScreen(Rf.Gamma(ChainPoints[i], Z0));
            g.FillEllipse(Brushes.Black, p.X - 3, p.Y - 3, 6, 6);
            g.DrawString("TP" + (i + 1), tf, Brushes.Black, p.X + 5, p.Y - 14);
        }
    }

    const float HintLength = 45;   // arrow length in pixels

    /// <summary>Keeps the start of a polyline up to the given length (cutting the last segment).</summary>
    static PointF[] TrimToLength(List<PointF> pts, float length)
    {
        var res = new List<PointF> { pts[0] };
        float done = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1]; var b = pts[i];
            float d = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (done + d >= length)
            {
                float t = (length - done) / d;
                res.Add(new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                return res.ToArray();
            }
            done += d;
            res.Add(b);
        }
        return res.ToArray();
    }

        void DrawHints(Graphics g)
    {
        using var f = new Font("Segoe UI", 8f, FontStyle.Bold);
        for (int i = 0; i < Hints.Count; i++)
        {
            var (path, label) = Hints[i];
            var pts = TrimToLength(path.Select(z => ToScreen(Rf.Gamma(z, Z0))).ToList(), HintLength);
            if (pts.Length < 2) continue;
            var a = pts[^2];
            var b = pts[^1];
            if (Math.Abs(b.X - pts[0].X) + Math.Abs(b.Y - pts[0].Y) < 4) continue;   // value has no visible effect
            bool up = label.EndsWith("+");
            var color = up ? Color.FromArgb(0, 150, 0) : Color.FromArgb(200, 0, 0);
            using var pen = new Pen(color, 2f) { CustomEndCap = new AdjustableArrowCap(4, 5) };
            g.DrawLines(pen, pts);
            using var br = new SolidBrush(color);
            g.DrawString(label, f, br, b.X + 4, b.Y - 6);
        }
    }

        void DrawPoints(Graphics g)
    {
        using var f = new Font("Segoe UI", 9f);
        using var green = new SolidBrush(Color.Green);
        for (int i = 0; i < Points.Count; i++)
        {
            var p = ToScreen(Rf.Gamma(Points[i].Z, Z0));
            g.DrawRectangle(Pens.Black, p.X - 4, p.Y - 4, 8, 8);
            g.DrawString("DP " + (i + 1), f, green, p.X + 4, p.Y + 2);
        }
    }
}
