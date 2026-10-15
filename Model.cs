using System.Globalization;
using System.Numerics;

namespace SmithMatch;

public enum ElementType
{
    SeriesC, SeriesL, SeriesR, ShuntC, ShuntL, ShuntR
}

/// <summary>A network element added after the start datapoint (from load towards source).</summary>
public class Element
{
    public ElementType Type;
    public double Value;          // F, H or Ohm

    public string Unit => Type switch
    {
        ElementType.SeriesC or ElementType.ShuntC => "F",
        ElementType.SeriesL or ElementType.ShuntL => "H",
        ElementType.SeriesR or ElementType.ShuntR => "Ω",
        _ => "",
    };

    public string Name => Type switch
    {
        ElementType.SeriesC => "Series C",
        ElementType.SeriesL => "Series L",
        ElementType.SeriesR => "Series R",
        ElementType.ShuntC => "Shunt C",
        ElementType.ShuntL => "Shunt L",
        ElementType.ShuntR => "Shunt R",
        _ => "?",
    };

    public override string ToString()
    {
        return $"{Name}  {Eng.Format(Value)}{Unit}";
    }

    /// <summary>Applies this element to impedance z, with the element value scaled by t (0..1) for drawing the trace.</summary>
    public Complex Apply(Complex z, double freq, double t)
    {
        double w = 2 * Math.PI * freq;
        double v = Value;
        switch (Type)
        {
            case ElementType.SeriesR: return z + v * t;
            case ElementType.SeriesL: return z + new Complex(0, w * v * t);
            case ElementType.SeriesC: return t <= 0 ? z : z + new Complex(0, -1 / (w * v)) * t;
            case ElementType.ShuntR: return t <= 0 ? z : 1 / (1 / z + t / v);
            case ElementType.ShuntC: return 1 / (1 / z + new Complex(0, w * v * t));
            case ElementType.ShuntL: return t <= 0 ? z : 1 / (1 / z + new Complex(0, -1 / (w * v)) * t);
        }
        return z;
    }
}

public class DataPoint
{
    public Complex Z;
    public double Freq;
}

public static class Rf
{
    public static Complex Gamma(Complex z, double z0) => (z - z0) / (z + z0);
    public static Complex ZFromGamma(Complex g, double z0) => z0 * (1 + g) / (1 - g);
    public static double Vswr(Complex g) { double m = g.Magnitude; return m >= 1 ? double.PositiveInfinity : (1 + m) / (1 - m); }
    public static double ReturnLoss(Complex g) => -20 * Math.Log10(Math.Max(g.Magnitude, 1e-12));
    public static double Q(Complex z) => Math.Abs(z.Real) < 1e-15 ? double.PositiveInfinity : Math.Abs(z.Imaginary / z.Real);

    public static string FormatZ(Complex z) =>
        $"({z.Real:0.000} {(z.Imaginary < 0 ? "-" : "+")} j{Math.Abs(z.Imaginary):0.000}) Ω";
}

/// <summary>Engineering notation helpers (p, n, u, m, k, M, G).</summary>
public static class Eng
{
    static readonly (double f, string s)[] Prefix =
    {
        (1e9, "G"), (1e6, "M"), (1e3, "k"), (1, ""), (1e-3, "m"), (1e-6, "u"), (1e-9, "n"), (1e-12, "p"), (1e-15, "f")
    };

    public static string Format(double v)
    {
        if (v == 0 || double.IsNaN(v) || double.IsInfinity(v)) return v.ToString(CultureInfo.InvariantCulture);
        double a = Math.Abs(v);
        foreach (var (f, s) in Prefix)
            if (a >= f * 0.9999) return (v / f).ToString("0.#", CultureInfo.InvariantCulture) + s;
        return v.ToString("0.###E0", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().Replace(',', '.');
        // strip unit letters at end (F, H, Hz, Ohm, Ω)
        foreach (var u in new[] { "Hz", "hz", "Ohm", "ohm", "Ω", "F", "H" })
            if (text.EndsWith(u) && text.Length > u.Length) { text = text[..^u.Length]; break; }
        double mult = 1;
        char last = text[^1];
        switch (last)
        {
            case 'G': mult = 1e9; break;
            case 'M': mult = 1e6; break;
            case 'k': case 'K': mult = 1e3; break;
            case 'm': mult = 1e-3; break;
            case 'u': case 'µ': mult = 1e-6; break;
            case 'n': mult = 1e-9; break;
            case 'p': mult = 1e-12; break;
            case 'f': mult = 1e-15; break;
        }
        if (mult != 1) text = text[..^1];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return false;
        value = v * mult;
        return true;
    }
}
