using System.Diagnostics;
using System.Reflection;

namespace SmithMatch;

/// <summary>Icon and logo embedded in the program (Wattlet AS).</summary>
public static class Branding
{
    static Icon icon;
    static Image logo;

    public static Icon AppIcon => icon ??= Load(s => new Icon(s), "SmithMatch.icon.ico");
    public static Image Logo => logo ??= Load(Image.FromStream, "SmithMatch.logo.png");

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int plus = v.IndexOf('+');      // drop the "+commit" suffix
            return plus >= 0 ? v[..plus] : v;
        }
    }

    static T Load<T>(Func<Stream, T> make, string name) where T : class
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return s == null ? null : make(s);
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser: ignore */ }
    }
}

/// <summary>Help → About: program, Wattlet AS and links.</summary>
public class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About SmithMatch";
        Icon = Branding.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(560, 570);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(24, 20, 24, 10), AutoScroll = true,
        };
        int width = 500;

        if (Branding.Logo is Image logo)
            flow.Controls.Add(new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(380, 73), Margin = new Padding(0, 0, 0, 14),
            });

        Label Text_(string text, float size = 9.5f, FontStyle style = FontStyle.Regular, Color? color = null, int bottom = 8) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Font = new Font("Segoe UI", size, style),
            ForeColor = color ?? Color.FromArgb(40, 40, 40), Margin = new Padding(0, 0, 0, bottom),
        };
        LinkLabel Link(string text, string url, int bottom = 10)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, bottom), LinkColor = Color.FromArgb(0, 140, 210) };
            l.LinkClicked += (_, _) => Branding.OpenUrl(url);
            return l;
        }

        flow.Controls.Add(Text_($"SmithMatch {Branding.Version}", 14f, FontStyle.Bold, bottom: 2));
        flow.Controls.Add(Text_("Interactive Smith chart for designing RF and antenna matching networks.", bottom: 14));

        flow.Controls.Add(Text_("Made by Wattlet AS — Lillehammer, Norway", 10.5f, FontStyle.Bold, bottom: 6));
        flow.Controls.Add(Text_(
            "Wattlet AS develops and produces connected electronics in Lillehammer: hardware design, " +
            "electronics production and assembly, firmware and cloud services. Our products communicate over " +
            "Wi-Fi, 4G and EnOcean radio, so antenna matching is part of our daily work — SmithMatch grew out " +
            "of matching the antennas on our own boards.", bottom: 12));

        flow.Controls.Add(Text_("SmartVVB", 10f, FontStyle.Bold, bottom: 0));
        flow.Controls.Add(Text_("Smart control of water heaters by spot price and grid tariff, for homes and cabins. " +
                                "Type-approved by Nemko.", bottom: 2));
        flow.Controls.Add(Link("smartvvb.no", "https://smartvvb.no"));

        flow.Controls.Add(Text_("PoE-Sea", 10f, FontStyle.Bold, bottom: 0));
        flow.Controls.Add(Text_("Wi-Fi on demand for schools: access points switched off classroom by classroom, " +
                                "with battery-free wireless switches and a cloud service. Marketed by PoE-Tic, France.", bottom: 2));
        flow.Controls.Add(Link("poe-sea.com", "https://poe-sea.com", 14));

        flow.Controls.Add(Text_("Source code and releases (MIT license):", 8.5f, color: Color.DimGray, bottom: 0));
        flow.Controls.Add(Link("github.com/lwathelet/SmithMatch", "https://github.com/lwathelet/SmithMatch", 0));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 14, 0), BackColor = Color.FromArgb(245, 245, 245),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(90, 30), BackColor = SystemColors.Control };
        bar.Controls.Add(ok);

        Controls.Add(flow);
        Controls.Add(bar);
        AcceptButton = ok;
        CancelButton = ok;
    }
}
