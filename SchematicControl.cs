using System.Drawing.Drawing2D;

namespace SmithMatch;

/// <summary>
/// Draws the pi network: load Z — shunt R/L/C (load side) — series R/L/C — shunt R/L/C (source side) — source.
/// Slots: index = position * 3 + kind, position 0 = load shunt, 1 = series, 2 = source shunt; kind 0 = R, 1 = L, 2 = C.
/// </summary>
public class SchematicControl : Control
{
    public Element[] Slots = new Element[9];
    public int SelectedSlot = -1;
    public string LoadText = "";
    public string ZinText = "";   // shown under the source (VSWR)

    public event Action<int> SlotClicked;        // left click
    public event Action<int> SlotDoubleClicked;  // double click
    public event Action<int> SlotRightClicked;   // right click

    readonly List<(RectangleF rect, int slot)> hits = new();

    const float RailY = 55, GndY = 145, Pitch = 72;

    public SchematicControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    int HitTest(Point p)
    {
        foreach (var (r, s) in hits) if (r.Contains(p)) return s;
        return -1;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int s = HitTest(e.Location);
        if (s < 0) return;
        if (e.Button == MouseButtons.Left) SlotClicked?.Invoke(s);
        else if (e.Button == MouseButtons.Right) SlotRightClicked?.Invoke(s);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        int s = HitTest(e.Location);
        if (s >= 0 && e.Button == MouseButtons.Left) SlotDoubleClicked?.Invoke(s);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        hits.Clear();

        using var pen = new Pen(Color.Black, 1.5f);
        using var font = new Font("Segoe UI", 8f);
        using var sel = new Pen(Color.DodgerBlue, 2) { DashStyle = DashStyle.Dash };

        float x = 30;
        // Load: vertical box between rail and ground
        g.DrawLine(pen, x, RailY, x, RailY + 20);
        g.DrawRectangle(pen, x - 7, RailY + 20, 14, 40);
        g.DrawString("Z", font, Brushes.Black, x + 9, RailY + 30);
        g.DrawLine(pen, x, RailY + 60, x, GndY);
        DrawGround(g, pen, x);
        g.DrawString(LoadText, font, Brushes.DarkGreen, 4, GndY + 12);

        float railStart = x;
        x += Pitch * 0.8f;

        // Load shunt
        for (int k = 0; k < 3; k++)
            if (Slots[k] != null) { DrawShunt(g, pen, font, sel, x, k); x += Pitch; }
        // Series (horizontal along the rail)
        float seriesStart = x;
        for (int k = 3; k < 6; k++)
            if (Slots[k] != null) { DrawSeries(g, pen, font, sel, x, k); x += Pitch; }
        g.DrawLine(pen, railStart, RailY, seriesStart - Pitch / 2, RailY);  // series elements draw their own leads (±Pitch/2)
        // Source shunt
        float afterSeries = x;
        for (int k = 6; k < 9; k++)
            if (Slots[k] != null) { DrawShunt(g, pen, font, sel, x, k); x += Pitch; }

        // Source: circle with ~, Zin arrow
        float sx = x + 10;
        g.DrawLine(pen, afterSeries - Pitch / 2, RailY, sx, RailY);
        g.DrawLine(pen, sx, RailY, sx, RailY + 32);
        g.DrawEllipse(pen, sx - 12, RailY + 32, 24, 24);
        g.DrawBezier(pen, sx - 7, RailY + 44, sx - 3, RailY + 36, sx + 3, RailY + 52, sx + 7, RailY + 44);
        g.DrawLine(pen, sx, RailY + 56, sx, GndY);
        DrawGround(g, pen, sx);
        g.DrawString("Zin", font, Brushes.Black, sx - 34, RailY + 14);
        g.DrawString(ZinText, font, Brushes.DarkBlue, sx - 40, GndY + 12);
        // bottom (ground) wire
        g.DrawLine(pen, railStart, GndY, sx, GndY);
    }

    void DrawGround(Graphics g, Pen pen, float x)
    {
        g.DrawLine(pen, x - 8, GndY + 4, x + 8, GndY + 4);
    }

    // vertical element between rail and ground
    void DrawShunt(Graphics g, Pen pen, Font font, Pen sel, float x, int slot)
    {
        float mid = (RailY + GndY) / 2;
        float y0 = mid - 20, y1 = mid + 20;
        g.FillEllipse(Brushes.Black, x - 3, RailY - 3, 6, 6);
        g.DrawLine(pen, x, RailY, x, y0);
        g.DrawLine(pen, x, y1, x, GndY);
        DrawSymbol(g, pen, slot % 3, new PointF(x, y0), new PointF(x, y1), vertical: true);
        Finish(g, font, sel, slot, new RectangleF(x - 18, RailY + 4, 36, GndY - RailY - 8), new PointF(x + 13, (RailY + GndY) / 2 - 7));
    }

    // horizontal element in the top rail
    void DrawSeries(Graphics g, Pen pen, Font font, Pen sel, float x, int slot)
    {
        float x0 = x - 20, x1 = x + 20;
        g.DrawLine(pen, x - Pitch / 2, RailY, x0, RailY);
        g.DrawLine(pen, x1, RailY, x + Pitch / 2, RailY);
        DrawSymbol(g, pen, slot % 3, new PointF(x0, RailY), new PointF(x1, RailY), vertical: false);
        Finish(g, font, sel, slot, new RectangleF(x - 26, RailY - 18, 52, 36), new PointF(x - 22, RailY - 32));
    }

    void Finish(Graphics g, Font font, Pen sel, int slot, RectangleF hit, PointF label)
    {
        hits.Add((hit, slot));
        if (slot == SelectedSlot) g.DrawRectangle(sel, hit.X, hit.Y, hit.Width, hit.Height);
        var el = Slots[slot];
        g.DrawString(Eng.Format(el.Value) + el.Unit, font, Brushes.Black, label);
        if (Kits.PartNumber(el.Type, el.Value) is string pn)
            g.DrawString(pn, font, Brushes.Gray, label.X, label.Y + font.Height);
        if (el.HasParasitics)   // parasitics estimated from measurements
            g.DrawString($"par {Eng.Format(el.ParC)}F {Eng.Format(el.ParL)}H", font, Brushes.DarkOrange,
                         label.X, slot / 3 == 1 ? label.Y - font.Height : label.Y + 2 * font.Height);   // series: above the value
    }

    static void DrawSymbol(Graphics g, Pen pen, int kind, PointF a, PointF b, bool vertical)
    {
        float len = vertical ? b.Y - a.Y : b.X - a.X;
        switch (kind)
        {
            case 0: // R: box
                if (vertical) g.DrawRectangle(pen, a.X - 6, a.Y + 2, 12, len - 4);
                else g.DrawRectangle(pen, a.X + 2, a.Y - 6, len - 4, 12);
                break;
            case 1: // L: four bumps
                for (int i = 0; i < 4; i++)
                {
                    float s = len / 4;
                    if (vertical) g.DrawArc(pen, a.X - s / 2, a.Y + i * s, s, s, 270, 180);
                    else g.DrawArc(pen, a.X + i * s, a.Y - s / 2, s, s, 180, 180);
                }
                break;
            case 2: // C: two plates
                float m = len / 2, gap = 4;
                if (vertical)
                {
                    g.DrawLine(pen, a.X, a.Y, a.X, a.Y + m - gap);
                    g.DrawLine(pen, a.X, a.Y + m + gap, a.X, b.Y);
                    g.DrawLine(pen, a.X - 12, a.Y + m - gap, a.X + 12, a.Y + m - gap);
                    g.DrawLine(pen, a.X - 12, a.Y + m + gap, a.X + 12, a.Y + m + gap);
                }
                else
                {
                    g.DrawLine(pen, a.X, a.Y, a.X + m - gap, a.Y);
                    g.DrawLine(pen, a.X + m + gap, a.Y, b.X, a.Y);
                    g.DrawLine(pen, a.X + m - gap, a.Y - 12, a.X + m - gap, a.Y + 12);
                    g.DrawLine(pen, a.X + m + gap, a.Y - 12, a.X + m + gap, a.Y + 12);
                }
                break;
        }
    }
}
