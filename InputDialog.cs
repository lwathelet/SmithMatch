namespace SmithMatch;

/// <summary>Small modal dialog with one text box per field.</summary>
public class InputDialog : Form
{
    readonly List<TextBox> boxes = new();
    public string[] Values => boxes.Select(b => b.Text).ToArray();

    public InputDialog(string title, params (string label, string value)[] fields)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9f);

        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill };
        foreach (var (label, value) in fields)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
            var tb = new TextBox { Text = value, Width = 160 };
            boxes.Add(tb);
            t.Controls.Add(tb);
        }
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        t.Controls.Add(buttons);
        t.SetColumnSpan(buttons, 2);
        Controls.Add(t);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
