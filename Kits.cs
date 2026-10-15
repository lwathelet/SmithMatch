namespace SmithMatch;

/// <summary>Component values available in Würth Elektronik design kits (values read from the kit datasheets).</summary>
public static class Kits
{
    /// <summary>WE-MK SMD multilayer ceramic inductors 0402, 5 % — design kit 744784: (nH, Würth order code).</summary>
    static readonly (double nH, string pn)[] InductorParts =
    {
        (1.0, "7447840010"), (1.2, "7447840012"), (1.5, "7447840015"), (1.8, "7447840018"),
        (2.0, "7447840020"), (2.2, "7447840022"), (2.7, "7447840027"), (3.0, "7447840030"),
        (3.3, "7447840033"), (3.9, "7447840039"), (4.7, "7447840047"), (5.6, "7447840056"),
        (6.8, "7447840068"), (7.5, "7447840075"), (8.2, "7447840082"), (10.0, "7447840110"),
        (12.0, "7447840112"), (15.0, "7447840115"), (18.0, "7447840118"), (22.0, "7447840122"),
        (27.0, "7447840127"), (33.0, "7447840133"), (39.0, "7447840139"), (47.0, "7447840147"),
        (56.0, "7447840156"), (68.0, "7447840168"), (82.0, "7447840182"), (100.0, "7447840210"),
        (120.0, "7447840212"), (150.0, "7447840215"), (180.0, "7447840218"), (220.0, "7447840222"),
        (270.0, "7447840227"),
    };

    /// <summary>WCAP-CSRF high-frequency MLCC 0402, NP0, 50 V — design kit 885390 (0402 part): (pF, Würth order code).</summary>
    static readonly (double pF, string pn)[] CapacitorParts =
    {
        (0.2, "885392005146"), (0.3, "885392005001"), (0.4, "885392005002"), (0.5, "885392005004"),
        (0.7, "885392005019"), (0.8, "885392005130"), (0.9, "885392005022"), (1.0, "885392005005"),
        (1.2, "885392005024"), (1.5, "885392005007"), (1.8, "885392005008"), (2.0, "885392005030"),
        (2.2, "885392005032"), (2.3, "885392005009"), (2.4, "885392005034"), (2.7, "885392005037"),
        (3.0, "885392005010"), (3.3, "885392005011"), (3.6, "885392005044"), (3.9, "885392005047"),
        (4.3, "885392005051"), (4.7, "885392005012"), (5.1, "885392005058"), (5.6, "885392005013"),
        (6.2, "885392005068"), (6.8, "885392005074"), (7.5, "885392005081"), (8.2, "885392005088"),
        (9.1, "885392005097"), (10.0, "885392005106"), (12.0, "885392005127"), (15.0, "885392005110"),
        (18.0, "885392005147"), (20.0, "885392005131"), (22.0, "885392005114"), (27.0, "885392005116"),
    };

    public static readonly double[] Inductors = InductorParts.Select(p => p.nH * 1e-9).ToArray();
    public static readonly double[] Capacitors = CapacitorParts.Select(p => p.pF * 1e-12).OrderBy(v => v).ToArray();

    public const string InductorKit = "Würth WE-MK 0402 (744784)";
    public const string CapacitorKit = "Würth WCAP-CSRF 0402 (885390)";

    /// <summary>List used for an element type, or null for resistors (no kit).</summary>
    public static double[] For(ElementType t) => t switch
    {
        ElementType.SeriesL or ElementType.ShuntL => Inductors,
        ElementType.SeriesC or ElementType.ShuntC => Capacitors,
        _ => null,
    };

    /// <summary>Nearest kit value (on a log scale).</summary>
    public static double Snap(ElementType t, double v)
    {
        var list = For(t);
        if (list == null || v <= 0) return v;
        return list.OrderBy(x => Math.Abs(Math.Log(x / v))).First();
    }

    /// <summary>Kit value 'steps' positions away from v (v is snapped first).</summary>
    public static double Step(ElementType t, double v, int steps)
    {
        var list = For(t);
        if (list == null) return v;
        int i = Array.IndexOf(list, Snap(t, v));
        return list[Math.Clamp(i + steps, 0, list.Length - 1)];
    }

    /// <summary>Würth order code when the value is exactly a kit value, otherwise null.</summary>
    public static string PartNumber(ElementType t, double v)
    {
        if (t is ElementType.SeriesL or ElementType.ShuntL)
            return InductorParts.FirstOrDefault(p => Math.Abs(p.nH * 1e-9 / v - 1) < 1e-6).pn;
        if (t is ElementType.SeriesC or ElementType.ShuntC)
            return CapacitorParts.FirstOrDefault(p => Math.Abs(p.pF * 1e-12 / v - 1) < 1e-6).pn;
        return null;
    }
}
