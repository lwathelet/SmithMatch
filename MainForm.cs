using System.Numerics;

namespace SmithMatch;

public class MainForm : Form
{
    readonly SmithChartControl chart = new() { Dock = DockStyle.Fill };
    readonly ListView dpList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false };
    readonly SchematicControl schematic = new() { Dock = DockStyle.Fill };
    readonly TrackBar tune = new() { Dock = DockStyle.Bottom, Minimum = -100, Maximum = 100, TickFrequency = 10 };
    readonly TextBox txtZ0 = new() { Width = 60, Text = "50" };
    readonly TextBox txtFreq = new() { Width = 80, Text = "2.45G" };
    readonly CheckBox chkKit = new() { Text = "Würth kit values only", Appearance = Appearance.Button, AutoSize = true };
    readonly CheckBox chkMouse = new() { Text = "Mouse adds DP", Appearance = Appearance.Button, AutoSize = true, Checked = true };
    readonly Dictionary<string, TextBox> cursorBoxes = new();
    readonly Label lblResult = new() { Dock = DockStyle.Bottom, Height = 40, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

    Element[] slots = new Element[9];   // see SchematicControl: position * 3 + kind (R, L, C)
    (double C, double L)[] slotPar = new (double, double)[9];   // parasitics of each position (from a measurement fit)
    List<(double value, Complex z)>[] slotMeas = new List<(double, Complex)>[9];   // measurements used for the fit
    IEnumerable<Element> Elements => slots.Where(e => e != null);
    int selectedSlot = -1;
    string designPath;           // file of the current design (null = not saved yet)
    bool dirty;                  // unsaved changes
    bool loading;                // true while a design is being loaded
    int forcedStart = -1;        // start datapoint to restore after loading
    string DesignName => designPath == null ? "Untitled" : Path.GetFileNameWithoutExtension(designPath);
    SweepForm sweep;             // VSWR vs frequency window (null when closed)
    (double f1, double f2, double vmax)? sweepRange;   // last range used in the sweep window
    readonly PrintOptions printOptions = new();
    int printPage;               // page being printed (0 = first)
    int placingSlot = -1;        // slot whose value follows the mouse on its circle
    Element placingBackup;       // previous content of that slot, restored by ESC
    double tuneBase;     // element value when tuning starts
    bool updatingList;

    double Z0 => Eng.TryParse(txtZ0.Text, out var v) && v > 0 ? v : 50;
    double Freq => Eng.TryParse(txtFreq.Text, out var v) && v > 0 ? v : 2.45e9;

    public MainForm()
    {
        Text = "SmithMatch";
        Width = 1400; Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        Controls.Add(BuildBody());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());

        chart.GammaHover += g => { UpdateCursor(g); if (placingSlot >= 0) PlaceAt(g); };
        chart.GammaClick += g =>
        {
            if (placingSlot >= 0) FinishPlacing();
            else if (chkMouse.Checked) AddPoint(Rf.ZFromGamma(g, Z0));
        };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && placingSlot >= 0) { CancelPlacing(); e.Handled = true; } };
        txtZ0.TextChanged += (_, _) => Recalc();
        txtFreq.TextChanged += (_, _) => Recalc();
        FormClosing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
        Recalc();
        dirty = false;
        UpdateTitle();
    }

    MenuStrip BuildMenu()
    {
        var ms = new MenuStrip();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(new ToolStripMenuItem("New", null, (_, _) => NewDesign()) { ShortcutKeys = Keys.Control | Keys.N });
        file.DropDownItems.Add(new ToolStripMenuItem("Open...", null, (_, _) => OpenDesign()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem("Save", null, (_, _) => SaveDesign(false)) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add("Save as...", null, (_, _) => SaveDesign(true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Export image...", null, (_, _) => ExportImage());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Print...", null, (_, _) => Print(preview: false)) { ShortcutKeys = Keys.Control | Keys.P });
        file.DropDownItems.Add("Print preview...", null, (_, _) => Print(preview: true));   // same dialog: options + live preview
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());
        var view = new ToolStripMenuItem("View");
        var imp = new ToolStripMenuItem("Impedance grid") { Checked = true, CheckOnClick = true };
        imp.CheckedChanged += (_, _) => { chart.ShowImpedance = imp.Checked; chart.Invalidate(); };
        var adm = new ToolStripMenuItem("Admittance grid") { Checked = true, CheckOnClick = true };
        adm.CheckedChanged += (_, _) => { chart.ShowAdmittance = adm.Checked; chart.Invalidate(); };
        view.DropDownItems.Add(imp);
        view.DropDownItems.Add(adm);
        view.DropDownItems.Add("Zoom 1:1", null, (_, _) => { chart.Zoom = 1; chart.Invalidate(); });
        ms.Items.Add(file);
        ms.Items.Add(view);
        return ms;
    }

    ToolStrip BuildToolbar()
    {
        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4) };
        ts.Items.Add(new ToolStripControlHost(chkMouse));
        ts.Items.Add(new ToolStripButton("Keyboard DP...", null, (_, _) => KeyboardPoint()));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripLabel("Z0 [Ω]"));
        ts.Items.Add(new ToolStripControlHost(txtZ0));
        ts.Items.Add(new ToolStripLabel("Freq [Hz]"));
        ts.Items.Add(new ToolStripControlHost(txtFreq));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripControlHost(chkKit) { ToolTipText = $"L from {Kits.InductorKit}, C from {Kits.CapacitorKit}" });
        chkKit.CheckedChanged += KitChanged;
        ts.Items.Add(new ToolStripSeparator());
        string[] groups = { "Load", "Serie", "Source" };
        string[] kinds = { "R", "L", "C" };
        for (int pos = 0; pos < 3; pos++)
        {
            ts.Items.Add(new ToolStripLabel(groups[pos] + ":") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            for (int k = 1; k < 3; k++)   // L and C only (R is not used)
            {
                int slot = pos * 3 + k;
                ts.Items.Add(new ToolStripButton(kinds[k], null, (_, _) => StartPlacing(slot))
                {
                    ToolTipText = $"{groups[pos]} {kinds[k]}: move the mouse on the green circle, click to fix, ESC to cancel"
                });
            }
            ts.Items.Add(new ToolStripSeparator());
        }
        ts.Items.Add(new ToolStripButton("Auto-match", null, (_, _) => AutoMatch()) { ToolTipText = "List the L-network solutions that match the start DP to Z0" });
        ts.Items.Add(new ToolStripButton("VSWR sweep...", null, (_, _) => ShowSweep()) { ToolTipText = "VSWR versus frequency over a chosen range" });
        ts.Items.Add(new ToolStripButton("Parasitics...", null, (_, _) => ShowParasitics()) { ToolTipText = "Estimate the parasitic L and C from measurements" });
        return ts;
    }

    Control BuildBody()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 900, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(chart);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        // Datapoints
        dpList.Columns.Add("Start", 45);
        dpList.Columns.Add("Point", 50);
        dpList.Columns.Add("Z", 200);
        dpList.Columns.Add("Q", 60);
        dpList.Columns.Add("Freq", 80);
        dpList.ItemChecked += (_, e) =>
        {
            if (updatingList || !e.Item.Checked) { if (!updatingList) Recalc(); return; }
            updatingList = true;
            foreach (ListViewItem it in dpList.Items) if (it != e.Item) it.Checked = false;
            updatingList = false;
            Recalc();
        };
        var dpMenu = new ContextMenuStrip();
        dpMenu.Items.Add("Delete", null, (_, _) =>
        {
            if (dpList.SelectedIndices.Count == 0) return;
            chart.Points.RemoveAt(dpList.SelectedIndices[0]);
            Recalc();
        });
        dpList.ContextMenuStrip = dpMenu;
        right.Controls.Add(Group("Datapoints", dpList), 0, 0);

        // Schematic
        var elemPanel = new Panel { Dock = DockStyle.Fill };
        elemPanel.Controls.Add(schematic);
        elemPanel.Controls.Add(tune);
        elemPanel.Controls.Add(lblResult);
        schematic.SlotClicked += SelectSlot;
        schematic.SlotDoubleClicked += EditSlot;
        schematic.SlotRightClicked += slot =>
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Edit value...", null, (_, _) => EditSlot(slot));
            m.Items.Add("Remove", null, (_, _) => { if (placingSlot == slot) StopPlacing(); slots[slot] = null; if (selectedSlot == slot) selectedSlot = -1; Recalc(); });
            m.Show(Cursor.Position);
        };
        tune.Scroll += (_, _) =>
        {
            if (selectedSlot < 0 || slots[selectedSlot] == null) return;
            // ±100 steps = ×/÷ 2 (logarithmic tuning)
            var el = slots[selectedSlot];
            el.Value = chkKit.Checked
                ? Kits.Step(el.Type, tuneBase, tune.Value / 10)          // kit: one value per 10 slider steps
                : tuneBase * Math.Pow(2, tune.Value / 100.0);
            Recalc();
        };
        tune.MouseUp += (_, _) => { if (selectedSlot >= 0 && slots[selectedSlot] != null) tuneBase = slots[selectedSlot].Value; tune.Value = 0; };
        right.Controls.Add(Group("Schematic  —  click to select (slider tunes), double-click to edit, right-click to remove", elemPanel), 0, 1);

        // Cursor
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4 };
        string[] names = { "Return Loss", "VSWR", "Q", "Γ", "Y", "Z", "Zo", "Freq" };
        for (int i = 0; i < names.Length; i++)
        {
            var tb = new TextBox { ReadOnly = true, Width = 160 };
            cursorBoxes[names[i]] = tb;
            grid.Controls.Add(new Label { Text = names[i], AutoSize = true, Anchor = AnchorStyles.Right }, (i % 2) * 2, i / 2);
            grid.Controls.Add(tb, (i % 2) * 2 + 1, i / 2);
        }
        right.Controls.Add(Group("Cursor", grid), 0, 2);

        split.Panel2.Controls.Add(right);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, ClientSize.Width - 520);
        return split;
    }

    static GroupBox Group(string title, Control c)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill };
        g.Controls.Add(c);
        return g;
    }

    // ---------- actions ----------

    void AddPoint(Complex z)
    {
        chart.Points.Add(new DataPoint { Z = z, Freq = Freq });
        Recalc();
    }

    void KeyboardPoint()
    {
        using var dlg = new InputDialog("Keyboard datapoint", ("R [Ω]", "50"), ("X [Ω]", "0"));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (Eng.TryParse(dlg.Values[0], out var r) && Eng.TryParse(dlg.Values[1], out var x))
            AddPoint(new Complex(r, x));
        else
            MessageBox.Show(this, "Invalid number.");
    }

    static ElementType SlotType(int slot) => slot / 3 == 1
        ? new[] { ElementType.SeriesR, ElementType.SeriesL, ElementType.SeriesC }[slot % 3]
        : new[] { ElementType.ShuntR, ElementType.ShuntL, ElementType.ShuntC }[slot % 3];

    static int SlotOf(ElementType t, bool sourceSide) => t switch
    {
        ElementType.SeriesR => 3, ElementType.SeriesL => 4, ElementType.SeriesC => 5,
        ElementType.ShuntR => sourceSide ? 6 : 0,
        ElementType.ShuntL => sourceSide ? 7 : 1,
        _ => sourceSide ? 8 : 2,
    };

    // ---------- placing a component with the mouse ----------

    /// <summary>Impedance seen just before this slot (start DP through all earlier slots).</summary>
    Complex? ZBefore(int slot)
    {
        var start = StartPoint();
        if (start == null) return null;
        Complex z = start.Z;
        for (int i = 0; i < slot; i++) if (slots[i] != null) z = slots[i].Apply(z, Freq, 1);
        return z;
    }

    void StartPlacing(int slot)
    {
        if (placingSlot >= 0) CancelPlacing();
        var zb = ZBefore(slot);
        if (zb == null) { MessageBox.Show(this, "Add a datapoint first."); return; }
        placingSlot = slot;
        placingBackup = slots[slot]?.With(slots[slot].Value);
        slots[slot] ??= new Element { Type = SlotType(slot), Value = (slot % 3) switch { 0 => slot / 3 == 1 ? 1e-3 : 1e9, 1 => slot / 3 == 1 ? 1e-15 : 1e3, _ => slot / 3 == 1 ? 1e3 : 1e-18 } };
        SyncParasitics();
        selectedSlot = slot;

        // locus: series R → constant x, series L/C → constant r, shunt R → constant b, shunt L/C → constant g
        // (computed after the parasitics of this position: pad capacitance and series inductance)
        Complex z = slots[slot].ApplyParasitic(zb.Value, Freq, 1) / Z0, y = 1 / z;
        bool series = slot / 3 == 1, isR = slot % 3 == 0;
        double v = series ? (isR ? z.Imaginary : z.Real) : (isR ? y.Imaginary : y.Real);
        double sign = series ? 1 : -1;  // admittance circles are the impedance ones mirrored through the centre
        chart.Locus = isR
            ? (Math.Abs(v) < 1e-9 ? (Complex.Zero, 0) : (new Complex(sign, sign / v), 1 / Math.Abs(v)))
            : (new Complex(sign * v / (1 + v), 0), 1 / (1 + v));
        Recalc();
        lblResult.Text = "Move the mouse along the green circle, click to fix the value, ESC to cancel.";
    }

    void PlaceAt(Complex gamma)
    {
        if (slots[placingSlot] == null) { StopPlacing(); return; }   // slot emptied meanwhile (new/open/remove)
        var zb = ZBefore(placingSlot);
        if (zb == null) return;
        Complex zm = Rf.ZFromGamma(gamma, Z0), zbv = zb.Value;
        double w = 2 * Math.PI * Freq;
        bool series = placingSlot / 3 == 1;
        var el = slots[placingSlot];
        Complex zpre = el.ApplyParasitic(zbv, Freq, 1);       // after pad capacitance (and series inductance)
        Complex d = series ? zm - zpre : 1 / zm - 1 / zpre;   // added impedance or admittance
        if (!series && el.ParL != 0) d = 1 / (1 / d - new Complex(0, w * el.ParL));   // remove the branch inductance
        double val = (placingSlot % 3) switch
        {
            0 => series ? d.Real : (d.Real > 0 ? 1 / d.Real : -1),                 // R
            1 => series ? d.Imaginary / w : (d.Imaginary < 0 ? -1 / (w * d.Imaginary) : -1),  // L
            _ => series ? (d.Imaginary < 0 ? -1 / (w * d.Imaginary) : -1) : d.Imaginary / w,  // C
        };
        if (val <= 0 || double.IsInfinity(val) || double.IsNaN(val)) return;  // wrong side of the circle: keep last value
        slots[placingSlot].Value = val = Fix(slots[placingSlot].Type, val);
        Recalc();
        lblResult.Text = $"{slots[placingSlot].Name} = {Eng.Format(val)}{slots[placingSlot].Unit}   —  click to fix, ESC to cancel";
    }

    void StopPlacing()
    {
        placingSlot = -1;
        chart.Locus = null;
    }

    void FinishPlacing()
    {
        int slot = placingSlot;
        placingSlot = -1;
        chart.Locus = null;
        SelectSlot(slot);
    }

    void CancelPlacing()
    {
        slots[placingSlot] = placingBackup;
        if (placingBackup == null && selectedSlot == placingSlot) selectedSlot = -1;
        placingSlot = -1;
        chart.Locus = null;
        Recalc();
    }

    /// <summary>Value actually used: snapped to the Würth kit when kit mode is on.</summary>
    double Fix(ElementType t, double v) => chkKit.Checked ? Kits.Snap(t, v) : v;

    // ---------- design files ----------

    class DesignFile
    {
        public double Z0 { get; set; }
        public string Frequency { get; set; }
        public bool KitValuesOnly { get; set; }
        public int StartPoint { get; set; }
        public List<PointDto> Points { get; set; } = new();
        public List<SlotDto> Components { get; set; } = new();
        public List<ParasiticDto> Parasitics { get; set; } = new();
    }
    class ParasiticDto
    {
        public int Slot { get; set; }
        public double C { get; set; }
        public double L { get; set; }
        public List<MeasDto> Measurements { get; set; } = new();
    }
    class MeasDto { public double Value { get; set; } public double R { get; set; } public double X { get; set; } }
    class PointDto { public double R { get; set; } public double X { get; set; } public double Freq { get; set; } }
    class SlotDto { public int Slot { get; set; } public string Type { get; set; } public double Value { get; set; } }

    void UpdateTitle() => Text = $"{DesignName}{(dirty ? " *" : "")} — SmithMatch";

    /// <summary>Asks to save unsaved changes; false = the user cancelled.</summary>
    bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var r = MessageBox.Show(this, $"Save changes to \"{DesignName}\"?", "SmithMatch", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r == DialogResult.No || (r == DialogResult.Yes && SaveDesign(false));
    }

    void NewDesign()
    {
        if (!ConfirmDiscard()) return;
        StopPlacing();
        loading = true;
        chart.Points.Clear();
        slots = new Element[9];
        slotPar = new (double, double)[9];
        slotMeas = new List<(double, Complex)>[9];
        selectedSlot = -1;
        designPath = null;
        Recalc();
        loading = false;
        dirty = false;
        UpdateTitle();
    }

    bool SaveDesign(bool askName)
    {
        if (placingSlot >= 0) FinishPlacing();   // keep the value under the mouse
        if (askName || designPath == null)
        {
            using var sfd = new SaveFileDialog { Filter = "SmithMatch design (*.smith)|*.smith", FileName = DesignName, DefaultExt = "smith" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return false;
            designPath = sfd.FileName;
        }
        var start = StartPoint();
        var d = new DesignFile
        {
            Z0 = Z0, Frequency = txtFreq.Text, KitValuesOnly = chkKit.Checked,
            StartPoint = start == null ? 0 : chart.Points.IndexOf(start),
            Points = chart.Points.Select(p => new PointDto { R = p.Z.Real, X = p.Z.Imaginary, Freq = p.Freq }).ToList(),
            Components = Enumerable.Range(0, 9).Where(k => slots[k] != null)
                .Select(k => new SlotDto { Slot = k, Type = slots[k].Type.ToString(), Value = slots[k].Value }).ToList(),
            Parasitics = Enumerable.Range(0, 9).Where(k => slotPar[k] != default || slotMeas[k] is { Count: > 0 })
                .Select(k => new ParasiticDto
                {
                    Slot = k, C = slotPar[k].C, L = slotPar[k].L,
                    Measurements = (slotMeas[k] ?? new()).Select(m => new MeasDto { Value = m.value, R = m.z.Real, X = m.z.Imaginary }).ToList(),
                }).ToList(),
        };
        try
        {
            File.WriteAllText(designPath, System.Text.Json.JsonSerializer.Serialize(d, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { MessageBox.Show(this, "Cannot save: " + ex.Message); return false; }
        loading = true;
        Recalc();          // refresh the design name on the chart and the print title
        loading = false;
        dirty = false;
        UpdateTitle();
        return true;
    }

    void OpenDesign()
    {
        if (!ConfirmDiscard()) return;
        using var ofd = new OpenFileDialog { Filter = "SmithMatch design (*.smith)|*.smith|All files|*.*" };
        if (ofd.ShowDialog(this) == DialogResult.OK) LoadDesign(ofd.FileName);
    }

    /// <summary>Loads a .smith file (from File → Open or from the command line).</summary>
    public void LoadDesign(string path)
    {
        var ofd = new { FileName = path };
        DesignFile d;
        try { d = System.Text.Json.JsonSerializer.Deserialize<DesignFile>(File.ReadAllText(ofd.FileName)); }
        catch (Exception ex) { MessageBox.Show(this, "Cannot open: " + ex.Message); return; }

        StopPlacing();
        loading = true;
        designPath = ofd.FileName;
        txtZ0.Text = d.Z0.ToString(System.Globalization.CultureInfo.InvariantCulture);
        txtFreq.Text = d.Frequency;
        chkKit.Checked = false;            // load the exact values, then restore the mode without snapping
        chart.Points.Clear();
        chart.Points.AddRange(d.Points.Select(p => new DataPoint { Z = new Complex(p.R, p.X), Freq = p.Freq }));
        slots = new Element[9];
        foreach (var c in d.Components)
            if (c.Slot is >= 0 and < 9 && Enum.TryParse<ElementType>(c.Type, out var t))
                slots[c.Slot] = new Element { Type = t, Value = c.Value };
        slotPar = new (double, double)[9];
        slotMeas = new List<(double, Complex)>[9];
        foreach (var pd in d.Parasitics ?? new())
            if (pd.Slot is >= 0 and < 9)
            {
                slotPar[pd.Slot] = (pd.C, pd.L);
                slotMeas[pd.Slot] = pd.Measurements.Select(m => (m.Value, new Complex(m.R, m.X))).ToList();
            }
        selectedSlot = -1;
        forcedStart = d.StartPoint;
        Recalc();
        chkKit.CheckedChanged -= KitChanged;
        chkKit.Checked = d.KitValuesOnly;
        chkKit.CheckedChanged += KitChanged;
        loading = false;
        dirty = false;
        UpdateTitle();
    }

    void KitChanged(object sender, EventArgs e)
    {
        if (chkKit.Checked) foreach (var el in Elements) el.Value = Fix(el.Type, el.Value);
        Recalc();
    }

    /// <summary>Saves images of the main window, the VSWR sweep and the print dialog (used for the README).</summary>
    public void SaveScreenshots(string folder)
    {
        Directory.CreateDirectory(folder);
        void Save(Control c, string name)
        {
            c.Refresh();
            Application.DoEvents();
            using var bmp = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
            bmp.Save(Path.Combine(folder, name), System.Drawing.Imaging.ImageFormat.Png);
        }
        chart.Zoom = 1;
        chart.CursorGamma = null;
        Save(this, "main.png");

        ShowSweep();
        sweep.Size = new Size(800, 420);
        Save(sweep, "sweep.png");
        sweep.Close();

        using var doc = new System.Drawing.Printing.PrintDocument { DocumentName = DesignName };
        doc.BeginPrint += (_, _) => printPage = 0;
        doc.PrintPage += PrintPage;
        using var dlg = new PrintOptionsForm(printOptions, doc) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
        dlg.Show(this);
        for (int k = 0; k < 30; k++) { Application.DoEvents(); Thread.Sleep(50); }
        // the preview control draws asynchronously and is not captured: render the page and paste it in
        var ppc = new System.Drawing.Printing.PreviewPrintController();
        doc.PrintController = ppc;
        doc.DefaultPageSettings.Landscape = printOptions.Landscape;
        doc.Print();
        using (var bmp = new Bitmap(dlg.Width, dlg.Height))
        {
            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
            using var g = Graphics.FromImage(bmp);
            var area = dlg.RectangleToClient(dlg.preview.RectangleToScreen(dlg.preview.ClientRectangle));
            var off = dlg.PointToScreen(Point.Empty);
            area.Offset(off.X - dlg.Left, off.Y - dlg.Top);
            var page = ppc.GetPreviewPageInfo()[0].Image;
            float sc = Math.Min((area.Width - 30f) / page.Width, (area.Height - 30f) / page.Height);
            float w = page.Width * sc, h = page.Height * sc;
            float x = area.Left + (area.Width - w) / 2, y = area.Top + (area.Height - h) / 2;
            g.FillRectangle(Brushes.DimGray, x + 4, y + 4, w, h);
            g.FillRectangle(Brushes.White, x, y, w, h);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(page, x, y, w, h);
            bmp.Save(Path.Combine(folder, "print.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        dlg.Close();

        // parasitics dialog of the first position that has measurements
        int ks = Enumerable.Range(0, 9).FirstOrDefault(k => slotMeas[k] is { Count: > 0 }, -1);
        if (ks >= 0)
        {
            string[] groups = { "Load", "Serie", "Source" };
            var names = Enumerable.Range(0, 9).Where(k => k % 3 != 0).Select(k => (k, $"{groups[k / 3]} {"RLC"[k % 3]}")).ToList();
            using var pf = new ParasiticsForm(names, ks, Freq, Z0, k => ZBefore(k) ?? Complex.Zero,
                k => slots[k]?.With(slots[k].Value) ?? new Element { Type = SlotType(k), Value = 1e-12 },
                k => slotPar[k], k => slotMeas[k]) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
            pf.Show(this);
            for (int k = 0; k < 20; k++) { Application.DoEvents(); Thread.Sleep(50); }
            Save(pf, "parasitics.png");
            pf.Close();
        }
        dirty = false;   // nothing to save when closing
    }

        /// <summary>Copies the parasitics of each position into the component placed there.</summary>
    void SyncParasitics()
    {
        for (int k = 0; k < 9; k++)
            if (slots[k] != null) (slots[k].ParC, slots[k].ParL) = slotPar[k];
    }

    void ShowParasitics()
    {
        if (placingSlot >= 0) FinishPlacing();
        if (StartPoint() == null) { MessageBox.Show(this, "Add the load datapoint first."); return; }
        string[] groups = { "Load", "Serie", "Source" };
        var names = Enumerable.Range(0, 9).Where(k => k % 3 != 0)
            .Select(k => (k, $"{groups[k / 3]} {"RLC"[k % 3]}")).ToList();
        using var dlg = new ParasiticsForm(names, selectedSlot >= 0 && selectedSlot % 3 != 0 ? selectedSlot : 5, Freq, Z0,
            k => ZBefore(k) ?? Complex.Zero,
            k => slots[k]?.With(slots[k].Value) ?? new Element { Type = SlotType(k), Value = 1e-12 },
            k => slotPar[k], k => slotMeas[k]);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var (k, par, meas) in dlg.Results)
        {
            slotPar[k] = par;
            slotMeas[k] = meas;
        }
        Recalc();
    }

    void ShowSweep()
    {
        if (sweep == null || sweep.IsDisposed)
        {
            sweep = new SweepForm(VswrAt, () => Freq);
            sweep.FormClosed += (_, _) => { sweepRange = sweep.Range ?? sweepRange; sweep = null; };
            sweep.Show(this);
        }
        else sweep.Activate();
    }

    /// <summary>
    /// VSWR of the whole network at frequency f. The load is only known at the design frequency, so it is
    /// modelled as R in series with the equivalent L (X &gt; 0) or C (X &lt; 0) found at that frequency.
    /// </summary>
    double? VswrAt(double f)
    {
        var start = StartPoint();
        if (start == null) return null;
        double w0 = 2 * Math.PI * start.Freq, w = 2 * Math.PI * f;
        double rl = start.Z.Real, xl = start.Z.Imaginary;
        double x = xl > 0 ? xl * w / w0 : xl < 0 ? xl * w0 / w : 0;
        Complex z = new(rl, x);
        foreach (var e in Elements) z = e.Apply(z, f, 1);
        return Rf.Vswr(Rf.Gamma(z, Z0));
    }

    void SelectSlot(int slot)
    {
        selectedSlot = slot;
        tune.Value = 0;
        if (slots[slot] != null) tuneBase = slots[slot].Value;
        Recalc();
    }

    /// <summary>Adds the component in this slot (if empty) or edits its value.</summary>
    void EditSlot(int slot)
    {
        var e = slots[slot] ?? new Element
        {
            Type = SlotType(slot),
            Value = (slot % 3) switch { 0 => slot / 3 == 1 ? 10 : 100, 1 => 1e-9, _ => 1e-12 },
        };
        if (!ShowElementDialog(e)) return;
        slots[slot] = e;
        SelectSlot(slot);
    }

    bool ShowElementDialog(Element e)
    {
        using var dlg = new InputDialog(e.Name, ($"Value [{e.Unit}] (e.g. 1.5p, 3.3n)", Eng.Format(e.Value)));
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        if (!Eng.TryParse(dlg.Values[0], out var v) || v <= 0) { MessageBox.Show(this, "Invalid value."); return false; }
        e.Value = Fix(e.Type, v);
        return true;
    }
    /// <summary>Lists every lossless L-network that matches the start DP to Z0 and lets the user pick one (live preview).</summary>
    void AutoMatch()
    {
        var start = StartPoint();
        if (start == null) { MessageBox.Show(this, "Add a datapoint first."); return; }
        double z0 = Z0, w = 2 * Math.PI * Freq;
        double rl = start.Z.Real, xl = start.Z.Imaginary;
        if (rl <= 0) { MessageBox.Show(this, "Load resistance must be > 0."); return; }

        // each solution fills the 9 slots: a shunt next to the load goes on the load side, after the series part on the source side
        var solutions = new List<Element[]>();
        string Key(Element[] a) => string.Join("|", a.Select(e => e?.ToString()));
        void Add(bool shuntAtSource, params Element[] els)
        {
            var sol = new Element[9];
            foreach (var e in els.Where(e => e != null))
            {
                int k = SlotOf(e.Type, shuntAtSource);
                (e.ParC, e.ParL) = slotPar[k];
                sol[k] = e;
            }
            // with parasitics the ideal values no longer match: refine them numerically on the real network
            if (sol.Any(e => e != null && e.HasParasitics)) RefineMatch(sol, start.Z);
            foreach (var e in sol.Where(e => e != null)) e.Value = Fix(e.Type, e.Value);
            if (sol.Any(e => e != null) && !solutions.Any(x => Key(x) == Key(sol)))
                solutions.Add(sol);
        }

        // Topology 1: shunt B next to the load, then series X (needs R²+X²−Z0·R ≥ 0)
        double d = rl * rl + xl * xl - z0 * rl;
        if (d >= 0)
            foreach (int s in new[] { 1, -1 })
            {
                double b = (xl + s * Math.Sqrt(rl / z0) * Math.Sqrt(d)) / (rl * rl + xl * xl);
                if (Math.Abs(b) < 1e-15) { Add(false, Reactance(-xl, w)); continue; }
                double x = 1 / b + xl * z0 / rl - z0 / (b * rl);
                Add(false, Susceptance(b, w), Reactance(x, w));
            }
        // Topology 2: series X next to the load, then shunt B (needs R ≤ Z0)
        if (rl <= z0)
            foreach (int s in new[] { 1, -1 })
            {
                double x = s * Math.Sqrt(rl * (z0 - rl)) - xl;
                double b = s * Math.Sqrt((z0 - rl) / rl) / z0;
                Add(true, Reactance(x, w), Susceptance(b, w));
            }

        if (solutions.Count == 0) { MessageBox.Show(this, "No L-network solution found."); return; }

        var backup = slots;
        using var dlg = new Form
        {
            Text = "Auto-match: choose a solution", Width = 520, Height = 260,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow
        };
        var lb = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        for (int i = 0; i < solutions.Count; i++)
        {
            var els = solutions[i].Where(e => e != null).ToList();
            string kind = els.All(e => e.Type is ElementType.SeriesL or ElementType.ShuntC) ? "low-pass"
                        : els.All(e => e.Type is ElementType.SeriesC or ElementType.ShuntL) ? "high-pass" : "mixed";
            string[] groups = { "Load", "Serie", "Source" };
            var parts = solutions[i].Select((e, k) => e == null ? null : $"{groups[k / 3]} {"RLC"[k % 3]} = {Eng.Format(e.Value)}{e.Unit}"
                                        + (Kits.PartNumber(e.Type, e.Value) is string pn ? $" [{pn}]" : ""))
                                    .Where(t => t != null);
            Complex zin = start.Z;
            foreach (var e in solutions[i].Where(e => e != null)) zin = e.Apply(zin, Freq, 1);
            string par = solutions[i].Any(e => e != null && e.HasParasitics) ? "   incl. parasitics" : "";
            lb.Items.Add($"{i + 1}. {string.Join("   ", parts)}   ({kind})   VSWR {Rf.Vswr(Rf.Gamma(zin, z0)):0.00}{par}");
        }
        lb.SelectedIndexChanged += (_, _) =>
        {
            slots = solutions[lb.SelectedIndex].Select(e => e?.With(e.Value)).ToArray();
            selectedSlot = -1;
            Recalc();
        };
        var ok = new Button { Text = "Use", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
        bar.Controls.Add(cancel); bar.Controls.Add(ok);
        dlg.Controls.Add(lb); dlg.Controls.Add(bar);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        lb.DoubleClick += (_, _) => dlg.DialogResult = DialogResult.OK;
        lb.SelectedIndex = 0;

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            slots = backup;
            Recalc();
        }
    }

    /// <summary>Adjusts the values of a solution (log scale, Nelder–Mead) to minimise |Γ| with the parasitics included.</summary>
    void RefineMatch(Element[] sol, Complex zLoad)
    {
        var els = sol.Where(e => e != null).ToList();
        var x0 = els.Select(e => Math.Log(e.Value)).ToArray();
        double Cost(double[] x)
        {
            Complex z = zLoad;
            for (int i = 0; i < els.Count; i++) z = els[i].With(Math.Exp(x[i])).Apply(z, Freq, 1);
            return Rf.Gamma(z, Z0).Magnitude;
        }
        var best = (x: x0, f: Cost(x0));
        foreach (double a in new[] { 0.0, -0.7, 0.7 })
            foreach (double b in new[] { 0.0, -0.7, 0.7 })
            {
                var start = x0.Select((v, i) => v + (i == 0 ? a : b)).ToArray();
                var r = Minimizer.Minimize(Cost, start, 0.4, 400);
                if (r.f < best.f) best = r;
            }
        for (int i = 0; i < els.Count; i++) els[i].Value = Math.Exp(best.x[i]);
    }

    static Element Reactance(double x, double w) =>
        Math.Abs(x) < 1e-9 ? null :
        x > 0 ? new Element { Type = ElementType.SeriesL, Value = x / w }
              : new Element { Type = ElementType.SeriesC, Value = -1 / (w * x) };

    static Element Susceptance(double b, double w) =>
        Math.Abs(b) < 1e-12 ? null :
        b > 0 ? new Element { Type = ElementType.ShuntC, Value = b / w }
              : new Element { Type = ElementType.ShuntL, Value = -1 / (w * b) };

    DataPoint StartPoint()
    {
        for (int i = 0; i < dpList.Items.Count && i < chart.Points.Count; i++)
            if (dpList.Items[i].Checked) return chart.Points[i];
        return chart.Points.Count > 0 ? chart.Points[0] : null;
    }

    // ---------- computation / display ----------

    void Recalc()
    {
        chart.Z0 = Z0;
        double f = Freq;

        // datapoint list (keep start selection)
        if (!loading) dirty = true;
        SyncParasitics();
        UpdateTitle();
        int checkedIdx = forcedStart;
        forcedStart = -1;
        if (checkedIdx < 0)
            for (int i = 0; i < dpList.Items.Count; i++) if (dpList.Items[i].Checked) checkedIdx = i;
        if (checkedIdx >= chart.Points.Count) checkedIdx = -1;
        if (checkedIdx < 0 && chart.Points.Count > 0) checkedIdx = 0;

        updatingList = true;
        dpList.BeginUpdate();
        dpList.Items.Clear();
        for (int i = 0; i < chart.Points.Count; i++)
        {
            var p = chart.Points[i];
            var it = new ListViewItem("") { Checked = i == checkedIdx };
            it.SubItems.Add("DP " + (i + 1));
            it.SubItems.Add(Rf.FormatZ(p.Z));
            it.SubItems.Add("Q=" + Rf.Q(p.Z).ToString("0.000"));
            it.SubItems.Add(Eng.Format(p.Freq) + "Hz");
            dpList.Items.Add(it);
        }
        dpList.EndUpdate();

        updatingList = false;

        schematic.Slots = slots;
        schematic.SelectedSlot = selectedSlot;
        schematic.LoadText = StartPoint() is DataPoint dp ? Rf.FormatZ(dp.Z) : "";
        schematic.Invalidate();

        // chain
        chart.Traces.Clear();
        chart.ChainPoints.Clear();
        chart.Hints.Clear();
        chart.ParasiticTraces.Clear();
        chart.Measured.Clear();
        chart.ModelPaths.Clear();
        var start = StartPoint();
        if (start != null)
        {
            Complex z = start.Z;
            string[] groups = { "Load", "Serie", "Source" };
            for (int k = 0; k < 9; k++)
            {
                var e = slots[k];
                if (e == null) continue;
                if (e.HasParasitics)
                    chart.ParasiticTraces.Add(Enumerable.Range(0, 51).Select(i => e.ApplyParasitic(z, f, i / 50.0)).ToList());
                if (slotMeas[k] is { Count: > 0 } meas)
                {
                    // measured points and the model over the measured range
                    foreach (var (v, zm) in meas) chart.Measured.Add((zm, Eng.Format(v) + e.Unit));
                    double v1 = meas.Min(m => m.value), v2 = meas.Max(m => m.value);
                    var zFrom = z;
                    chart.ModelPaths.Add(Enumerable.Range(0, 61).Select(i => e.With(v1 * Math.Pow(v2 / v1, i / 60.0)).Apply(zFrom, f, 1)).ToList());
                }
                var trace = new List<Complex>();
                for (int i = 0; i <= 100; i++) trace.Add(e.Apply(z, f, i / 100.0));
                chart.Traces.Add(trace);
                // direction hints: path of the point when the value is increased / decreased (×/÷ up to 10);
                // the chart trims it to a fixed arrow length
                var up = new List<Complex>();
                var down = new List<Complex>();
                for (int i = 0; i <= 200; i++)
                {
                    double factor = Math.Pow(10, i / 200.0);
                    up.Add(e.With(e.Value * factor).Apply(z, f, 1));
                    down.Add(e.With(e.Value / factor).Apply(z, f, 1));
                }
                string name = $"{groups[k / 3]} {"RLC"[k % 3]}";
                chart.Hints.Add((up, name + " +"));
                chart.Hints.Add((down, name + " −"));
                z = e.Apply(z, f, 1);
                chart.ChainPoints.Add(z);
            }
            var gm = Rf.Gamma(z, Z0);
            chart.InfoText = $"{DesignName}\nVSWR {Rf.Vswr(gm):0.00}   RL {Rf.ReturnLoss(gm):0.0} dB";
            schematic.ZinText = $"VSWR {Rf.Vswr(gm):0.00}";
            lblResult.Text = $"Zin = {Rf.FormatZ(z)}   |Γ| = {gm.Magnitude:0.000}   VSWR = {Rf.Vswr(gm):0.00}   RL = {Rf.ReturnLoss(gm):0.00} dB";
        }
        else { chart.InfoText = DesignName; schematic.ZinText = ""; }
        if (start == null) lblResult.Text = "Click on the chart (or use Keyboard DP) to add a datapoint.";

        chart.Invalidate();
        schematic.Invalidate();
        sweep?.RefreshPlot();
    }

    void UpdateCursor(Complex g)
    {
        var z = Rf.ZFromGamma(g, Z0);
        var y = 1 / z;
        cursorBoxes["Return Loss"].Text = $"{Rf.ReturnLoss(g):0.00} dB";
        cursorBoxes["VSWR"].Text = $"{Rf.Vswr(g):0.00} :1";
        cursorBoxes["Q"].Text = $"{Rf.Q(z):0.00}";
        cursorBoxes["Γ"].Text = $"{g.Magnitude:0.000} / {g.Phase * 180 / Math.PI:0.000}°";
        cursorBoxes["Y"].Text = $"({y.Real * 1e3:0.00}{(y.Imaginary < 0 ? "-" : "+")}j{Math.Abs(y.Imaginary * 1e3):0.00}) mS";
        cursorBoxes["Z"].Text = $"({z.Real:0.00}{(z.Imaginary < 0 ? "-" : "+")}j{Math.Abs(z.Imaginary):0.00}) Ω";
        cursorBoxes["Zo"].Text = $"{Z0:0.00} Ω";
        cursorBoxes["Freq"].Text = Eng.Format(Freq) + "Hz";
    }

    // ---------- printing: chart, schematic and all results on one page ----------

    void Print(bool preview)
    {
        if (placingSlot >= 0) FinishPlacing();
        using var doc = new System.Drawing.Printing.PrintDocument { DocumentName = DesignName };
        doc.BeginPrint += (_, _) => printPage = 0;
        doc.PrintPage += PrintPage;
        using var dlg = new PrintOptionsForm(printOptions, doc);
        if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.PrintRequested) return;
        using var pd = new PrintDialog { Document = doc, UseEXDialog = true };
        doc.DefaultPageSettings.Landscape = printOptions.Landscape;
        if (pd.ShowDialog(this) == DialogResult.OK) doc.Print();
    }

    void PrintPage(object sender, System.Drawing.Printing.PrintPageEventArgs e)
    {
        var o = printOptions;
        var g = e.Graphics;
        var m = e.MarginBounds;
        float y = m.Top;
        using var title = new Font("Segoe UI", 16f, FontStyle.Bold);
        using var head = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var text = new Font("Segoe UI", 9f);
        float lineH = text.GetHeight(g) + 3;
        bool hasStart = StartPoint() != null;

        g.DrawString(DesignName, title, Brushes.Black, m.Left, y);
        g.DrawString($"Z0 = {Z0:0.##} Ω     f = {Eng.Format(Freq)}Hz     {DateTime.Now:yyyy-MM-dd HH:mm}", text, Brushes.Black, m.Left, y + 30);
        y += 55;

        bool ownPage = o.Chart && o.ChartOwnPage;
        bool chartHere = o.Chart && printPage == 0;
        bool sectionsHere = !ownPage || printPage == 1;

        // text lines of the sections
        var dpLines = new List<string>();
        for (int i = 0; i < chart.Points.Count; i++)
        {
            var p = chart.Points[i];
            dpLines.Add($"DP {i + 1}{(p == StartPoint() ? " (start)" : "")}:  Z = {Rf.FormatZ(p.Z)}   Q = {Rf.Q(p.Z):0.000}   f = {Eng.Format(p.Freq)}Hz");
        }
        var compLines = new List<string>();
        string[] groups = { "Load", "Serie", "Source" };
        for (int k = 0; k < 9; k++)
            if (slots[k] != null) compLines.Add($"{groups[k / 3]} {"RLC"[k % 3]} = {Eng.Format(slots[k].Value)}{slots[k].Unit}"
                + (Kits.PartNumber(slots[k].Type, slots[k].Value) is string pn ? $"   Würth {pn}" : "")
                + (slots[k].HasParasitics ? $"   parasitics: {Eng.Format(slots[k].ParC)}F to ground, {Eng.Format(slots[k].ParL)}H in series" : ""));
        var resLines = hasStart ? new List<string> { lblResult.Text } : new List<string>();

        float schH = 110, sweepH = Math.Max(130, m.Height * 0.15f);
        float SectionsHeight()
        {
            float h = 0;
            if (o.Schematic) h += 22 + schH + 10;
            if (o.Sweep && hasStart) h += 22 + sweepH + 10;
            if (o.Datapoints && dpLines.Count > 0) h += 22 + dpLines.Count * lineH + 8;
            if (o.Components && compLines.Count > 0) h += 22 + compLines.Count * lineH + 8;
            if (o.Results && resLines.Count > 0) h += 22 + resLines.Count * lineH + 8;
            return h;
        }

        if (chartHere)
        {
            // the chart takes all the space left by the sections printed on the same page
            float avail = m.Bottom - y - (sectionsHere ? SectionsHeight() : 0) - 10;
            float size = Math.Max(Math.Min(m.Width, avail), Math.Min(m.Width, m.Height) * 0.35f);
            using (var bmp = RenderChart(o, size))
                g.DrawImage(bmp, m.Left + (m.Width - size) / 2, y, size, size);
            y += size + 10;
        }

        if (sectionsHere)
        {
            if (o.Schematic)
            {
                g.DrawString("Schematic", head, Brushes.Black, m.Left, y);
                y += 22;
                using var bmp = RenderControl(schematic, Math.Max(schematic.Width, 400), 200);
                float sw = Math.Min(m.Width, bmp.Width * schH / bmp.Height);
                g.DrawImage(bmp, m.Left, y, sw, sw * bmp.Height / bmp.Width);
                y += schH + 10;
            }
            if (o.Sweep && hasStart)
            {
                var range = sweep?.Range ?? sweepRange ?? (Freq * 0.8, Freq * 1.2, 5);
                g.DrawString($"VSWR from {Eng.Format(range.f1)}Hz to {Eng.Format(range.f2)}Hz", head, Brushes.Black, m.Left, y);
                y += 22;
                SweepForm.DrawSweep(g, new RectangleF(m.Left, y, m.Width, sweepH), range.f1, range.f2, range.vmax, VswrAt, Freq);
                y += sweepH + 10;
            }
            void Block(bool show, string name, List<string> lines)
            {
                if (!show || lines.Count == 0) return;
                g.DrawString(name, head, Brushes.Black, m.Left, y);
                y += 22;
                foreach (var l in lines) { g.DrawString(l, text, Brushes.Black, new RectangleF(m.Left, y, m.Width, lineH)); y += lineH; }
                y += 8;
            }
            Block(o.Datapoints, "Datapoints", dpLines);
            Block(o.Components, "Components", compLines);
            Block(o.Results, "Results", resLines);
        }

        printPage++;
        e.HasMorePages = ownPage && printPage == 1;
        if (!e.HasMorePages) printPage = 0;
    }

    /// <summary>Renders the Smith chart for printing with the chosen layers (zoom 1:1, no cursor).</summary>
    Bitmap RenderChart(PrintOptions o, float printedSize)
    {
        var keep = (chart.ShowImpedance, chart.ShowAdmittance, chart.ShowLabels, chart.ShowTraces, chart.ShowHints,
                    chart.ShowPoints, chart.ShowInfo, chart.Zoom, chart.CursorGamma);
        chart.ShowImpedance = o.ZGrid; chart.ShowAdmittance = o.YGrid; chart.ShowLabels = o.Labels;
        chart.ShowTraces = o.Traces; chart.ShowHints = o.Arrows; chart.ShowPoints = o.Points; chart.ShowInfo = o.InfoBox;
        chart.Zoom = 1; chart.CursorGamma = null;
        // the 1600 px bitmap is shrunk to 'printedSize' (1/100 inch): enlarge text and lines so they print
        // at about 80 % of their screen size (96 dpi), which fits the printed chart without overlapping
        chart.S = 1600f / (printedSize * 0.96f) * 0.8f;
        try { return RenderControl(chart, 1600, 1600); }
        finally
        {
            (chart.ShowImpedance, chart.ShowAdmittance, chart.ShowLabels, chart.ShowTraces, chart.ShowHints,
             chart.ShowPoints, chart.ShowInfo, chart.Zoom, chart.CursorGamma) = keep;
            chart.S = 1;
        }
    }

    /// <summary>Draws a control into a bitmap of the given size (temporarily resizing it).</summary>
    static Bitmap RenderControl(Control c, int w, int h)
    {
        var old = c.Size;
        var dock = c.Dock;
        c.Dock = DockStyle.None;
        c.Size = new Size(w, h);
        var bmp = new Bitmap(w, h);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
        c.Size = old;
        c.Dock = dock;
        return bmp;
    }

    void ExportImage()
    {
        using var sfd = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "smith.png" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        using var bmp = new Bitmap(chart.Width, chart.Height);
        chart.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }
}
