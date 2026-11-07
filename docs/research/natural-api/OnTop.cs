#:project C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\natural-api\src\Natural\Natural.csproj
#:property PublishAot=false
// Proves the ON-TOP rows: every recipe here uses only Natural's public API. BigInteger, Math and
// the hardware appear only as oracles, and lines beginning "ORACLE" are checked by oracle.py against
// exact integers and Python's decimal (no third-party packages).
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Natural;
using Natural.Jobs;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = Encoding.UTF8;
string oracleFile = args.Length > 0 ? args[0] : "ontop.oracle.txt";
var oracle = new StreamWriter(oracleFile);

void H(string s) => Console.WriteLine($"\n#### {s}");
void Ok(string name, bool ok, string detail = "") => Console.WriteLine($"{(ok ? "ok  " : "BAD ")} {name} {detail}");
static long Ms(Action a) { var s = Stopwatch.StartNew(); a(); return s.ElapsedMilliseconds; }

// Exact: a hex float carries every bit, so the Python side reads the value exactly.
void Emit(string kind, string arg, int p, ApFloat v) => oracle.WriteLine($"{kind} {arg} {p} {v.ToString("A")}");

// ------------------------------------------------------------------ integers
ApInt FlooredMod(ApInt a, ApInt b) { ApInt r = a % b; return !r.IsZero && r.IsNegative != b.IsNegative ? r + b : r; }
ApInt Lcm(ApInt a, ApInt b) => a.IsZero || b.IsZero ? ApInt.Zero : ApInt.Abs(a) / ApInt.GreatestCommonDivisor(a, b) * ApInt.Abs(b);
ApInt Factorial(int n) { ApInt r = 1; for (int i = 2; i <= n; i++) r *= i; return r; }
ApInt ProductRange(long lo, long hi) => lo > hi ? ApInt.One : lo == hi ? (ApInt)lo : ProductRange(lo, (lo + hi) / 2) * ProductRange((lo + hi) / 2 + 1, hi);
ApInt NPr(long n, long r) => ProductRange(n - r + 1, n);
ApInt NCr(long n, long r) { r = Math.Min(r, n - r); ApInt c = 1; for (long i = 1; i <= r; i++) c = c * (n - r + i) / i; return c; }
ApInt ModInverse(ApInt a, ApInt m)
{
    ApInt r0 = FlooredMod(a, m), r1 = m, s0 = 1, s1 = 0;
    while (!r1.IsZero) { var (q, r) = ApInt.DivRem(r0, r1); (r0, r1) = (r1, r); (s0, s1) = (s1, s0 - q * s1); }
    if (r0 != 1) throw new ArithmeticException("not invertible");
    return FlooredMod(s0, m);
}
bool MillerRabin(ApInt n, int[] bases)
{
    if (n < 2) return false;
    foreach (int p in new[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 }) { if (n == p) return true; if ((n % p).IsZero) return false; }
    ApInt d = n - 1; int s = 0;
    while (ApInt.IsEvenInteger(d)) { d >>= 1; s++; }
    foreach (int a in bases)
    {
        ApInt x = ApInt.ModPow(a, d, n);
        if (x == 1 || x == n - 1) continue;
        bool composite = true;
        for (int i = 1; i < s && composite; i++) { x = x * x % n; if (x == n - 1) composite = false; }
        if (composite) return false;
    }
    return true;
}
ApInt PollardRho(ApInt n)
{
    for (int c = 1; ; c++)
    {
        ApInt x = 2, y = 2, d = 1;
        while (d == 1)
        {
            x = (x * x + c) % n;
            y = (y * y + c) % n; y = (y * y + c) % n;
            d = ApInt.GreatestCommonDivisor(ApInt.Abs(x - y), n);
        }
        if (d != n) return d;
    }
}
const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
string ToRadix(ApInt x, int radix)
{
    if (x.IsZero) return "0";
    var sb = new StringBuilder();
    ApInt m = ApInt.Abs(x);
    while (!m.IsZero) { var (q, r) = ApInt.DivRem(m, radix); sb.Insert(0, Digits[(int)(long)r]); m = q; }
    return (x.IsNegative ? "-" : "") + sb;
}
ApInt FromRadix(string s, int radix)
{
    bool neg = s.StartsWith('-');
    ApInt r = 0;
    foreach (char c in neg ? s[1..] : s) r = r * radix + Digits.IndexOf(c);
    return neg ? -r : r;
}
string ToOctal(ApInt x)   // from the bits, so it costs n, not n²
{
    string bits = ApInt.Abs(x).ToString("B").TrimStart('0');
    if (bits.Length == 0) return "0";
    bits = bits.PadLeft((bits.Length + 2) / 3 * 3, '0');
    var sb = new StringBuilder(x.IsNegative ? "-" : "");
    for (int i = 0; i < bits.Length; i += 3) sb.Append((char)('0' + (bits[i] - '0') * 4 + (bits[i + 1] - '0') * 2 + (bits[i + 2] - '0')));
    return sb.ToString();
}
// floor(n^(1/k)) by Newton on ApInt.
ApInt IntegerRoot(ApInt n, int k)
{
    if (n.IsZero) return 0;
    ApInt x = ApInt.One << (int)(n.BitLength / k + 1);
    while (true)
    {
        ApInt y = ((k - 1) * x + n / ApInt.Pow(x, k - 1)) / k;
        if (y >= x) return x;
        x = y;
    }
}
// x^(1/k), correctly rounded: the integer root of a scaled significand, with a sticky bit below it.
ApFloat RootN(ApFloat x, int k, int p, RoundingMode mode = RoundingMode.ToNearestEven)
{
    if (x.IsZero || !x.IsFinite) return x.WithPrecision(p);
    if (x.IsNegative && int.IsEvenInteger(k)) return ApFloat.NaN.WithPrecision(p);
    ApInt m = x.Significand; long e = x.Exponent;
    long s = (p + 2L) * k - m.BitLength;              // the root gets at least p + 2 bits
    if (s < 0) s = 0;
    while ((e - s) % k != 0) s++;                     // e - s must be a multiple of k
    ApInt scaled = m << (int)s;
    ApInt root = IntegerRoot(scaled, k);
    bool exact = ApInt.Pow(root, k) == scaled;
    ApInt sticky = (root << 1) + (exact ? 0 : 1);      // one bit below: the sticky bit, for the public constructor
    ApFloat r = new ApFloat(sticky, (e - s) / k - 1, p, mode);
    return x.IsNegative ? -r : r;
}
// x^n exact, then one rounding: (m·2^e)^n = m^n · 2^(en).
ApFloat PowInt(ApFloat x, int n, int p, RoundingMode mode = RoundingMode.ToNearestEven)
{
    if (x.IsZero) return n == 0 ? ApFloat.One.WithPrecision(p) : x.WithPrecision(p);
    ApInt m = ApInt.Pow(x.Significand, n);
    if (x.IsNegative && int.IsOddInteger(n)) m = -m;
    return new ApFloat(m, x.Exponent * n, p, mode);
}
long Top(ApFloat x) => x.Exponent + x.Significand.BitLength - 1;
ApFloat Ulp(ApFloat x, int p) => new ApFloat(1, Top(x) - (p - 1), 2);
ApFloat NextUp(ApFloat x, int p)
{
    // Any positive value below every rounding gap: rounded up, it is exactly one step.
    ApFloat tiny = new ApFloat(1, Top(x) - p - 4, 2);
    return ApFloat.Add(x, tiny, p, RoundingMode.TowardPositive);
}
ApFloat NextDown(ApFloat x, int p) => -NextUp(-x, p);
// In a format: the same, and the format's edges come from Add itself.
ApFloat NextUpIn(ApFloat x, IeeeFormat f)
{
    if (x.IsNaN || (x.IsInfinity && !x.IsNegative)) return x;
    long least = f.MinExponent - (f.Precision - 1);
    ApFloat smallest = new ApFloat(1, least, 2);
    if (x.IsZero) return smallest;
    if (x.IsInfinity) return -new ApFloat((ApInt.One << f.Precision) - 1, f.MaxExponent - (f.Precision - 1), f.Precision);
    long at = x.IsInfinity ? f.MaxExponent + 1 : Math.Max(Top(x) - (f.Precision - 1) - 4, least - 4);
    return ApFloat.Add(x, new ApFloat(1, at, 2), f, RoundingMode.TowardPositive);
}

H("integers");
Ok("floored mod", FlooredMod(-7, 2) == 1 && FlooredMod(7, -2) == -1 && FlooredMod(-6, 2) == 0 && FlooredMod(7, 2) == 1);
Ok("lcm", Lcm(4, 6) == 12 && Lcm(0, 5) == 0 && Lcm(-4, 6) == 12);
Ok("20!", Factorial(20).ToString() == "2432902008176640000");
Ok("nPr(20,5), nCr(100,50)", NPr(20, 5) == 1860480 && NCr(100, 50).ToString() == "100891344545564193334812497256");
long msF10k = Ms(() => Factorial(10000));
long msF100k = 0; ApInt f100k = 0;
msF100k = Ms(() => f100k = ProductRange(1, 30000));
Console.WriteLine($"     10000! one by one: {msF10k} ms; 30000! by product tree: {msF100k} ms, {f100k.BitLength} bits");
{
    var rng = new Random(7); int bad = 0, n = 0;
    BigInteger B(ApInt a) => BigInteger.Parse(a.ToString());
    for (int i = 0; i < 2000; i++)
    {
        var bytes = new byte[rng.Next(1, 40)]; rng.NextBytes(bytes);
        ApInt m = ApInt.FromMagnitude(bytes); if (m < 2) continue;
        var ab = new byte[rng.Next(1, 40)]; rng.NextBytes(ab);
        ApInt a = ApInt.FromMagnitude(ab, rng.Next(2) == 0);
        if (ApInt.GreatestCommonDivisor(a, m) != 1) continue;
        ApInt inv = ModInverse(a, m); n++;
        BigInteger x = (B(a) % B(m) + B(m)) % B(m);
        if (x * B(inv) % B(m) != 1) bad++;
    }
    Ok("modular inverse against BigInteger", bad == 0, $"{bad} wrong of {n}");
}
{
    int[] b12 = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 };
    Ok("Miller-Rabin: known primes and composites",
        MillerRabin((ApInt.One << 127) - 1, b12) && !MillerRabin((ApInt.One << 129) - 1, b12) && MillerRabin((ApInt.One << 521) - 1, b12)
        && !MillerRabin(561, b12) && !MillerRabin(3215031751, b12) && MillerRabin(2147483647, b12));
    long ms = Ms(() => MillerRabin((ApInt.One << 2203) - 1, new[] { 2 }));
    Console.WriteLine($"     one base on 2^2203-1: {ms} ms");
    ApInt semi = (ApInt)1000000007 * 998244353;
    ApInt d = 0; long msRho = Ms(() => d = PollardRho(semi));
    Ok("Pollard rho on a 60-bit semiprime", d == 1000000007 || d == 998244353, $"{msRho} ms");
}
{
    var rng = new Random(3); int bad = 0, n = 0;
    for (int i = 0; i < 3000; i++)
    {
        var bytes = new byte[rng.Next(0, 30)]; rng.NextBytes(bytes);
        ApInt v = ApInt.FromMagnitude(bytes, rng.Next(2) == 0);
        int radix = rng.Next(2, 37); n++;
        string s = ToRadix(v, radix);
        if (FromRadix(s, radix) != v) bad++;
        if (radix == 8 && s != ToOctal(v)) bad++;
    }
    Ok("radix 2..36 both ways round trip", bad == 0, $"{bad} wrong of {n}");
    Ok("octal", ToOctal(8) == "10" && ToOctal(-255) == "-377" && ToOctal(0) == "0");
    ApInt big = ApInt.Pow(3, 10000);
    long a = Ms(() => ToRadix(big, 7)), b = Ms(() => ToOctal(big));
    Console.WriteLine($"     {big.BitLength}-bit value: radix 7 by DivRem {a} ms, octal from bits {b} ms");
}
{
    ApInt v = ApInt.Pow(10, 30) + 7;
    string grouped = ((ApFloat)v).ToString("#,##0");
    Ok("ApInt grouping through the exact ApFloat route", grouped == "1,000,000,000,000,000,000,000,000,000,007", grouped);
    Ok("negative too", ((ApFloat)(-v)).ToString("#,##0").StartsWith("-1,000,"));
    ApInt b = ApInt.Pow(3, 30000);
    string via = "";
    long job = Ms(() =>
    {
        using var j = ((ApFloat)b).StartFormat("F0");
        while (!j.Continue(TimeSpan.FromMilliseconds(5))) { }
        via = j.Result;
    });
    string direct = "";
    long one = Ms(() => direct = b.ToString());
    Ok("ApInt decimal as a job, via (ApFloat)x StartFormat(\"F0\")", via == direct, $"{b.BitLength} bits: job {job} ms, ToString {one} ms");
    ApInt back = 0;
    long parse = Ms(() =>
    {
        using var j = ApFloat.StartParse(direct, (int)(direct.Length * 3.33) + 8);
        while (!j.Continue(TimeSpan.FromMilliseconds(5))) { }
        back = (ApInt)j.Result;
    });
    Ok("ApInt decimal parse as a job, via StartParse then (ApInt)", back == b, $"{parse} ms");
}
{
    // Calc's own long loops are jobs too: Natural.Jobs is public.
    static IEnumerable<long> FactorialSteps(JobContext<ApInt> c, int n)
    {
        ApInt r = 1;
        c.ExpectedWork = n;
        for (int i = 2; i <= n; i++) { r *= i; if ((i & 255) == 0) yield return 256; }
        c.Result = r;
    }
    using var job = Job.Start<ApInt>(c => FactorialSteps(c, 5000));
    int calls = 0;
    while (!job.Continue(TimeSpan.FromMilliseconds(1))) calls++;
    Ok("a calculator's own loop as a Natural.Jobs job", job.Result == Factorial(5000), $"{calls} slices, progress {job.Progress}");
}

// ------------------------------------------------------------------ floats
H("roots, powers, ulp, next");
foreach (var (arg, k) in new (string, int)[] { ("2", 3), ("2", 5), ("3", 3), ("1000", 7), ("0.001", 3), ("1e300", 3), ("27", 3), ("10", 2) })
    foreach (int p in new[] { 53, 256, 1024 })
    {
        ApFloat x = ApFloat.Parse(arg, 4096);
        foreach (RoundingMode m in Enum.GetValues<RoundingMode>())
            oracle.WriteLine($"ROOT {x.ToString("A")} {k} {p} {m} {RootN(x, k, p, m).ToString("A")}");
    }
Ok("cbrt(27) exactly 3", RootN(27, 3, 64) == 3);
Ok("sqrt recipe agrees with ApFloat.Sqrt", Enumerable.Range(2, 200).All(n => RootN(n, 2, 256) == ApFloat.Sqrt(n, 256)));
foreach (var (arg, n) in new (string, int)[] { ("1.1", 100), ("3", 200), ("0.1", 77), ("-1.5", 33) })
    foreach (int p in new[] { 53, 256 })
    {
        ApFloat x = ApFloat.Parse(arg, 256);
        oracle.WriteLine($"POW {x.ToString("A")} {n} {p} {PowInt(x, n, p).ToString("A")}");
    }
{
    var rng = new Random(11); int badU = 0, badD = 0, badUlp = 0, n = 0;
    for (int i = 0; i < 20000; i++)
    {
        double d = BitConverter.Int64BitsToDouble(rng.NextInt64() ^ (rng.NextInt64() << 1));
        if (!double.IsFinite(d) || d == 0 || Math.Abs(d) < 1e-300) continue;
        n++;
        ApFloat x = d;
        if ((double)NextUp(x, 53) != Math.BitIncrement(d)) badU++;
        if ((double)NextDown(x, 53) != Math.BitDecrement(d)) badD++;
        if ((double)Ulp(x, 53) != Math.BitIncrement(Math.Abs(d)) - Math.Abs(d)) badUlp++;
    }
    Ok("NextUp, NextDown, Ulp at 53 bits against Math.BitIncrement", badU + badD + badUlp == 0, $"{badU}+{badD}+{badUlp} wrong of {n}");
    int bad2 = 0; n = 0;
    foreach (double d in new[] { 0.0, -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue, double.PositiveInfinity, double.NegativeInfinity, 2.2250738585072014E-308, 2.225073858507201E-308, -2.2250738585072014E-308, 1.0, -1.0 })
    {
        n++;
        if (BitConverter.DoubleToInt64Bits((double)NextUpIn(d, IeeeFormat.Binary64)) != BitConverter.DoubleToInt64Bits(Math.BitIncrement(d))) { bad2++; Console.WriteLine($"     NextUpIn({d:R}) = {(double)NextUpIn(d, IeeeFormat.Binary64):R}, BitIncrement {Math.BitIncrement(d):R}"); }
    }
    Ok("NextUpIn(binary64) at the format's edges against BitIncrement", bad2 == 0, $"{bad2} wrong of {n}");
    Console.WriteLine($"     next-up of 0 at a bare precision: no such value (unbounded exponent); Top(0) is meaningless: {Top(ApFloat.Zero)}");
}

// ------------------------------------------------------------------ transcendentals, on the public API
H("constants and transcendentals on the public API (ORACLE lines go to oracle.py)");
// Working in ApInt fixed point at w fractional bits.
ApInt AtanInverse(int n, int w)   // atan(1/n)
{
    ApInt one = ApInt.One << w, term = one / n, sum = term; int k = 1; ApInt n2 = n * n;
    while (!term.IsZero) { term /= n2; k += 2; ApInt t = term / k; sum = int.IsOddInteger((k - 1) / 2) ? sum - t : sum + t; }
    return sum;
}
ApInt PiFixed(int w) => (4 * (4 * AtanInverse(5, w + 16) - AtanInverse(239, w + 16))) >> 16;     // Machin
ApInt Ln2Fixed(int w)                                                                             // 18 atanh(1/26) - 2 atanh(1/4801) + 8 atanh(1/8749)
{
    ApInt Atanh(int n) { ApInt one = ApInt.One << (w + 16), term = one / n, sum = term; ApInt n2 = n * n; int k = 1; while (!term.IsZero) { term /= n2; k += 2; sum += term / k; } return sum; }
    return (18 * Atanh(26) - 2 * Atanh(4801) + 8 * Atanh(8749)) >> 16;
}
ApInt EFixed(int w) { ApInt one = ApInt.One << (w + 16), term = one, sum = one; for (int k = 1; !term.IsZero; k++) { term /= k; sum += term; } return sum >> 16; }
foreach (int p in new[] { 53, 256, 1024, 4096 })
{
    int w = p + 32;
    ApFloat pi = 0, e = 0, ln2 = 0, phi = 0, tau = 0;
    long a = Ms(() => pi = new ApFloat(PiFixed(w), -w, p));
    long b = Ms(() => e = new ApFloat(EFixed(w), -w, p));
    long c = Ms(() => ln2 = new ApFloat(Ln2Fixed(w), -w, p));
    long d = Ms(() => phi = ApFloat.Add(ApFloat.One, ApFloat.Sqrt(5, p + 64), p + 64) / 2);
    tau = new ApFloat(PiFixed(w) << 1, -w, p);
    Console.WriteLine($"     {p} bits: pi {a} ms, e {b} ms, ln2 {c} ms, phi {d} ms");
    Emit("PI", "-", p, pi); Emit("E", "-", p, e); Emit("LN2", "-", p, ln2); Emit("PHI", "-", p, phi.WithPrecision(p)); Emit("TAU", "-", p, tau);
}

// The series, at working precision w, in plain ApFloat with an explicit precision everywhere.
ApFloat Mul(ApFloat a, ApFloat b, int w) => ApFloat.Multiply(a, b, w);
ApFloat Exp(ApFloat x, int w)
{
    int r = (int)Math.Sqrt(w) + 2;
    ApFloat y = ApFloat.Divide(x, new ApFloat(1, r, 2), w + r), term = ApFloat.One.WithPrecision(w + r), sum = term;
    for (int k = 1; k < 100000; k++)
    {
        term = ApFloat.Divide(Mul(term, y, w + r), k, w + r);
        if (term.IsZero || Top(term) < Top(sum) - (w + r) - 2) break;
        sum = ApFloat.Add(sum, term, w + r);
    }
    for (int i = 0; i < r; i++) sum = Mul(sum, sum, w + r);
    return sum;
}
ApFloat Ln(ApFloat x, int w)
{
    // Newton on exp: y := y + 2 (x - e^y)/(x + e^y), from a double's guess, doubling the bits.
    double m = (double)new ApFloat(x.Significand, -x.Significand.BitLength, 53);
    double guess = Math.Log(m) + (x.Exponent + x.Significand.BitLength) * Math.Log(2);
    ApFloat y = guess;
    for (int bits = 50; ; bits *= 2)
    {
        int wp = Math.Min(bits * 2, w) + 16;
        ApFloat ey = Exp(y, wp);
        y = ApFloat.Add(y, ApFloat.Multiply(2, ApFloat.Divide(ApFloat.Subtract(x, ey, wp), ApFloat.Add(x, ey, wp), wp), wp), wp);
        if (bits * 2 >= w) break;
    }
    return y;
}
(ApFloat Sin, ApFloat Cos) SinCos(ApFloat x, int w)
{
    // Taylor on x / 2^r, then double angles.
    int r = (int)Math.Sqrt(w) / 2 + 2, wp = w + 2 * r;
    ApFloat y = ApFloat.Divide(x, new ApFloat(1, r, 2), wp), y2 = Mul(y, y, wp);
    ApFloat s = y, c = ApFloat.One.WithPrecision(wp), ts = y, tc = c;
    for (int k = 1; k < 100000; k++)
    {
        tc = -ApFloat.Divide(Mul(tc, y2, wp), (2 * k - 1) * (2 * k), wp);
        ts = -ApFloat.Divide(Mul(ts, y2, wp), (2 * k) * (2 * k + 1), wp);
        c = ApFloat.Add(c, tc, wp); s = ApFloat.Add(s, ts, wp);
        if (tc.IsZero || Top(tc) < -wp - 2) break;
    }
    for (int i = 0; i < r; i++) { ApFloat ns = 2 * Mul(s, c, wp); c = ApFloat.Subtract(Mul(2, Mul(c, c, wp), wp), 1, wp); s = ns; }
    return (s, c);
}
ApFloat Atan(ApFloat x, int w)
{
    // Newton on tan: y := y + (x cos y - sin y)(cos y + x sin y)... from a double's guess: y += (x c - s) / (c + x s).
    ApFloat y = Math.Atan((double)x);
    for (int bits = 50; ; bits *= 2)
    {
        int wp = Math.Min(bits * 2, w) + 16;
        var (s, c) = SinCos(y, wp);
        y = ApFloat.Add(y, ApFloat.Divide(ApFloat.Subtract(Mul(x, c, wp), s, wp), ApFloat.Add(c, Mul(x, s, wp), wp), wp), wp);
        if (bits * 2 >= w) break;
    }
    return y;
}
// Ziv: both ends of an error bound round the same way, or the working precision doubles.
ApFloat Ziv(Func<int, ApFloat> f, int p, int slackBits, out int rounds)
{
    rounds = 0;
    for (int w = p + 32; ; w *= 2)
    {
        rounds++;
        ApFloat y = f(w), err = new ApFloat(1, Top(y) - w + slackBits, 2);
        ApFloat lo = ApFloat.Subtract(y, err, w, RoundingMode.TowardNegative).WithPrecision(p);
        ApFloat hi = ApFloat.Add(y, err, w, RoundingMode.TowardPositive).WithPrecision(p);
        if (lo == hi) return lo;
    }
}
foreach (int p in new[] { 53, 256, 1024 })
{
    foreach (string arg in new[] { "0.5", "1", "1.5", "2", "10", "-3.25", "100.125" })
    {
        ApFloat x = ApFloat.Parse(arg, 4096);
        int r1 = 0;
        ApFloat v = default;
        long t = Ms(() => v = Ziv(w => Exp(x, w), p, 12, out r1));
        Emit("EXP", arg, p, v);
        if (arg == "1.5") Console.WriteLine($"     exp(1.5) at {p} bits: {t} ms, {r1} round(s)");
    }
    foreach (string arg in new[] { "0.5", "2", "10", "1000", "1e-5", "12345.678" })
    {
        ApFloat x = ApFloat.Parse(arg, 4096);
        ApFloat ln = default, l10 = default, l2 = default; int r = 0;
        long t = Ms(() => ln = Ziv(w => Ln(x, w), p, 14, out r));
        l10 = Ziv(w => ApFloat.Divide(Ln(x, w + 16), Ln(10, w + 16), w + 16), p, 16, out _);
        l2 = Ziv(w => ApFloat.Divide(Ln(x, w + 16), Ln(2, w + 16), w + 16), p, 16, out _);
        Emit("LN", arg, p, ln); Emit("LOG10", arg, p, l10); Emit("LOG2", arg, p, l2);
        if (arg == "10") Console.WriteLine($"     ln(10) at {p} bits: {t} ms, {r} round(s)");
    }
    foreach (string arg in new[] { "0.5", "1", "2", "3", "10", "-1.25" })
    {
        ApFloat x = ApFloat.Parse(arg, 4096);
        ApFloat s = Ziv(w => SinCos(x, w).Sin, p, 14, out _), c = Ziv(w => SinCos(x, w).Cos, p, 14, out _);
        ApFloat tan = Ziv(w => { var (sn, cs) = SinCos(x, w + 8); return ApFloat.Divide(sn, cs, w + 8); }, p, 16, out _);
        ApFloat at = Ziv(w => Atan(x, w), p, 14, out _);
        Emit("SIN", arg, p, s); Emit("COS", arg, p, c); Emit("TAN", arg, p, tan); Emit("ATAN", arg, p, at);
        ApFloat sh = Ziv(w => { var e = Exp(x, w + 8); return ApFloat.Divide(ApFloat.Subtract(e, ApFloat.Divide(1, e, w + 8), w + 8), 2, w + 8); }, p, 18, out _);
        Emit("SINH", arg, p, sh);
    }
    // x^y = exp(y ln x), for a non-integer y.
    foreach (var (a, b) in new[] { ("2", "0.5"), ("10", "1.5"), ("3.5", "2.25") })
    {
        ApFloat x = ApFloat.Parse(a, 4096), y = ApFloat.Parse(b, 4096);
        ApFloat v = Ziv(w => Exp(ApFloat.Multiply(y, Ln(x, w + 16), w + 16), w), p, 16, out _);
        Emit("POWR", a + "," + b, p, v);
    }
}
oracle.Close();
Console.WriteLine($"\noracle lines written to {oracleFile}");
