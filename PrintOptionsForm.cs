using System.Drawing.Printing;

namespace SmithMatch;

/// <summary>What to print (kept for the whole session).</summary>
public class PrintOptions
{
    public bool Chart = true, ZGrid = true, YGrid = true, Labels = true, Traces = true, Arrows = true, Points = true, InfoBox = true;
    public bool ChartOwnPage = false, Landscape = false;
    public bool Schematic = true, Datapoints = true, Components = true, Results = true, Sweep = true;

    public PrintOptions Clone() => (PrintOptions)MemberwiseClone();
}

/// <summary>Dialog to choose what to print, with a live page preview (in the style of the Smith program).</summary>
public class PrintOptionsForm : Form
{
    readonly PrintOptions o;
    readonly PrintOptions original;
    readonly PrintDocument doc;
    internal readonly PrintPreviewControl preview = new() { Dock = DockStyle.Fill, AutoZoom = true, UseAntiAlias = true };

    /// <summary>True when the user clicked Print (the caller then asks for the printer).</summary>
    public bool PrintRequested { get; private set; }

    public PrintOptionsForm(PrintOptions options, PrintDocument document)
    {
        o = options;
        original = options.Clone();
        doc = document;
        Text = "Print";
        Icon = Branding.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        Size = new Size(1100, 760);

        var left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(8) };

        var gChart = new GroupBox { Text = "Smith Chart", Dock = DockStyle.Top, Height = 300 };
        var chartBox = Add(gChart, "Smith Chart", o.Chart, v => o.Chart = v, 10, 22);
        var sub = new[]
        {
            Add(gChart, "Z-Chart (impedance)", o.ZGrid, v => o.ZGrid = v, 30, 48),
            Add(gChart, "Y-Chart (admittance)", o.YGrid, v => o.YGrid = v, 30, 72),
            Add(gChart, "Labels", o.Labels, v => o.Labels = v, 30, 96),
            Add(gChart, "Circuit elements", o.Traces, v => o.Traces = v, 30, 120),
            Add(gChart, "+/− arrows", o.Arrows, v => o.Arrows = v, 30, 144),
            Add(gChart, "Datapoints", o.Points, v => o.Points = v, 30, 168),
            Add(gChart, "Name / VSWR box", o.InfoBox, v => o.InfoBox = v, 30, 192),
            Add(gChart, "Chart alone on page 1", o.ChartOwnPage, v => o.ChartOwnPage = v, 30, 230),
        };
        void Enable() { foreach (var b in sub) b.Enabled = chartBox.Checked; }
        chartBox.CheckedChanged += (_, _) => Enable();
        Enable();
        Add(gChart, "Landscape page", o.Landscape, v => o.Landscape = v, 10, 262);

        var gOther = new GroupBox { Text = "Sections", Dock = DockStyle.Top, Height = 160 };
        Add(gOther, "Schematic", o.Schematic, v => o.Schematic = v, 10, 22);
        Add(gOther, "Datapoints", o.Datapoints, v => o.Datapoints = v, 10, 46);
        Add(gOther, "Components (Würth refs)", o.Components, v => o.Components = v, 10, 70);
        Add(gOther, "Results (Zin, VSWR, RL)", o.Results, v => o.Results = v, 10, 94);
        Add(gOther, "VSWR sweep graph", o.Sweep, v => o.Sweep = v, 10, 118);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        var print = new Button { Text = "Print...", Width = 90, Height = 30 };
        print.Click += (_, _) => { PrintRequested = true; DialogResult = DialogResult.OK; };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(print);

        left.Controls.Add(gOther);
        left.Controls.Add(gChart);   // docked top: added last = shown first
        left.Controls.Add(buttons);

        Controls.Add(preview);
        Controls.Add(left);
        CancelButton = cancel;
        AcceptButton = print;

        FormClosed += (_, _) => { if (!PrintRequested) CopyFrom(original); };
        Shown += (_, _) => Refresh_();
    }

    void CopyFrom(PrintOptions src)
    {
        foreach (var f in typeof(PrintOptions).GetFields()) f.SetValue(o, f.GetValue(src));
    }

    void Refresh_()
    {
        doc.DefaultPageSettings.Landscape = o.Landscape;
        preview.Columns = o.Chart && o.ChartOwnPage ? 2 : 1;
        preview.Document = doc;
        preview.InvalidatePreview();
    }

    CheckBox Add(Control parent, string text, bool value, Action<bool> set, int x, int y)
    {
        var b = new CheckBox { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y) };
        b.CheckedChanged += (_, _) => { set(b.Checked); Refresh_(); };
        parent.Controls.Add(b);
        return b;
    }
}
