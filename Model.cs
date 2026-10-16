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
    public double ParC;           // parasitic capacitance to ground at the input (load-side) node, F (pads, trace)
    public double ParL;           // parasitic inductance in series with the component, H (trace, ESL, via)

    public bool IsSeries => Type is ElementType.SeriesC or ElementType.SeriesL or ElementType.SeriesR;
    public bool HasParasitics => ParC != 0 || ParL != 0;

    /// <summary>Same component and parasitics with another value.</summary>
    public Element With(double value) => new() { Type = Type, Value = value, ParC = ParC, ParL = ParL };

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

    /// <summary>
    /// Parasitics only, scaled by t (0..1) for drawing: the pad capacitance at the input node, then for a series
    /// component the series inductance (for a shunt component the inductance is in its branch, see Apply).
    /// </summary>
    public Complex ApplyParasitic(Complex z, double freq, double t)
    {
        double w = 2 * Math.PI * freq;
        if (ParC != 0) z = 1 / (1 / z + new Complex(0, w * ParC * t));
        if (IsSeries && ParL != 0) z += new Complex(0, w * ParL * t);
        return z;
    }

    /// <summary>Applies this element (with its parasitics) to impedance z; the component value is scaled by t (0..1) for drawing the trace.</summary>
    public Complex Apply(Complex z, double freq, double t)
    {
        double w = 2 * Math.PI * freq;
        double v = Value;
        z = ApplyParasitic(z, freq, 1);
        if (t <= 0) return z;
        if (IsSeries)
        {
            Complex zc = Type switch
            {
                ElementType.SeriesR => v * t,
                ElementType.SeriesL => new Complex(0, w * v * t),
                _ => new Complex(0, -1 / (w * v)) * t,
            };
            return z + zc;
        }
        Complex yc = Type switch
        {
            ElementType.ShuntR => t / v,
            ElementType.ShuntC => new Complex(0, w * v * t),
            _ => new Complex(0, -1 / (w * v)) * t,
        };
        if (ParL != 0) yc = yc / (1 + new Complex(0, w * ParL) * yc);   // branch: component in series with ParL
        return 1 / (1 / z + yc);
    }
}

/// <summary>Nelder–Mead minimiser (small problems: parasitic fit, matching with parasitics).</summary>
public static class Minimizer
{
    public static (double[] x, double f) Minimize(Func<double[], double> fn, double[] x0, double step = 0.5, int iterations = 600)
    {
        int n = x0.Length;
        var pts = new double[n + 1][];
        var val = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            pts[i] = (double[])x0.Clone();
            if (i > 0) pts[i][i - 1] += step;
            val[i] = Safe(fn(pts[i]));
        }
        for (int it = 0; it < iterations; it++)
        {
            var order = Enumerable.Range(0, n + 1).OrderBy(i => val[i]).ToArray();
            pts = order.Select(i => pts[i]).ToArray();
            val = order.Select(i => val[i]).ToArray();
            var c = new double[n];
            for (int i = 0; i < n; i++) for (int d = 0; d < n; d++) c[d] += pts[i][d] / n;
            double[] Along(double k) => c.Select((cd, d) => cd + k * (pts[n][d] - cd)).ToArray();
            var xr = Along(-1); double fr = Safe(fn(xr));
            if (fr < val[0])
            {
                var xe = Along(-2); double fe = Safe(fn(xe));
                if (fe < fr) { pts[n] = xe; val[n] = fe; } else { pts[n] = xr; val[n] = fr; }
            }
            else if (fr < val[n - 1]) { pts[n] = xr; val[n] = fr; }
            else
            {
                var xc = Along(0.5); double fc = Safe(fn(xc));
                if (fc < val[n]) { pts[n] = xc; val[n] = fc; }
                else
                    for (int i = 1; i <= n; i++)
                    {
                        pts[i] = pts[i].Select((v, d) => pts[0][d] + 0.5 * (v - pts[0][d])).ToArray();
                        val[i] = Safe(fn(pts[i]));
                    }
            }
        }
        int best = Array.IndexOf(val, val.Min());
        return (pts[best], val[best]);
    }

    static double Safe(double v) => double.IsFinite(v) ? v : double.MaxValue;
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

/// <summary>Estimates the parasitics of a position from impedances measured for several component values.</summary>
public static class ParasiticFit
{
    /// <summary>Impedance after the component (value v) with the given parasitics, starting from zBefore.</summary>
    public static Complex Model(Element template, double v, double parC, double parL, Complex zBefore, double freq)
    {
        var e = template.With(v);
        e.ParC = parC;
        e.ParL = parL;
        return e.Apply(zBefore, freq, 1);
    }

    /// <summary>RMS distance (Ω) between model and measurements.</summary>
    public static double Rms(Element template, IList<(double value, Complex z)> meas, double parC, double parL, Complex zBefore, double freq) =>
        Math.Sqrt(meas.Average(m => Math.Pow((Model(template, m.value, parC, parL, zBefore, freq) - m.z).Magnitude, 2)));

    /// <summary>
    /// Best pad capacitance (F) and series inductance (H); either can be left out of the fit (kept at 0).
    /// Several (C, L) pairs can give the same result (the measurement only fixes the impedance the component sees),
    /// so physical, non-negative values are preferred when they fit (almost) as well.
    /// </summary>
    public static (double C, double L, double rms) Fit(Element template, IList<(double value, Complex z)> meas,
                                                       Complex zBefore, double freq, bool fitC, bool fitL)
    {
        var free = Search(template, meas, zBefore, freq, fitC, fitL, positive: false);
        var phys = Search(template, meas, zBefore, freq, fitC, fitL, positive: true);
        return phys.rms <= free.rms * 1.05 + 0.05 ? phys : free;
    }

    static (double C, double L, double rms) Search(Element template, IList<(double value, Complex z)> meas,
                                                   Complex zBefore, double freq, bool fitC, bool fitL, bool positive)
    {
        // parameters in pF and nH so that both have similar scales; |p| keeps them positive when asked
        double P(double v) => positive ? Math.Abs(v) : v;
        double Cost(double[] p) => Rms(template, meas, fitC ? P(p[0]) * 1e-12 : 0, fitL ? P(p[1]) * 1e-9 : 0, zBefore, freq);
        (double[] x, double f) best = (new[] { 0.0, 0.0 }, Cost(new[] { 0.0, 0.0 }));
        if (!fitC && !fitL) return (0, 0, best.f);
        foreach (double c0 in fitC ? new[] { 0.0, 0.5, 2, -0.5 } : new[] { 0.0 })
            foreach (double l0 in fitL ? new[] { 0.0, 2, 8, -2 } : new[] { 0.0 })
            {
                var r = Minimizer.Minimize(Cost, new[] { c0, l0 }, 0.5, 800);
                if (r.f < best.f) best = r;
            }
        return (fitC ? P(best.x[0]) * 1e-12 : 0, fitL ? P(best.x[1]) * 1e-9 : 0, best.f);
    }
}
