// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Deterministic logarithms for output paths. Platform math libraries may differ by one ulp in
/// <see cref="Math.Log(double)"/> and friends, so descriptor numbers never use them.
/// <para>
/// <see cref="Log2(double)"/> uses only IEEE <c>+ - * /</c>, which are exactly rounded on every platform, plus the
/// exact integer-exponent helpers <see cref="Math.ILogB(double)"/> and <see cref="Math.ScaleB(double, int)"/>.
/// The .NET JIT does not fuse multiply and add, so the operation sequence below is the one that runs.
/// </para>
/// </summary>
internal static class DetMath
{
    /// <summary>The natural logarithm of 2, correctly rounded.</summary>
    public const double Ln2 = 0.6931471805599453;

    // 2 / ln 2 as an unevaluated sum of two doubles (about 106 bits), from mpmath at 300 bits.
    private const double TwoOverLn2Hi = 2.8853900817779268;
    private const double TwoOverLn2Lo = 4.0710547481862066e-17;

    // Reduce the mantissa into [1/sqrt 2, sqrt 2] so that |t| <= 0.1716 in the series below.
    private const double SqrtTwo = 1.4142135623730951;

    // 24 terms: the first omitted term is below 1e-37 of the sum.
    private const int SeriesTerms = 24;

    /// <summary>
    /// Base-2 logarithm.
    /// <list type="bullet">
    /// <item>Exact for powers of two, including subnormal ones.</item>
    /// <item>Otherwise <c>x = m * 2^e</c> with <c>m</c> in [1/sqrt 2, sqrt 2], and
    /// <c>log2 x = e + (2 / ln 2) * atanh((m - 1) / (m + 1))</c> with the atanh series summed in double-double
    /// arithmetic (error-free transformations built from <c>+ - * /</c>). The documented maximum relative error is
    /// 1e-12, a deliberately loose bound. The unrounded result is accurate to about 1e-30, so the returned double is the
    /// correctly rounded logarithm except for inputs within 1e-30 of a rounding boundary.</item>
    /// <item>0 gives -Infinity, a negative or NaN input gives NaN, +Infinity gives +Infinity.</item>
    /// </list>
    /// </summary>
    /// <param name="x">The argument.</param>
    /// <returns>The base-2 logarithm.</returns>
    public static double Log2(double x)
    {
        if (double.IsNaN(x) || x < 0)
        {
            return double.NaN;
        }

        if (x == 0)
        {
            return double.NegativeInfinity;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        var exponent = Math.ILogB(x);
        var mantissa = Math.ScaleB(x, -exponent);
        if (mantissa == 1.0)
        {
            return exponent;
        }

        if (mantissa > SqrtTwo)
        {
            mantissa *= 0.5;
            exponent++;
        }

        // t = (m - 1) / (m + 1). m - 1 is exact (Sterbenz); m + 1 is carried as a double-double.
        var numerator = new DoubleDouble(mantissa - 1.0, 0.0);
        var denominator = DoubleDouble.TwoSum(mantissa, 1.0);
        var t = DoubleDouble.Divide(numerator, denominator);
        var tSquared = DoubleDouble.Multiply(t, t);

        // atanh(t) = t + t^3/3 + t^5/5 + ...
        var term = t;
        var sum = t;
        for (var k = 1; k <= SeriesTerms; k++)
        {
            term = DoubleDouble.Multiply(term, tSquared);
            sum = DoubleDouble.Add(sum, DoubleDouble.Divide(term, 2.0 * k + 1.0));
        }

        var fraction = DoubleDouble.Multiply(sum, new DoubleDouble(TwoOverLn2Hi, TwoOverLn2Lo));
        return DoubleDouble.Add(fraction, exponent).Hi;
    }

    /// <summary>Natural logarithm: <c>Log2(x) * Ln2</c>. Accurate to a few ulps; deterministic everywhere.</summary>
    /// <param name="x">The argument.</param>
    /// <returns>The natural logarithm.</returns>
    public static double Ln(double x)
    {
        return Log2(x) * Ln2;
    }

    /// <summary>An unevaluated sum <c>Hi + Lo</c> with <c>|Lo| &lt;= ulp(Hi) / 2</c>.</summary>
    private readonly struct DoubleDouble
    {
        // Veltkamp splitting constant 2^27 + 1.
        private const double SplitFactor = 134217729.0;

        public DoubleDouble(double hi, double lo)
        {
            Hi = hi;
            Lo = lo;
        }

        public double Hi { get; }

        public double Lo { get; }

        public static DoubleDouble TwoSum(double a, double b)
        {
            var s = a + b;
            var bb = s - a;
            var err = (a - (s - bb)) + (b - bb);
            return new DoubleDouble(s, err);
        }

        public static DoubleDouble Add(DoubleDouble a, DoubleDouble b)
        {
            var s = TwoSum(a.Hi, b.Hi);
            var t = TwoSum(a.Lo, b.Lo);
            var normalized = QuickTwoSum(s.Hi, s.Lo + t.Hi);
            return QuickTwoSum(normalized.Hi, normalized.Lo + t.Lo);
        }

        public static DoubleDouble Add(DoubleDouble a, double b)
        {
            var s = TwoSum(a.Hi, b);
            return QuickTwoSum(s.Hi, s.Lo + a.Lo);
        }

        public static DoubleDouble Multiply(DoubleDouble a, DoubleDouble b)
        {
            var p = TwoProduct(a.Hi, b.Hi);
            return QuickTwoSum(p.Hi, p.Lo + ((a.Hi * b.Lo) + (a.Lo * b.Hi)));
        }

        public static DoubleDouble Multiply(DoubleDouble a, double b)
        {
            var p = TwoProduct(a.Hi, b);
            return QuickTwoSum(p.Hi, p.Lo + (a.Lo * b));
        }

        public static DoubleDouble Divide(DoubleDouble a, DoubleDouble b)
        {
            var q1 = a.Hi / b.Hi;
            var r = Subtract(a, Multiply(b, q1));
            var q2 = r.Hi / b.Hi;
            r = Subtract(r, Multiply(b, q2));
            var q3 = r.Hi / b.Hi;
            var q = QuickTwoSum(q1, q2);
            return Add(q, q3);
        }

        public static DoubleDouble Divide(DoubleDouble a, double b)
        {
            var q1 = a.Hi / b;
            var p = TwoProduct(q1, b);
            var s = TwoSum(a.Hi, -p.Hi);
            var e = s.Lo - p.Lo + a.Lo;
            var q2 = (s.Hi + e) / b;
            return QuickTwoSum(q1, q2);
        }

        private static DoubleDouble Subtract(DoubleDouble a, DoubleDouble b)
        {
            return Add(a, new DoubleDouble(-b.Hi, -b.Lo));
        }

        // Requires |a| >= |b| or a == 0.
        private static DoubleDouble QuickTwoSum(double a, double b)
        {
            var s = a + b;
            return new DoubleDouble(s, b - (s - a));
        }

        // Dekker's product: exact for operands whose magnitude is far from overflow.
        private static DoubleDouble TwoProduct(double a, double b)
        {
            var p = a * b;
            var (ah, al) = Split(a);
            var (bh, bl) = Split(b);
            var err = (((ah * bh) - p) + (ah * bl) + (al * bh)) + (al * bl);
            return new DoubleDouble(p, err);
        }

        private static (double Hi, double Lo) Split(double a)
        {
            var t = SplitFactor * a;
            var hi = t - (t - a);
            return (hi, a - hi);
        }
    }
}
