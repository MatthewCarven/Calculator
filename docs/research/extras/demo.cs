#:project C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\extras\NaturalCopy\src\Natural\Natural.csproj
// Checks the extras' claims against the library. One evaluator over four number backends:
// float at p bits, an IEEE format with flags, a directed-rounding interval, an exact rational.
// Sections are picked by name on the command line, so a slow one can be run on its own.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Natural;
using Natural.Jobs;

string want = args.Length > 0 ? args[0] : "all";
void Section(string name, Action a)
{
    if (want != "all" && want != name) return;
    Console.WriteLine($"=== {name} ===");
    long t0 = Stopwatch.GetTimestamp();
    a();
    Console.WriteLine($"--- {name}: {Stopwatch.GetElapsedTime(t0).TotalSeconds:0.000} s");
    Console.Out.Flush();
}

Section("rump", Tests.Rump);
Section("muller", Tests.Muller);
Section("zero", Tests.ZeroTest);
Section("formats", Tests.Formats);
Section("mix", Tests.Contamination);
Section("display", Tests.DisplayDigits);
Section("roundtrip", Tests.RoundTrip);
Section("identify", Tests.Identify);
Section("programmer", Tests.Programmer);
Section("huge", Tests.Huge);
Section("exactsum", Tests.ExactSums);
Section("gcd", Tests.GcdCost);
Section("numtheory", Tests.NumberTheory);
Section("bigint", Tests.BigIntDemos);
Section("trans", Tests.Transcendentals);
Section("integrate", Tests.Integrate);
Section("job", Tests.UserLoopJob);
Section("mulrand", Tests.MulRand);
Section("fresh", Tests.FreshOperands);
Section("xray", Tests.Xray);
Section("solve", Tests.Solve);
Section("matrix", Tests.Matrix);
Section("notation", Tests.Notation);
Section("certificate", Tests.Certificates);

// ---------------------------------------------------------------------------------------

interface IB<T>
{
    T Lit(string s);
    T Add(T a, T b);
    T Sub(T a, T b);
    T Mul(T a, T b);
    T Div(T a, T b);
    T Sqrt(T a);
    string Show(T a);
}

sealed class FloatB(int p, RoundingMode mode = RoundingMode.ToNearestEven) : IB<ApFloat>
{
    public ApFloat Lit(string s) => ApFloat.Parse(s, p, mode, CultureInfo.InvariantCulture);
    public ApFloat Add(ApFloat a, ApFloat b) => ApFloat.Add(a, b, p, mode);
    public ApFloat Sub(ApFloat a, ApFloat b) => ApFloat.Subtract(a, b, p, mode);
    public ApFloat Mul(ApFloat a, ApFloat b) => ApFloat.Multiply(a, b, p, mode);
    public ApFloat Div(ApFloat a, ApFloat b) => ApFloat.Divide(a, b, p, mode);
    public ApFloat Sqrt(ApFloat a) => ApFloat.Sqrt(a, p, mode);
    public string Show(ApFloat a) => a.ToString("E20");
}

readonly record struct Iv(ApFloat Lo, ApFloat Hi);

sealed class IvB(int p) : IB<Iv>
{
    const RoundingMode Down = RoundingMode.TowardNegative, Up = RoundingMode.TowardPositive;
    public Iv Lit(string s) => new(ApFloat.Parse(s, p, Down, CultureInfo.InvariantCulture), ApFloat.Parse(s, p, Up, CultureInfo.InvariantCulture));
    public Iv Add(Iv a, Iv b) => new(ApFloat.Add(a.Lo, b.Lo, p, Down), ApFloat.Add(a.Hi, b.Hi, p, Up));
    public Iv Sub(Iv a, Iv b) => new(ApFloat.Subtract(a.Lo, b.Hi, p, Down), ApFloat.Subtract(a.Hi, b.Lo, p, Up));
    public Iv Mul(Iv a, Iv b)
    {
        ApFloat[] lo = [ApFloat.Multiply(a.Lo, b.Lo, p, Down), ApFloat.Multiply(a.Lo, b.Hi, p, Down), ApFloat.Multiply(a.Hi, b.Lo, p, Down), ApFloat.Multiply(a.Hi, b.Hi, p, Down)];
        ApFloat[] hi = [ApFloat.Multiply(a.Lo, b.Lo, p, Up), ApFloat.Multiply(a.Lo, b.Hi, p, Up), ApFloat.Multiply(a.Hi, b.Lo, p, Up), ApFloat.Multiply(a.Hi, b.Hi, p, Up)];
        return new(lo.Min(), hi.Max());
    }
    public Iv Div(Iv a, Iv b)
    {
        if (b.Lo <= ApFloat.Zero && b.Hi >= ApFloat.Zero) return new(ApFloat.NegativeInfinity, ApFloat.PositiveInfinity);
        ApFloat[] lo = [ApFloat.Divide(a.Lo, b.Lo, p, Down), ApFloat.Divide(a.Lo, b.Hi, p, Down), ApFloat.Divide(a.Hi, b.Lo, p, Down), ApFloat.Divide(a.Hi, b.Hi, p, Down)];
        ApFloat[] hi = [ApFloat.Divide(a.Lo, b.Lo, p, Up), ApFloat.Divide(a.Lo, b.Hi, p, Up), ApFloat.Divide(a.Hi, b.Lo, p, Up), ApFloat.Divide(a.Hi, b.Hi, p, Up)];
        return new(lo.Min(), hi.Max());
    }
    public Iv Sqrt(Iv a) => new(ApFloat.Sqrt(a.Lo, p, Down), ApFloat.Sqrt(a.Hi, p, Up));
    public string Show(Iv a) => $"[{a.Lo.ToString("E6", null, Down)}, {a.Hi.ToString("E6", null, Up)}]  proven digits {Proven(a, ApFloat.DecimalDigitsFor(p))}";

    /// <summary>The most digits n at which both ends round to one n-digit decimal: Ziv's rule, for text.</summary>
    public static int Proven(Iv a, int max)
    {
        if (a.Lo.IsNaN || a.Hi.IsNaN || !a.Lo.IsFinite || !a.Hi.IsFinite) return 0;
        for (int n = max; n >= 1; n--)
            if (a.Lo.ToString("E" + (n - 1)) == a.Hi.ToString("E" + (n - 1))) return n;
        return 0;
    }
}

readonly record struct Rat(ApInt N, ApInt D)
{
    public static Rat Make(ApInt n, ApInt d)
    {
        if (d.IsNegative) { n = -n; d = -d; }
        ApInt g = ApInt.GreatestCommonDivisor(n, d);
        return g == ApInt.One ? new(n, d) : new(n / g, d / g);
    }
    public ApFloat ToFloat(int p, RoundingMode m = RoundingMode.ToNearestEven) => ApFloat.Divide((ApFloat)N, (ApFloat)D, p, m);
}

sealed class RatB : IB<Rat>
{
    public Rat Lit(string s)
    {
        string mant = s; long exp = 0;
        int e = s.IndexOf('e');
        if (e >= 0) { mant = s[..e]; exp = long.Parse(s[(e + 1)..]); }
        int dot = mant.IndexOf('.');
        if (dot >= 0) { exp -= mant.Length - dot - 1; mant = mant.Remove(dot, 1); }
        ApInt n = ApInt.Parse(mant);
        return exp >= 0 ? new(n * ApInt.Pow(10, (int)exp), ApInt.One) : Rat.Make(n, ApInt.Pow(10, (int)-exp));
    }
    public Rat Add(Rat a, Rat b) => Rat.Make(a.N * b.D + b.N * a.D, a.D * b.D);
    public Rat Sub(Rat a, Rat b) => Rat.Make(a.N * b.D - b.N * a.D, a.D * b.D);
    public Rat Mul(Rat a, Rat b) => Rat.Make(a.N * b.N, a.D * b.D);
    public Rat Div(Rat a, Rat b) => Rat.Make(a.N * b.D, a.D * b.N);
    public Rat Sqrt(Rat a) => throw new NotSupportedException();
    public string Show(Rat a) => a.ToFloat(400).ToString("E20") + $"  ({a.N.BitLength}/{a.D.BitLength} bits)";
}

static class Tests
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // f(a, b) = 333.75 b^6 + a^2 (11 a^2 b^2 - b^6 - 121 b^4 - 2) + 5.5 b^8 + a / (2 b)
    static T RumpExpr<T>(IB<T> k)
    {
        T a = k.Lit("77617"), b = k.Lit("33096");
        T b2 = k.Mul(b, b), b4 = k.Mul(b2, b2), b6 = k.Mul(b4, b2), b8 = k.Mul(b4, b4), a2 = k.Mul(a, a);
        T inner = k.Sub(k.Sub(k.Sub(k.Mul(k.Mul(k.Lit("11"), a2), b2), b6), k.Mul(k.Lit("121"), b4)), k.Lit("2"));
        T sum = k.Add(k.Add(k.Mul(k.Lit("333.75"), b6), k.Mul(a2, inner)), k.Mul(k.Lit("5.5"), b8));
        return k.Add(sum, k.Div(a, k.Mul(k.Lit("2"), b)));
    }

    public static void Rump()
    {
        var exact = new RatB();
        Rat truth = RumpExpr(exact);
        Console.WriteLine($"exact: {truth.N}/{truth.D} = {truth.ToFloat(256).ToString("E30")}");
        foreach (int p in new[] { 24, 53, 64, 80, 96, 106, 113, 120, 121, 122, 123, 124, 128, 160, 256 })
        {
            ApFloat f = RumpExpr(new FloatB(p));
            var iv = new IvB(p);
            Iv e = RumpExpr(iv);
            ApFloat err = ApFloat.Abs(ApFloat.Subtract(f, truth.ToFloat(600), 600));
            Console.WriteLine($"p={p,4}  float {f.ToString("E16"),24}   interval width {ApFloat.Subtract(e.Hi, e.Lo, 64, RoundingMode.TowardPositive).ToString("E2"),10}   proven {IvB.Proven(e, ApFloat.DecimalDigitsFor(p)),3}");
        }
        // what one evaluation costs, each backend, interleaved
        double[][] t = [new double[7], new double[7], new double[7]];
        for (int r = 0; r < 7; r++)
        {
            t[0][r] = Us(() => RumpExpr(new FloatB(256)));
            t[1][r] = Us(() => RumpExpr(new IvB(256)));
            t[2][r] = Us(() => RumpExpr(new RatB()));
        }
        Console.WriteLine($"one Rump evaluation (22 ops), median of 7 interleaved: float256 {Med(t[0]):0} us, interval256 {Med(t[1]):0} us, exact rational {Med(t[2]):0} us");
    }

    static T MullerN<T>(IB<T> k, int n)
    {
        T u0 = k.Lit("2"), u1 = k.Lit("-4");
        T c1 = k.Lit("111"), c2 = k.Lit("1130"), c3 = k.Lit("3000");
        for (int i = 2; i <= n; i++)
        {
            T u2 = k.Add(k.Sub(c1, k.Div(c2, u1)), k.Div(c3, k.Mul(u1, u0)));
            u0 = u1; u1 = u2;
        }
        return u1;
    }

    public static void Muller()
    {
        var exact = new RatB();
        foreach (int n in new[] { 15, 30, 60, 100 })
        {
            Rat truth = MullerN(exact, n);
            Console.WriteLine($"n={n}: exact {exact.Show(truth)}");
            foreach (int p in new[] { 53, 106, 212, 424, 848 })
            {
                ApFloat f = MullerN(new FloatB(p), n);
                var iv = new IvB(p);
                Iv e = MullerN(iv, n);
                string width = e.Lo.IsFinite && e.Hi.IsFinite ? ApFloat.Subtract(e.Hi, e.Lo, 64, RoundingMode.TowardPositive).ToString("E2") : "unbounded";
                Console.WriteLine($"   p={p,4}  float {f.ToString("E20"),30}   interval width {width,10}  proven {IvB.Proven(e, ApFloat.DecimalDigitsFor(p)),3}");
            }
        }
    }

    public static void ZeroTest()
    {
        foreach (int p in new[] { 53, 256, 4096 })
        {
            var f = new FloatB(p);
            ApFloat r = f.Sqrt(f.Lit("2"));
            ApFloat z = f.Sub(f.Mul(r, r), f.Lit("2"));
            var iv = new IvB(p);
            Iv ri = iv.Sqrt(iv.Lit("2"));
            Iv zi = iv.Sub(iv.Mul(ri, ri), iv.Lit("2"));
            Console.WriteLine($"sqrt(2)^2-2 p={p}: float {z.ToString("E6")}  interval [{zi.Lo.ToString("E3")}, {zi.Hi.ToString("E3")}] contains 0: {zi.Lo <= ApFloat.Zero && zi.Hi >= ApFloat.Zero}");
        }
        // Rational square-root exactness: ApInt.SqrtRem's remainder settles perfect squares.
        foreach (string s in new[] { "16/9", "2/1", "1000000/1" })
        {
            var parts = s.Split('/');
            var (rn, en) = ApInt.SqrtRem(ApInt.Parse(parts[0]));
            var (rd, ed) = ApInt.SqrtRem(ApInt.Parse(parts[1]));
            Console.WriteLine($"sqrt({s}): {(en.IsZero && ed.IsZero ? $"exact {rn}/{rd}" : "irrational (or needs lowest terms first)")}");
        }
        // Ziv on a whole expression: raise p until 40 digits are proven.
        var sb = new StringBuilder();
        for (int p = 64; p <= 1024; p *= 2)
        {
            Iv e = RumpExpr(new IvB(p));
            int d = IvB.Proven(e, 40);
            sb.Append($"p={p}:{d} ");
        }
        Console.WriteLine("Ziv rounds on Rump, proven of 40 digits: " + sb);
    }

    public static void Formats()
    {
        (string, IeeeFormat)[] formats = [("binary16", IeeeFormat.Binary16), ("binary32", IeeeFormat.Binary32), ("binary64", IeeeFormat.Binary64),
            ("binary128", IeeeFormat.Binary128), ("binary256", IeeeFormat.Binary256), ("bfloat16", new IeeeFormat(8, 8))];
        var exact = new RatB();
        Rat truth = exact.Add(exact.Lit("0.1"), exact.Lit("0.2"));
        foreach (var (name, fmt) in formats)
        {
            var ctx = new IeeeContext(fmt);
            ApFloat a = ctx.Parse("0.1", Inv), b = ctx.Parse("0.2", Inv);
            ApFloat r = ctx.Add(a, b);
            Rat rr = ToRat(r);
            Rat diff = exact.Sub(rr, truth);
            ApFloat err = diff.ToFloat(64);
            long ulpExp = r.Exponent + r.Significand.BitLength - fmt.Precision;
            ApFloat ulp = new(ApInt.One, ulpExp, 64);
            ApFloat inUlps = ApFloat.Divide(err, ulp, 64);
            Console.WriteLine($"{name,-10} {r.ToString("R"),-40} err {err.ToString("E3"),12}  {inUlps.ToString("F3"),8} ulp  flags {ctx.Flags}");
        }
        Console.WriteLine($"double hardware {0.1 + 0.2:R}, float {0.1f + 0.2f:R}, Half {(Half.Parse("0.1", Inv) + Half.Parse("0.2", Inv)).ToString("R", Inv)}");
        var d = new IeeeContext(IeeeFormat.Binary64);
        ApFloat dr = d.Add(d.Parse("0.1", Inv), d.Parse("0.2", Inv));
        Console.WriteLine($"binary64 bits equal hardware: {BitConverter.DoubleToInt64Bits((double)dr) == BitConverter.DoubleToInt64Bits(0.1 + 0.2)}");
        Console.WriteLine($"what binary64 stores for 0.1, exactly: {ApFloat.Parse("0.1", IeeeFormat.Binary64).ToString("F55")}");
        foreach (RoundingMode m in Enum.GetValues<RoundingMode>())
        {
            var c = new IeeeContext(IeeeFormat.Binary32, m);
            ApFloat r = c.Add(c.Parse("0.1", Inv), c.Parse("0.2", Inv));
            Console.WriteLine($"binary32 {m,-15} {r.ToString("R"),-14} hex {r.ToString("A"),-18} flags {c.Flags}");
        }
        // Lamps in precision mode against format mode
        var cp = new IeeeContext(256); cp.Multiply(ApFloat.Parse("1e300", 256), ApFloat.Parse("1e300", 256));
        var cf = new IeeeContext(IeeeFormat.Binary64); cf.Multiply(1e300, 1e300);
        Console.WriteLine($"1e300*1e300: precision 256 flags [{cp.Flags}]  binary64 flags [{cf.Flags}]");
        var cz = new IeeeContext(IeeeFormat.Binary64);
        cz.Multiply(1e-300, 1e-300); IeeeFlags f1 = cz.Flags; cz.ClearFlags();
        cz.Divide(1.0, 0.0); IeeeFlags f2 = cz.Flags; cz.ClearFlags();
        cz.Sqrt(-1.0); IeeeFlags f3 = cz.Flags;
        Console.WriteLine($"binary64: 1e-300*1e-300 [{f1}]  1/0 [{f2}]  sqrt(-1) [{f3}]");
    }

    static Rat ToRat(ApFloat x)
    {
        ApInt m = x.IsNegative ? -x.Significand : x.Significand;
        long e = x.Exponent;
        return e >= 0 ? new(m << (int)e, ApInt.One) : Rat.Make(m, ApInt.One << (int)-e);
    }

    public static void Contamination()
    {
        ApFloat ans53 = ApFloat.Parse("0.1", 53);
        ApFloat byOperator = ans53 * ApFloat.Parse("3", 256);
        ApFloat byStatic = ApFloat.Multiply(ans53, ApFloat.Parse("3", 256), 256);
        ApFloat clean = ApFloat.Multiply(ApFloat.Parse("0.1", 256), ApFloat.Parse("3", 256), 256);
        Console.WriteLine($"UP   Ans@53 then *3 at 256: precision {byOperator.Precision}: {byOperator.ToString("E40")}");
        Console.WriteLine($"     static form agrees: {byStatic.Equals(byOperator)}");
        Console.WriteLine($"     fresh at 256:                  {clean.ToString("E40")}");
        ApFloat ans256 = ApFloat.Parse("0.1", 256);
        ApFloat carried = ApFloat.Add(ans256, ApFloat.Parse("0.2", 53), 53);
        ApFloat freshDown = ApFloat.Add(ApFloat.Parse("0.1", 53), ApFloat.Parse("0.2", 53), 53);
        Console.WriteLine($"DOWN Ans@256 (0.1) then +0.2 at 53: {carried.ToString("R")}   fresh at 53: {freshDown.ToString("R")}   equal: {carried.Equals(freshDown)}");
        ApFloat op = ans256 + ApFloat.Parse("0.2", 53);
        Console.WriteLine($"     the plain + operator keeps {op.Precision} bits, though the setting says 53");
    }

    public static void DisplayDigits()
    {
        var exact = new RatB();
        foreach (int p in new[] { 53, 256 })
        {
            int round = ApFloat.DecimalDigitsFor(p);
            int safe = (int)Math.Floor((p - 1) * Math.Log10(2));
            foreach (var (n, d) in new[] { (1, 3), (2, 3), (1, 7) })
            {
                ApFloat f = ApFloat.Divide(n, d, p);
                Console.WriteLine($"p={p} {n}/{d}: round-trip {round} digits {f.ToString("E" + (round - 1))}");
                Console.WriteLine($"          floor((p-1)log10 2) = {safe} digits {f.ToString("E" + (safe - 1))}");
            }
        }
    }

    public static void RoundTrip()
    {
        var rng = new Random(7);
        foreach (int p in new[] { 53, 256, 4096 })
        {
            int badA = 0, badR = 0, n = p > 1000 ? 40 : 300;
            for (int i = 0; i < n; i++)
            {
                var bytes = new byte[p / 8 + 1];
                rng.NextBytes(bytes);
                ApFloat x = new ApFloat(ApInt.FromMagnitude(bytes, rng.Next(2) == 0), rng.Next(-5000, 5000), p);
                ApFloat ra = ApFloat.Parse(x.ToString("A", Inv), p, RoundingMode.ToNearestEven, Inv);
                ApFloat rr = ApFloat.Parse(x.ToString("R", Inv), p, RoundingMode.ToNearestEven, Inv);
                if (!Same(x, ra)) badA++;
                if (!Same(x, rr)) badR++;
            }
            Console.WriteLine($"p={p}: {n} random values, A text failed {badA}, R text failed {badR}");
        }
        foreach (ApFloat s in new[] { ApFloat.PositiveInfinity, ApFloat.NegativeInfinity, ApFloat.NaN, ApFloat.NegativeZero })
        {
            string t = s.ToString("A", Inv);
            string outcome;
            try { outcome = ApFloat.Parse(t, 256, RoundingMode.ToNearestEven, Inv).ToString("R", Inv); } catch (Exception e) { outcome = e.GetType().Name; }
            Console.WriteLine($"special {s.ToString("R", Inv)}: A text '{t}' reads back {outcome}");
        }
    }

    static bool Same(ApFloat a, ApFloat b) => a.Significand == b.Significand && a.Exponent == b.Exponent && a.IsNegative == b.IsNegative;

    /// <summary>The simplest fraction within half an ulp of x: continued fractions on the exact value.</summary>
    static (ApInt, ApInt) Simplest(ApFloat x, int p)
    {
        Rat v = ToRat(ApFloat.Abs(x));
        ApFloat tol = new(ApInt.One, x.Exponent + x.Significand.BitLength - p - 1, 64);
        ApInt h0 = ApInt.Zero, h1 = ApInt.One, k0 = ApInt.One, k1 = ApInt.Zero, n = v.N, d = v.D;
        while (!d.IsZero)
        {
            var (q, r) = ApInt.DivRem(n, d);
            (h0, h1) = (h1, q * h1 + h0);
            (k0, k1) = (k1, q * k1 + k0);
            (n, d) = (d, r);
            ApFloat c = ApFloat.Divide((ApFloat)h1, (ApFloat)k1, p + 80);
            if (ApFloat.Abs(ApFloat.Subtract(c, x, p + 80)) <= tol) break;
        }
        return (h1, k1);
    }

    public static void Identify()
    {
        foreach (var (name, text) in new[] { ("0.1", "0.1"), ("1/3 as double", "0.333333333333333314829616256247"), ("0.142857142857142857", "0.142857142857142857"), ("pi to 15 places", "3.141592653589793"), ("1.5707963267948966", "1.5707963267948966") })
        {
            ApFloat x = ApFloat.Parse(text, 53);
            var (h, k) = Simplest(x, 53);
            Console.WriteLine($"{name,-24} -> {h}/{k}");
        }
        ApFloat d = ApFloat.Parse("0.1", 53);
        Rat exact = ToRat(d);
        Console.WriteLine($"binary64 0.1 is exactly {exact.N}/{exact.D}");
    }

    public static void Programmer()
    {
        ApInt Mask(int w) => (ApInt.One << w) - ApInt.One;
        ApInt Wrap(ApInt x, int w) => x & Mask(w);
        ApInt Rotl(ApInt x, int k, int w) => Wrap((x << k) | (x >> (w - k)), w);
        ApInt Signed(ApInt x, int w) => (x >> (w - 1)).IsZero ? x : x - (ApInt.One << w);
        string Hex(ApInt x) { string s = x.ToString("X").TrimStart('0'); return s.Length == 0 ? "0" : s; }
        int w8 = 8, w = 1024;
        Console.WriteLine($"w=8: -1 wraps {Wrap(-1, w8)}, 0x80 signed {Signed(128, w8)}, rotl(0x81,1) {Rotl(0x81, 1, w8)}");
        ApInt big = Wrap(-1, w);
        Console.WriteLine($"w=1024: ~0 has {big.BitLength} bits, hex head {Hex(big)[..8]}, length {Hex(big).Length}, signed {Signed(big, w)}");
        Console.WriteLine($"quirks that rule out the generic members: PopCount(-1)={ApInt.PopCount(-1)}, RotateLeft(1,40)={ApInt.RotateLeft(1, 40)}, -255 X='{((ApInt)(-255)).ToString("X")}', 255 X='{((ApInt)255).ToString("X")}'");
        // Timing, interleaved
        ApInt a = ApInt.Pow(3, 1400), b = ApInt.Pow(7, 700);
        double[] xor = new double[7], shift = new double[7], mul = new double[7];
        for (int r = 0; r < 7; r++)
        {
            xor[r] = Us(() => { for (int i = 0; i < 1000; i++) Wrap(a ^ b, 4096); }) / 1000;
            shift[r] = Us(() => { for (int i = 0; i < 1000; i++) Rotl(a, 123, 4096); }) / 1000;
            mul[r] = Us(() => { for (int i = 0; i < 100; i++) Wrap(a * b, 4096); }) / 100;
        }
        Console.WriteLine($"4096-bit: xor+mask {Med(xor):0.0} us, rotl {Med(shift):0.0} us, mul+mask {Med(mul):0} us");
    }

    public static void Huge()
    {
        for (int i = 0; i < 300; i++) { ApFloat.Parse("1e1000000000000000000", 256).ToString("E20"); ApFloat.Parse("3.7e-999999999999", 53); }
        double t = Med(Enumerable.Range(0, 9).Select(_ => Us(() => ApFloat.Parse("1e1000000000000000000", 256))).ToArray());
        ApFloat x = ApFloat.Parse("1e1000000000000000000", 256);
        ApFloat y = ApFloat.Parse("1e-1000000000000000000", 256);
        Console.WriteLine($"parse 1e(10^18) {t:0} us; printed {x.ToString("E6")}; x*y = {ApFloat.Multiply(x, y, 256).ToString("E20")}");
        double f = Med(Enumerable.Range(0, 9).Select(_ => Us(() => x.ToString("E20"))).ToArray());
        Console.WriteLine($"format E20 {f:0} us");
        double p3 = Med(Enumerable.Range(0, 9).Select(_ => Us(() => ApFloat.Parse("3.7e-999999999999", 53))).ToArray());
        Console.WriteLine($"parse 3.7e-999999999999 {p3:0} us");
        // The exact-first trap: the same literal as an integer would need this many bits.
        Console.WriteLine($"as an exact integer 1e(10^18) would need {1e18 * Math.Log2(10):E2} bits; ApFloat.MaxPrecision is {ApFloat.MaxPrecision}");
    }

    public static void ExactSums()
    {
        foreach (int n in new[] { 100, 300, 1000, 2000 })
        {
            var t = Stopwatch.StartNew();
            ApInt num = ApInt.Zero, den = ApInt.One;
            for (int k = 1; k <= n; k++)
            {
                Rat r = Rat.Make(num * k + den, den * k);
                (num, den) = (r.N, r.D);
            }
            t.Stop();
            double exactMs = t.Elapsed.TotalMilliseconds;
            var f = Stopwatch.StartNew();
            ApFloat s = ApFloat.Zero;
            for (int k = 1; k <= n; k++) s = ApFloat.Add(s, ApFloat.Divide(1, k, 256), 256);
            f.Stop();
            ApFloat truth = new Rat(num, den).ToFloat(600);
            ApFloat err = ApFloat.Abs(ApFloat.Subtract(s, truth, 600));
            Console.WriteLine($"H_{n}: exact {exactMs:0.0} ms, den {den.BitLength} bits; 256-bit float sum {f.Elapsed.TotalMilliseconds:0.00} ms, value {s.ToString("E20")}, error {err.ToString("E2")}");
        }
    }

    public static void GcdCost()
    {
        var rng = new Random(3);
        Console.WriteLine("Stein gcd of two random n-bit values, then a schoolbook product and exact division, median of 9, interleaved");
        int[] sizes = [64, 256, 1024, 4096, 16384];
        var g = sizes.ToDictionary(s => s, _ => new List<double>());
        var m = sizes.ToDictionary(s => s, _ => new List<double>());
        for (int r = 0; r < 9; r++)
            foreach (int s in sizes)
            {
                ApInt a = Rand(rng, s) | ApInt.One, b = Rand(rng, s) | ApInt.One;
                int reps = s <= 256 ? 200 : s <= 1024 ? 20 : s <= 4096 ? 4 : 1;
                g[s].Add(Us(() => { for (int i = 0; i < reps; i++) ApInt.GreatestCommonDivisor(a, b); }) / reps);
                m[s].Add(Us(() => { for (int i = 0; i < reps; i++) _ = a * b; }) / reps);
            }
        foreach (int s in sizes) Console.WriteLine($"{s,6} bits: gcd {Med(g[s].ToArray()):0.0} us, multiply {Med(m[s].ToArray()):0.0} us");
    }

    static ApInt Rand(Random rng, int bits)
    {
        var bytes = new byte[bits / 8];
        rng.NextBytes(bytes);
        return ApInt.FromMagnitude(bytes);
    }

    static bool MillerRabin(ApInt n, int[] bases)
    {
        if (n < 2) return false;
        ApInt d = n - 1; int s = 0;
        while ((d & 1).IsZero) { d >>= 1; s++; }
        foreach (int a in bases)
        {
            if ((n % a).IsZero) return n == a;
            ApInt x = ApInt.ModPow(a, d, n);
            if (x == ApInt.One || x == n - 1) continue;
            bool composite = true;
            for (int r = 1; r < s && composite; r++)
            {
                x = x * x % n;
                if (x == n - 1) composite = false;
            }
            if (composite) return false;
        }
        return true;
    }

    static ApInt Rho(ApInt n, int c, out long steps)
    {
        ApInt x = 2, y = 2, q = ApInt.One, g = ApInt.One;
        steps = 0;
        while (g == ApInt.One)
        {
            for (int i = 0; i < 128 && !q.IsZero; i++)
            {
                x = (x * x + c) % n;
                y = (y * y + c) % n; y = (y * y + c) % n;
                ApInt diff = x > y ? x - y : y - x;
                q = q * diff % n;
                steps++;
            }
            g = q.IsZero ? n : ApInt.GreatestCommonDivisor(q, n);
        }
        return g;
    }

    /// <summary>(g, x) with a·x + n·y = g, by extended Euclid on DivRem.</summary>
    static ApInt ModInverse(ApInt a, ApInt n)
    {
        ApInt r0 = n, r1 = a % n, t0 = ApInt.Zero, t1 = ApInt.One;
        while (!r1.IsZero)
        {
            var (q, r) = ApInt.DivRem(r0, r1);
            (r0, r1) = (r1, r);
            (t0, t1) = (t1, t0 - q * t1);
        }
        if (r0 != ApInt.One) throw new ArithmeticException("no inverse");
        return t0.IsNegative ? t0 + n : t0;
    }

    public static void NumberTheory()
    {
        var rng = new Random(11);
        for (int w = 0; w < 400; w++) ApInt.ModPow(12345, 67891, 1000003);   // past tier 0
        Console.WriteLine("ModPow, random full-size odd modulus and exponent, one call (JIT warmed):");
        foreach (int s in new[] { 64, 256, 512, 1024, 2048 })
        {
            ApInt n = Rand(rng, s) | (ApInt.One << (s - 1)) | ApInt.One, e = Rand(rng, s) | ApInt.One, b = Rand(rng, s) | ApInt.One;
            double[] t = new double[s >= 2048 ? 1 : 3];
            for (int r = 0; r < t.Length; r++) t[r] = Us(() => ApInt.ModPow(b, e, n));
            Console.WriteLine($"{s,5} bits: {Med(t) / 1000:0.0} ms");
        }
        int[] few = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37];
        ApInt m127 = (ApInt.One << 127) - 1, m521 = (ApInt.One << 521) - 1;
        foreach (var (name, v) in new[] { ("2^127-1", m127), ("2^521-1", m521) })
        {
            bool prime = false;
            double us = Us(() => prime = MillerRabin(v, few));
            Console.WriteLine($"Miller-Rabin 12 bases on {name}: {prime} in {us / 1000:0.0} ms");
        }
        ApInt p31 = 2147483647, q31 = 2147483629, p40 = 1099511627791, q40 = 1099511627689;
        Console.WriteLine($"check primes: {MillerRabin(p31, few)} {MillerRabin(q31, few)} {MillerRabin(p40, few)} {MillerRabin(q40, few)}");
        foreach (var (name, v) in new[] { ("31-bit x 31-bit", p31 * q31), ("40-bit x 40-bit", p40 * q40) })
        {
            long steps = 0; ApInt g = ApInt.One;
            double us = Us(() => g = Rho(v, 1, out steps));
            Console.WriteLine($"Pollard rho {name}: found {g} in {steps} steps, {us / 1000:0.0} ms, {us / Math.Max(1, steps):0.00} us a step");
        }
        // Trial division by odd numbers
        ApInt big = p31 * q31;
        int divs = 0;
        double td = Us(() => { for (ApInt d = 3; d < 200001; d += 2) { _ = big % d; divs++; } });
        Console.WriteLine($"trial division of a 62-bit n: {divs} odd divisors in {td / 1000:0.0} ms, {td / divs:0.00} us each");
        ApInt inv = ModInverse(12345678901, p40 * q40);
        double ti = Us(() => ModInverse(12345678901, p40 * q40));
        Console.WriteLine($"modular inverse check: {(inv * 12345678901 % (p40 * q40))}, {ti:0.0} us at 80 bits");
        ApInt rsa = (ApInt.One << 2047) + 981;
        double t2k = Us(() => ModInverse(ApInt.Pow(3, 1200) + 1, rsa));
        Console.WriteLine($"modular inverse at 2048 bits: {t2k / 1000:0.0} ms");
    }

    public static void BigIntDemos()
    {
        foreach (int n in new[] { 1000, 10000, 20000 })
        {
            ApInt f = ApInt.One;
            double us = Us(() => { f = ApInt.One; for (int k = 2; k <= n; k++) f *= k; });
            string text = "";
            double ps = Us(() => text = f.ToString());
            Console.WriteLine($"{n}!: {f.BitLength} bits, {text.Length} digits; multiply {us / 1000:0.0} ms, decimal text {ps / 1000:0.0} ms");
        }
        foreach (var (name, b, e) in new[] { ("3^30000", 3, 30000), ("3^100000", 3, 100000) })
        {
            ApInt v = ApInt.One;
            double us = Us(() => v = ApInt.Pow(b, e));
            Console.WriteLine($"{name}: {v.BitLength} bits in {us / 1000:0.0} ms");
        }
        ApInt p = ApInt.One << 300000;
        var job = ((ApFloat)p).StartFormat("F0");
        int slices = 0;
        var t = Stopwatch.StartNew();
        double firstPercent = -1;
        while (!job.Continue(TimeSpan.FromMilliseconds(50)) && slices < 400) { slices++; if (firstPercent < 0) firstPercent = job.Progress; }
        Console.WriteLine($"2^300000 decimal by StartFormat job: {(job.IsComplete ? job.Result.Length + " chars" : "not finished")} after {t.Elapsed.TotalSeconds:0.0} s, {slices} 50 ms slices, first progress {firstPercent:P1}, last {job.Progress:P1}");
        job.Dispose();
    }

    // --- rough transcendentals, for cost only: fixed working precision, no proof of rounding ---

    static ApFloat Scale(ApFloat x, int k, int w) => ApFloat.Multiply(x, new ApFloat(ApInt.One, k, 64), w);

    public static long Ops;

    public static ApFloat Exp(ApFloat x, int p)
    {
        int w = p + 40;
        int k = (int)Math.Sqrt(w) + 2;
        ApFloat r = Scale(x, -k, w);
        ApFloat sum = 1, term = 1;
        for (int i = 1; i < 10000; i++)
        {
            term = ApFloat.Divide(ApFloat.Multiply(term, r, w), i, w);
            Ops++;
            sum = ApFloat.Add(sum, term, w);
            if (term.IsZero || term.Exponent + term.Significand.BitLength < -w - 8) break;
        }
        for (int i = 0; i < k; i++) { sum = ApFloat.Multiply(sum, sum, w); Ops++; }
        return sum;
    }

    public static ApFloat Sin(ApFloat x, int p)
    {
        int w = p + 40;
        ApFloat x2 = ApFloat.Multiply(x, x, w), term = x, sum = x;
        for (int i = 1; i < 10000; i++)
        {
            term = -ApFloat.Divide(ApFloat.Multiply(term, x2, w), (long)(2 * i * (2 * i + 1)), w);
            Ops++;
            sum = ApFloat.Add(sum, term, w);
            if (term.IsZero || term.Exponent + term.Significand.BitLength < -w - 8) break;
        }
        return sum;
    }

    public static ApFloat Log(ApFloat x, int p)
    {
        int w = p + 40;
        ApFloat y = Math.Log((double)x);
        for (int bits = 50; ; bits *= 3)
        {
            int ww = Math.Min(w, bits * 3 + 40);
            ApFloat e = Exp(y, ww);
            y = ApFloat.Add(y, ApFloat.Divide(ApFloat.Multiply(ApFloat.Subtract(x, e, ww), 2, ww), ApFloat.Add(x, e, ww), ww), ww);
            if (ww == w) break;
        }
        return y;
    }

    public static ApInt Machin(int bits)
    {
        ApInt one = ApInt.One << (bits + 32);
        ApInt Atan(int inv)
        {
            ApInt term = one / inv, sum = term, sq = inv * inv;
            for (int n = 3; !term.IsZero; n += 2)
            {
                term /= sq;
                ApInt t = term / n;
                sum = (n & 2) != 0 ? sum - t : sum + t;
            }
            return sum;
        }
        return ((Atan(5) * 4 - Atan(239)) * 4) >> 32;
    }

    public static void Transcendentals()
    {
        int[] ps = [64, 256, 1024, 4096];
        Console.WriteLine("rough exp, sin, log by Taylor and Newton, single evaluation, median of 5 interleaved; cost only, rounding not proven");
        var e = ps.ToDictionary(p => p, _ => new List<double>());
        var s = ps.ToDictionary(p => p, _ => new List<double>());
        var l = ps.ToDictionary(p => p, _ => new List<double>());
        var o = new Dictionary<int, long>();
        for (int r = 0; r < 5; r++)
            foreach (int p in ps)
            {
                ApFloat x = ApFloat.Divide(5, 7, p + 40);
                Ops = 0;
                e[p].Add(Us(() => Exp(x, p)));
                o[p] = Ops;
                s[p].Add(Us(() => Sin(x, p)));
                l[p].Add(Us(() => Log(ApFloat.Divide(22, 7, p + 40), p)));
            }
        foreach (int p in ps)
            Console.WriteLine($"{p,5} bits: exp {Med(e[p].ToArray()) / 1000:0.000} ms ({o[p]} mul-ish ops), sin {Med(s[p].ToArray()) / 1000:0.000} ms, log {Med(l[p].ToArray()) / 1000:0.000} ms");
        ApFloat check = Exp(1, 256);
        Console.WriteLine($"e at 256 bits: {check.ToString("E40")}");
        Console.WriteLine($"sin(5/7): {Sin(ApFloat.Divide(5, 7, 300), 256).ToString("E30")} (Math.Sin {Math.Sin(5.0 / 7):R})");
        foreach (int digits in new[] { 1000, 10000 })
        {
            int bits = (int)(digits * 3.3219281) + 16;
            ApInt pi = ApInt.Zero;
            double us = Us(() => pi = Machin(bits));
            ApFloat v = new(pi, -bits, bits);
            string text = "";
            double f = Us(() => text = v.ToString("E" + digits));
            Console.WriteLine($"pi to {digits} digits by Machin: {us / 1000:0.0} ms, text {f / 1000:0.0} ms, tail ...{text.Substring(digits - 8, 10)}");
        }
        foreach (int p in new[] { 64, 256 })
        {
            var t = Stopwatch.StartNew();
            ApFloat acc = ApFloat.Zero;
            for (int i = 1; i <= 800; i++)
            {
                ApFloat x = ApFloat.Divide(i, 300, p + 40);
                acc = ApFloat.Add(acc, ApFloat.Divide(Sin(x, p), x, p + 40), p);
            }
            Console.WriteLine($"plot of sin(x)/x, 800 points at {p} bits: {t.Elapsed.TotalMilliseconds:0.0} ms");
        }
        var d = Stopwatch.StartNew();
        double dacc = 0;
        for (int i = 1; i <= 800; i++) { double x = i / 300.0; dacc += Math.Sin(x) / x; }
        Console.WriteLine($"same in hardware double: {d.Elapsed.TotalMilliseconds:0.000} ms ({dacc:0.0})");
    }

    public static void Integrate()
    {
        // Tanh-sinh on 4/(1+x^2) over [0,1], which is pi. Rough: cost and digits only.
        foreach (int p in new[] { 64, 128, 256 })
        {
            var t = Stopwatch.StartNew();
            int w = p + 40;
            ApFloat piHalf = new ApFloat(Machin(w + 16), -(w + 16) - 1, w);
            ApFloat F(ApFloat x) => ApFloat.Divide(4, ApFloat.Add(1, ApFloat.Multiply(x, x, w), w), w);
            ApFloat prev = ApFloat.NaN, cur = ApFloat.Zero;
            int evals = 0, level = 0;
            for (level = 1; level <= 9; level++)
            {
                ApFloat h = new(ApInt.One, -level, 64), sum = ApFloat.Zero;
                int stepK = 1;
                for (int sign = -1; sign <= 1; sign += 2)
                    for (int k = sign < 0 ? 1 : 0; ; k += stepK)
                    {
                        ApFloat ks = ApFloat.Multiply(k * sign, h, w);
                        ApFloat e = Exp(ks, w), ei = ApFloat.Divide(1, e, w);
                        ApFloat sh = Scale(ApFloat.Subtract(e, ei, w), -1, w), ch = Scale(ApFloat.Add(e, ei, w), -1, w);
                        ApFloat u = ApFloat.Multiply(piHalf, sh, w);
                        ApFloat eu = Exp(u, w), eui = ApFloat.Divide(1, eu, w);
                        ApFloat shu = Scale(ApFloat.Subtract(eu, eui, w), -1, w), chu = Scale(ApFloat.Add(eu, eui, w), -1, w);
                        ApFloat x = ApFloat.Divide(shu, chu, w);
                        ApFloat weight = ApFloat.Divide(ApFloat.Multiply(piHalf, ch, w), ApFloat.Multiply(chu, chu, w), w);
                        if (!weight.IsZero && weight.Exponent + weight.Significand.BitLength < -w) break;
                        ApFloat at = Scale(ApFloat.Add(x, 1, w), -1, w);            // [-1,1] to [0,1]
                        sum = ApFloat.Add(sum, ApFloat.Multiply(weight, F(at), w), w);
                        evals++;
                    }
                cur = Scale(ApFloat.Multiply(sum, h, w), -1, w);
                if (level > 2)
                {
                    ApFloat diff = ApFloat.Abs(ApFloat.Subtract(cur, prev, w));
                    if (diff.IsZero || diff.Exponent + diff.Significand.BitLength < -p + 8) break;
                }
                prev = cur;
            }
            Console.WriteLine($"tanh-sinh at {p} bits: level {level}, {evals} evaluations, {t.Elapsed.TotalMilliseconds:0.0} ms, result {cur.ToString("E30")}");
        }
    }

    public static void UserLoopJob()
    {
        ApInt n = (ApInt.One << 2203) - 1;
        ApInt d = n - 1; int s = 0;
        while ((d & 1).IsZero) { d >>= 1; s++; }
        IEnumerable<long> Body(JobContext<bool> c)
        {
            long bits = d.BitLength;
            c.ExpectedWork = bits + s;
            ApInt result = ApInt.One, square = 3 % n;
            for (long i = 0; i < bits; i++)
            {
                if (!((d >> (int)i) & 1).IsZero) result = result * square % n;
                square = square * square % n;
                yield return 1;
            }
            c.Result = result == ApInt.One || result == n - 1;
        }
        using Job<bool> job = Job.Start<bool>(Body);
        var slice = new List<double>();
        var t = Stopwatch.StartNew();
        while (slice.Count < 12)
        {
            double us = Us(() => job.Continue(TimeSpan.FromMilliseconds(16)));
            slice.Add(us);
            if (job.IsComplete) break;
        }
        Console.WriteLine($"A Miller-Rabin base as a Natural.Jobs job on 2^2203-1: after {slice.Count} 16 ms slices, {t.ElapsedMilliseconds} ms, progress {job.Progress:P1}, steps {job.Steps}; longest slice {slice.Max() / 1000:0.0} ms");
        Console.WriteLine("stopped there on purpose: stopping is not calling Continue again; no thread to kill.");
    }

    public static void MulRand()
    {
        var rng = new Random(5);
        int[] ps = [53, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];
        var rows = new Dictionary<(string, int), List<double>>();
        void Add(string n, int p, double us) { if (!rows.TryGetValue((n, p), out var l)) rows[(n, p)] = l = []; l.Add(us); }
        for (int r = -1; r < 7; r++)
        {
            foreach (int p in ps)
            {
                ApFloat a = new(Rand(rng, p + 8) | ApInt.One, -p, p), b = new(Rand(rng, p + 8) | ApInt.One, -p, p);
                ApFloat t1 = ApFloat.Divide(1, 3, p), t2 = ApFloat.Divide(1, 7, p);
                int reps = (int)Math.Max(1L, 40000000L / ((long)p * p));
                reps = Math.Min(reps, 2000);
                Add("mul rand", p, Us(() => { for (int i = 0; i < reps; i++) ApFloat.Multiply(a, b, p); }) / reps);
                Add("mul 1/3x1/7", p, Us(() => { for (int i = 0; i < reps; i++) ApFloat.Multiply(t1, t2, p); }) / reps);
                Add("div rand", p, Us(() => { for (int i = 0; i < reps; i++) ApFloat.Divide(a, b, p); }) / reps);
                Add("sqrt rand", p, Us(() => { for (int i = 0; i < Math.Max(1, reps / 4); i++) ApFloat.Sqrt(a, p); }) / Math.Max(1, reps / 4));
            }
            if (r < 0) rows.Clear();
        }
        Console.WriteLine("us per op, median of 7 interleaved rounds");
        Console.WriteLine($"{"bits",7} {"mul rand",12} {"mul 1/3x1/7",12} {"div rand",12} {"sqrt rand",12}");
        foreach (int p in ps)
            Console.WriteLine($"{p,7} {Med(rows[("mul rand", p)].ToArray()),12:0.00} {Med(rows[("mul 1/3x1/7", p)].ToArray()),12:0.00} {Med(rows[("div rand", p)].ToArray()),12:0.00} {Med(rows[("sqrt rand", p)].ToArray()),12:0.00}");
    }

    /// <summary>Two arms on the same sizes: one pair of operands repeated, and a pool of fresh pairs.</summary>
    public static void FreshOperands()
    {
        var rng = new Random(9);
        int[] ps = [53, 128, 256, 512, 1024, 2048, 4096];
        var same = ps.ToDictionary(p => p, _ => new List<double>());
        var fresh = ps.ToDictionary(p => p, _ => new List<double>());
        const int Pool = 1024;
        var pool = ps.ToDictionary(p => p, p => Enumerable.Range(0, Pool).Select(_ => new ApFloat(Rand(rng, p + 8) | ApInt.One, -p, p)).ToArray());
        for (int r = -2; r < 9; r++)
        {
            foreach (int p in ps)
            {
                ApFloat[] a = pool[p];
                int reps = p >= 2048 ? 64 : Pool;
                ApFloat x = a[0], y = a[1];
                double u1 = Us(() => { for (int i = 0; i < reps; i++) ApFloat.Multiply(x, y, p); }) / reps;
                double u2 = Us(() => { for (int i = 0; i < reps; i++) ApFloat.Multiply(a[i], a[(i + 1) & (Pool - 1)], p); }) / reps;
                if (r >= 0) { same[p].Add(u1); fresh[p].Add(u2); }
            }
        }
        Console.WriteLine("multiply, us: same pair repeated against a pool of fresh pairs, median of 9 interleaved rounds");
        foreach (int p in ps) Console.WriteLine($"{p,6} bits: same {Med(same[p].ToArray()),10:0.00}   fresh {Med(fresh[p].ToArray()),10:0.00}   ratio {Med(fresh[p].ToArray()) / Med(same[p].ToArray()):0.00}");
    }

    static int ProvenBits(Iv v, int p)
    {
        if (!v.Lo.IsFinite || !v.Hi.IsFinite) return 0;
        if (v.Lo.IsZero && v.Hi.IsZero) return p;
        if (v.Lo <= ApFloat.Zero && v.Hi >= ApFloat.Zero) return 0;
        ApFloat w = ApFloat.Subtract(v.Hi, v.Lo, 64, RoundingMode.TowardPositive);
        if (w.IsZero) return p;
        long TopOf(ApFloat x) => x.Exponent + x.Significand.BitLength - 1;
        long lead = Math.Min(TopOf(ApFloat.Abs(v.Lo)), TopOf(ApFloat.Abs(v.Hi)));
        return (int)Math.Clamp(lead - TopOf(w), 0, p);
    }

    /// <summary>The interval backend with a log of every operation: which one spent the digits.</summary>
    sealed class TraceB(int p) : IB<Iv>
    {
        readonly IvB k = new(p);
        public readonly List<(string Op, int Before, int After, string Mid)> Log = [];
        Iv Note(string op, Iv a, Iv b, Iv r)
        {
            Log.Add((op, Math.Min(ProvenBits(a, p), ProvenBits(b, p)), ProvenBits(r, p), r.Lo.ToString("E3")));
            return r;
        }
        public Iv Lit(string s) => k.Lit(s);
        public Iv Add(Iv a, Iv b) => Note("+", a, b, k.Add(a, b));
        public Iv Sub(Iv a, Iv b) => Note("-", a, b, k.Sub(a, b));
        public Iv Mul(Iv a, Iv b) => Note("*", a, b, k.Mul(a, b));
        public Iv Div(Iv a, Iv b) => Note("/", a, b, k.Div(a, b));
        public Iv Sqrt(Iv a) => Note("sqrt", a, a, k.Sqrt(a));
        public string Show(Iv a) => k.Show(a);
    }

    public static void Xray()
    {
        foreach (int p in new[] { 100 })
        {
            var t = new TraceB(p);
            RumpExpr(t);
            Console.WriteLine($"Rump at {p} bits, op by op: operands' least proven bits -> result's proven bits");
            int i = 0;
            foreach (var (op, before, after, mid) in t.Log)
            {
                string mark = before - after >= 16 ? $"   <-- lost {before - after} bits" : "";
                Console.WriteLine($"{++i,3} {op,-4} {before,4} -> {after,4}   {mid,14}{mark}");
            }
        }
    }

    public static void Solve()
    {
        foreach (int p in new[] { 64, 256, 1024 })
        {
            var k = new IvB(p);
            Iv F(ApFloat x) { Iv v = new(x, x); return k.Sub(k.Sub(k.Mul(k.Mul(v, v), v), v), k.Lit("2")); }   // x^3 - x - 2
            ApFloat lo = 1, hi = 2;
            int evals = 0;
            var t = Stopwatch.StartNew();
            for (int i = 0; i < p + 8; i++)
            {
                ApFloat mid = ApFloat.Multiply(ApFloat.Add(lo, hi, p + 2), new ApFloat(ApInt.One, -1, 64), p);
                Iv f = F(mid);
                evals++;
                if (f.Hi < ApFloat.Zero) lo = mid;
                else if (f.Lo > ApFloat.Zero) hi = mid;
                else break;   // the sign can't be proven at this precision
            }
            t.Stop();
            bool bracket = F(lo).Hi < ApFloat.Zero && F(hi).Lo > ApFloat.Zero;
            Console.WriteLine($"x^3-x-2 at {p} bits: {evals} evaluations, {t.Elapsed.TotalMilliseconds:0.00} ms, sign change proven {bracket}, proven digits {IvB.Proven(new Iv(lo, hi), ApFloat.DecimalDigitsFor(p))}, root {lo.ToString("E25")}");
        }
    }

    public static void Matrix()
    {
        var rng = new Random(13);
        foreach (int n in new[] { 5, 10, 20, 40 })
        {
            ApInt[,] m = new ApInt[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) m[i, j] = rng.Next(-99, 100);
            var t = Stopwatch.StartNew();
            // Bareiss: fraction-free elimination; every division is exact, so no rationals and no gcd.
            ApInt prev = ApInt.One;
            int sign = 1;
            for (int k = 0; k < n - 1; k++)
            {
                if (m[k, k].IsZero)
                {
                    int sw = -1;
                    for (int r = k + 1; r < n; r++) if (!m[r, k].IsZero) { sw = r; break; }
                    if (sw < 0) { Console.WriteLine("singular"); break; }
                    for (int c = 0; c < n; c++) (m[k, c], m[sw, c]) = (m[sw, c], m[k, c]);
                    sign = -sign;
                }
                for (int i = k + 1; i < n; i++)
                    for (int j = k + 1; j < n; j++)
                        m[i, j] = (m[i, j] * m[k, k] - m[i, k] * m[k, j]) / prev;
                prev = m[k, k];
            }
            ApInt det = m[n - 1, n - 1] * sign;
            t.Stop();
            Console.WriteLine($"exact determinant {n}x{n}, entries under 100: {t.Elapsed.TotalMilliseconds:0.0} ms, result {det.BitLength} bits");
        }
    }

    public static void Notation()
    {
        ApFloat x = ApFloat.Parse("1234567.891", 256), big = ApFloat.Parse("1e30", 256);
        foreach (string f in new[] { "#,##0.000", "#,##0", "F3", "E3", "G", "R", "G10", "N2", "ENG" })
        {
            string a;
            try { a = x.ToString(f, Inv) + "   |   " + big.ToString(f, Inv); } catch (Exception e) { a = e.GetType().Name + ": " + e.Message; }
            Console.WriteLine($"{f,-10} {a}");
        }
        var de = CultureInfo.GetCultureInfo("de-DE");
        Console.WriteLine($"de-DE #,##0.000: {x.ToString("#,##0.000", de)}");
    }

    public static void Certificates()
    {
        foreach (string text in new[] { "0.1", "3.14159", "1e5000", "1e100000", "1e-100000", "1e1000000000000000000" })
        {
            var job = ApFloat.StartParse(text, 256, RoundingMode.ToNearestEven, Inv);
            job.RunToEnd();
            DecimalCertificate c = job.Certificate;
            Console.WriteLine($"parse {text,-24} rounds {c.Rounds}, working bits {c.WorkingBits}, exact route {c.Exact}, low/high {(c.Low is null ? "none" : "present")}");
        }
        ApFloat[] vs = [ApFloat.Parse("0.1", 256), ApFloat.Parse("1e100000", 256)];
        foreach (ApFloat v in vs)
        {
            var job = v.StartFormat("E40", Inv);
            job.RunToEnd();
            DecimalCertificate c = job.Certificate;
            Console.WriteLine($"format {job.Result,-52} rounds {c.Rounds}, working bits {c.WorkingBits}, exact route {c.Exact}, scale {c.Scale}");
        }
    }

    static double Us(Action a)
    {
        long t0 = Stopwatch.GetTimestamp();
        a();
        return Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000;
    }

    static double Med(double[] x)
    {
        var s = x.OrderBy(v => v).ToArray();
        return s[s.Length / 2];
    }
}
