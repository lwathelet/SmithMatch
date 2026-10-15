using System.Drawing.Drawing2D;

namespace SmithMatch;

/// <summary>VSWR versus frequency of the matched network, over a range chosen by the user.</summary>
public class SweepForm : Form
{
    readonly Func<double, double?> vswrAt;   // VSWR at a frequency (null when there is no datapoint)
    readonly Func<double> designFreq;
    readonly TextBox txtStart = new() { Width = 80 };
    readonly TextBox txtStop = new() { Width = 80 };
    readonly TextBox txtMax = new() { Width = 50, Text = "5" };
    readonly PlotPanel plot = new() { Dock = DockStyle.Fill };

    public SweepForm(Func<double, double?> vswrAt, Func<double> designFreq)
    {
        this.vswrAt = vswrAt;
        this.designFreq = designFreq;
        Text = "VSWR sweep";
        Width = 800; Height = 450;
        Font = new Font("Segoe UI", 9f);
        double f0 = designFreq();
        txtStart.Text = Eng.Format(f0 * 0.8);
        txtStop.Text = Eng.Format(f0 * 1.2);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
        bar.Controls.Add(new Label { Text = "Start [Hz]", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        bar.Controls.Add(txtStart);
        bar.Controls.Add(new Label { Text = "Stop [Hz]", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
        bar.Controls.Add(txtStop);
        bar.Controls.Add(new Label { Text = "VSWR max", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
        bar.Controls.Add(txtMax);
        var btnCenter = new Button { Text = "±20 % around f", AutoSize = true };
        btnCenter.Click += (_, _) => { double f = designFreq(); txtStart.Text = Eng.Format(f * 0.8); txtStop.Text = Eng.Format(f * 1.2); };
        bar.Controls.Add(btnCenter);

        Controls.Add(plot);
        Controls.Add(bar);
        foreach (var tb in new[] { txtStart, txtStop, txtMax }) tb.TextChanged += (_, _) => RefreshPlot();
        plot.Paint += (_, e) => Draw(e.Graphics);
        plot.MouseMove += (_, e) => { plot.MouseX = e.X; plot.Invalidate(); };
        plot.MouseLeave += (_, _) => { plot.MouseX = -1; plot.Invalidate(); };
    }

    public void RefreshPlot() => plot.Invalidate();

    class PlotPanel : Panel
    {
        public int MouseX = -1;
        public PlotPanel() { DoubleBuffered = true; BackColor = Color.White; ResizeRedraw = true; }
    }

    /// <summary>Current range (start, stop, VSWR max), or null when the fields are not valid.</summary>
    public (double f1, double f2, double vmax)? Range =>
        Eng.TryParse(txtStart.Text, out double f1) && Eng.TryParse(txtStop.Text, out double f2) && f1 > 0 && f2 > f1
            ? (f1, f2, Eng.TryParse(txtMax.Text, out var vm) && vm > 1 ? vm : 5)
            : null;

    void Draw(Graphics g)
    {
        if (Range is not var (f1, f2, vmax))
        {
            using var font = new Font("Segoe UI", 8.5f);
            g.DrawString("Enter a valid range (e.g. 2G to 3G).", font, Brushes.Red, 10, 10);
            return;
        }
        DrawSweep(g, new RectangleF(0, 0, plot.Width, plot.Height), f1, f2, vmax, vswrAt, designFreq(), plot.MouseX);
    }

    /// <summary>Draws the VSWR curve in 'area' (used by the window and for printing).</summary>
    public static void DrawSweep(Graphics g, RectangleF area, double f1, double f2, double vmax,
                                 Func<double, double?> vswrAt, double f0, int mouseX = -1)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 8.5f);
        var r = new RectangleF(area.Left + 55, area.Top + 15, area.Width - 75, area.Height - 50);
        if (r.Width < 50 || r.Height < 50) return;

        float X(double f) => (float)(r.Left + (f - f1) / (f2 - f1) * r.Width);
        float Y(double v) => (float)(r.Bottom - (Math.Min(v, vmax) - 1) / (vmax - 1) * r.Height);

        // grid
        using var grid = new Pen(Color.FromArgb(225, 225, 225));
        for (int i = 0; i <= 10; i++)
        {
            double f = f1 + (f2 - f1) * i / 10;
            g.DrawLine(grid, X(f), r.Top, X(f), r.Bottom);
            if (i % 2 == 0) g.DrawString(Eng.Format(f) + "Hz", font, Brushes.Black, X(f) - 22, r.Bottom + 4);
        }
        double step = vmax <= 3 ? 0.25 : vmax <= 6 ? 0.5 : 1;
        for (double v = 1; v <= vmax + 1e-9; v += step)
        {
            g.DrawLine(grid, r.Left, Y(v), r.Right, Y(v));
            g.DrawString(v.ToString("0.##"), font, Brushes.Black, r.Left - 32, Y(v) - 7);
        }
        g.DrawRectangle(Pens.Gray, r.X, r.Y, r.Width, r.Height);
        // VSWR 2 reference line
        if (vmax > 2)
        {
            using var ref2 = new Pen(Color.Orange, 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(ref2, r.Left, Y(2), r.Right, Y(2));
        }
        // design frequency
        if (f0 >= f1 && f0 <= f2)
        {
            using var p0 = new Pen(Color.Gray) { DashStyle = DashStyle.Dot };
            g.DrawLine(p0, X(f0), r.Top, X(f0), r.Bottom);
        }

        // curve
        int n = Math.Max(2, (int)r.Width);
        var pts = new List<PointF>();
        double best = double.MaxValue, bestF = 0;
        for (int i = 0; i <= n; i++)
        {
            double f = f1 + (f2 - f1) * i / n;
            var v = vswrAt(f);
            if (v == null) { g.DrawString("Add a datapoint first.", font, Brushes.Red, r.Left + 10, r.Top + 10); return; }
            if (v < best) { best = v.Value; bestF = f; }
            pts.Add(new PointF(X(f), Y(double.IsInfinity(v.Value) ? vmax : v.Value)));
        }
        var oldClip = g.Clip;
        g.SetClip(r);
        using var pen = new Pen(Color.Blue, 2);
        g.DrawLines(pen, pts.ToArray());
        g.Clip = oldClip;
        g.DrawString($"Minimum VSWR {best:0.00} at {Eng.Format(bestF)}Hz", font, Brushes.Blue, r.Left + 6, r.Top + 4);

        // cursor readout
        if (mouseX >= r.Left && mouseX <= r.Right)
        {
            double f = f1 + (mouseX - r.Left) / r.Width * (f2 - f1);
            var v = vswrAt(f) ?? 0;
            g.DrawLine(Pens.Black, mouseX, r.Top, mouseX, r.Bottom);
            g.FillEllipse(Brushes.Red, mouseX - 3, Y(v) - 3, 6, 6);
            string t = $"{Eng.Format(f)}Hz   VSWR {v:0.00}";
            float tx = Math.Min(mouseX + 6, r.Right - 150);
            g.FillRectangle(Brushes.White, tx, r.Top + 22, 150, 16);
            g.DrawString(t, font, Brushes.Black, tx, r.Top + 22);
        }
    }
}
