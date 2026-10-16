using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SmithMatch;

/// <summary>
/// Measurements of one position for several component values, and the fit of its parasitics
/// (capacitance to ground at the input node and inductance in series with the component).
/// </summary>
public class ParasiticsForm : Form
{
    readonly Func<int, Complex> zBefore;
    readonly Func<int, Element> template;
    readonly double freq;
    readonly ComboBox cboSlot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToResizeRows = false, RowHeadersWidth = 28,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
    };
    readonly CheckBox chkC = new() { Text = "Capacitance to ground at the input (pads, trace)", AutoSize = true, Checked = true };
    readonly CheckBox chkL = new() { Text = "Inductance in series with the component (trace, ESL)", AutoSize = true, Checked = true };
    readonly TextBox txtC = new() { Width = 80 };
    readonly TextBox txtL = new() { Width = 80 };
    readonly Label lblInfo = new() { AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f) };
    readonly SmithChartControl preview = new() { Dock = DockStyle.Fill, ShowHints = false };

    readonly List<(int slot, string name)> slots;
    readonly Dictionary<int, ((double C, double L) par, List<(double value, Complex z)> meas)> state = new();
    int current = -1;
    bool updating;

    /// <summary>Positions whose parasitics or measurements were edited.</summary>
    public List<(int slot, (double C, double L) par, List<(double value, Complex z)> meas)> Results { get; } = new();

    public ParasiticsForm(List<(int slot, string name)> slots, int initialSlot, double freq, double z0,
                          Func<int, Complex> zBefore, Func<int, Element> template,
                          Func<int, (double C, double L)> parasitics, Func<int, List<(double value, Complex z)>> measurements)
    {
        this.slots = slots;
        this.freq = freq;
        preview.Z0 = z0;
        this.zBefore = zBefore;
        this.template = template;
        foreach (var (k, _) in slots) state[k] = (parasitics(k), measurements(k)?.ToList() ?? new());

        Text = "Parasitics from measurements";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        Size = new Size(1150, 680);

        // ----- left: component, measurements, fit
        var left = new TableLayoutPanel { Dock = DockStyle.Left, Width = 560, ColumnCount = 1, Padding = new Padding(8) };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        top.Controls.Add(new Label { Text = "Component varied:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        foreach (var (_, name) in slots) cboSlot.Items.Add(name);
        top.Controls.Add(cboSlot);
        left.Controls.Add(top);
        left.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 4, 0, 6),
            Text = "Enter the impedance measured just after this component for several values " +
                   "(components further towards the source not mounted, earlier ones as in the design). " +
                   "R and X can be typed together in the R column, e.g. 0,5-30j.",
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "V", HeaderText = "Value (e.g. 600f, 1.2n)" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "R", HeaderText = "Measured R [Ω]" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "Measured X [Ω]" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "M", HeaderText = "Model", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "E", HeaderText = "Error [Ω]", ReadOnly = true, FillWeight = 60 });
        foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.Columns["M"].DefaultCellStyle.ForeColor = Color.DimGray;
        grid.Columns["E"].DefaultCellStyle.ForeColor = Color.DimGray;
        grid.CellEndEdit += (_, e) => { SplitComplex(e.RowIndex); Update_(); };
        grid.RowsRemoved += (_, _) => Update_();
        grid.KeyDown += GridKeyDown;
        left.Controls.Add(grid);

        var fit = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        fit.Controls.Add(chkC, 0, 0);
        fit.Controls.Add(txtC, 1, 0);
        fit.Controls.Add(new Label { Text = "F", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 2, 0);
        fit.Controls.Add(chkL, 0, 1);
        fit.Controls.Add(txtL, 1, 1);
        fit.Controls.Add(new Label { Text = "H", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 2, 1);
        txtC.TextChanged += (_, _) => { if (!updating) Update_(); };
        txtL.TextChanged += (_, _) => { if (!updating) Update_(); };
        left.Controls.Add(fit);
        left.Controls.Add(lblInfo);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        var ok = new Button { Text = "Apply", Width = 90, Height = 30 };
        var btnFit = new Button { Text = "Fit", Width = 90, Height = 30, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        var btnZero = new Button { Text = "No parasitics", Width = 110, Height = 30 };
        ok.Click += (_, _) => { Save(); foreach (var (k, _) in slots) Results.Add((k, state[k].par, state[k].meas)); DialogResult = DialogResult.OK; };
        btnFit.Click += (_, _) => DoFit();
        btnZero.Click += (_, _) => { SetParams(0, 0); Update_(); };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(btnZero);
        buttons.Controls.Add(btnFit);
        left.Controls.Add(buttons);

        Controls.Add(preview);
        Controls.Add(left);
        CancelButton = cancel;

        cboSlot.SelectedIndexChanged += (_, _) => { Save(); Load_(slots[cboSlot.SelectedIndex].slot); };
        int idx = slots.FindIndex(s => s.slot == initialSlot);
        cboSlot.SelectedIndex = idx >= 0 ? idx : 0;
    }

    // ----- state of the selected position

    void Load_(int slot)
    {
        current = slot;
        updating = true;
        grid.Rows.Clear();
        foreach (var (v, z) in state[slot].meas)
            grid.Rows.Add(Eng.Format(v), z.Real.ToString("0.###"), z.Imaginary.ToString("0.###"));
        SetParams(state[slot].par.C, state[slot].par.L);
        updating = false;
        Update_();
    }

    void Save()
    {
        if (current < 0) return;
        state[current] = (Params(), Measurements());
    }

    void SetParams(double c, double l)
    {
        updating = true;
        txtC.Text = c == 0 ? "0" : Eng.Format(c);
        txtL.Text = l == 0 ? "0" : Eng.Format(l);
        updating = false;
    }

    (double C, double L) Params() =>
        (Eng.TryParse(txtC.Text, out var c) ? c : 0, Eng.TryParse(txtL.Text, out var l) ? l : 0);

    List<(double value, Complex z)> Measurements()
    {
        var list = new List<(double, Complex)>();
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow) continue;
            if (Eng.TryParse(r.Cells["V"].Value as string, out var v) && v > 0
                && TryNumber(r.Cells["R"].Value as string, out var re) && TryNumber(r.Cells["X"].Value as string, out var im))
                list.Add((v, new Complex(re, im)));
        }
        return list;
    }

    static bool TryNumber(string s, out double v) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>"0,5-30j" (or "0.5 - j30") typed in the R column is split into R and X.</summary>
    void SplitComplex(int row)
    {
        if (row < 0 || row >= grid.Rows.Count) return;
        var cell = grid.Rows[row].Cells["R"];
        if (cell.Value is not string s || !s.Contains('j', StringComparison.OrdinalIgnoreCase)) return;
        var m = Regex.Match(s.Replace(" ", ""), @"^([+-]?[\d.,]+)([+-])j?([\d.,]+)j?$", RegexOptions.IgnoreCase);
        if (!m.Success) return;
        cell.Value = m.Groups[1].Value;
        grid.Rows[row].Cells["X"].Value = (m.Groups[2].Value == "-" ? "-" : "") + m.Groups[3].Value;
    }

    /// <summary>Ctrl+V pastes rows copied from Excel (value, R, X separated by tabs or semicolons).</summary>
    void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (!(e.Control && e.KeyCode == Keys.V) || !Clipboard.ContainsText()) return;
        int row = Math.Max(0, grid.CurrentCell?.RowIndex ?? 0);
        foreach (var line in Clipboard.GetText().Split('\n').Select(l => l.Trim('\r')).Where(l => l.Trim().Length > 0))
        {
            var parts = line.Split('\t', ';');
            if (row >= grid.Rows.Count - 1) grid.Rows.Add();
            for (int c = 0; c < Math.Min(3, parts.Length); c++) grid.Rows[row].Cells[c].Value = parts[c].Trim();
            SplitComplex(row);
            row++;
        }
        e.Handled = true;
        Update_();
    }

    // ----- fit and display

    void DoFit()
    {
        var meas = Measurements();
        if (meas.Count < 2) { MessageBox.Show(this, "Enter at least 2 measurements (3 or more is better)."); return; }
        var tpl = template(current);
        var r = ParasiticFit.Fit(tpl, meas, zBefore(current), freq, chkC.Checked, chkL.Checked);
        SetParams(r.C, r.L);
        Update_();
    }

    void Update_()
    {
        if (updating || current < 0) return;
        var meas = Measurements();
        var (c, l) = Params();
        var tpl = template(current);
        var zb = zBefore(current);

        // model column
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            row.Cells["M"].Value = row.Cells["E"].Value = "";
            if (!Eng.TryParse(row.Cells["V"].Value as string, out var v) || v <= 0) continue;
            var zm = ParasiticFit.Model(tpl, v, c, l, zb, freq);
            row.Cells["M"].Value = $"{zm.Real:0.00} {(zm.Imaginary < 0 ? "-" : "+")} j{Math.Abs(zm.Imaginary):0.00}";
            if (TryNumber(row.Cells["R"].Value as string, out var re) && TryNumber(row.Cells["X"].Value as string, out var im))
                row.Cells["E"].Value = (zm - new Complex(re, im)).Magnitude.ToString("0.00");
        }

        // summary
        var withPar = tpl.With(tpl.Value); withPar.ParC = c; withPar.ParL = l;
        var zIn = withPar.ApplyParasitic(zb, freq, 1);
        string txt = $"The component sees {Rf.FormatZ(zIn)}   (without parasitics: {Rf.FormatZ(zb)})";
        if (meas.Count > 0)
        {
            double rmsTheory = ParasiticFit.Rms(tpl, meas, 0, 0, zb, freq);
            double rms = ParasiticFit.Rms(tpl, meas, c, l, zb, freq);
            txt = $"Error: {rms:0.00} Ω RMS with these parasitics, {rmsTheory:0.00} Ω without.\n" + txt;
            if (c < 0 || l < 0) txt += "\nA negative value means the load itself is probably not what was measured/entered.";
        }
        lblInfo.Text = txt;

        // chart: measured points, theory (dotted) and model (solid) over the measured range
        preview.Points.Clear();
        preview.Traces.Clear();
        preview.ModelPaths.Clear();
        preview.Measured.Clear();
        preview.ParasiticTraces.Clear();
        preview.Points.Add(new DataPoint { Z = zb, Freq = freq });
        if (meas.Count > 0)
        {
            double v1 = meas.Min(m => m.value) / 1.5, v2 = meas.Max(m => m.value) * 1.5;
            IEnumerable<double> Range() => Enumerable.Range(0, 81).Select(i => v1 * Math.Pow(v2 / v1, i / 80.0));
            preview.ModelPaths.Add(Range().Select(v => ParasiticFit.Model(tpl, v, 0, 0, zb, freq)).ToList());
            preview.Traces.Add(Range().Select(v => ParasiticFit.Model(tpl, v, c, l, zb, freq)).ToList());
            foreach (var (v, z) in meas) preview.Measured.Add((z, Eng.Format(v) + tpl.Unit));
        }
        if (c != 0 || l != 0) preview.ParasiticTraces.Add(Enumerable.Range(0, 51).Select(i => withPar.ApplyParasitic(zb, freq, i / 50.0)).ToList());
        preview.InfoText = "×  measured\n···  theory (no parasitics)\n—  model with parasitics";
        preview.Invalidate();
    }
}
