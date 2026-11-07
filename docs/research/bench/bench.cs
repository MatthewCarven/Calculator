#:project C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\natural-bench\src\Natural\Natural.csproj
#:property PublishAot=false
#:property InvariantGlobalization=true
// What Natural can do at speed. One file, sections chosen by the first argument:
//     dotnet build -c Release bench.cs
//     dotnet run -c Release --no-build bench.cs -- B log=b.csv [sizes=..] [ops=Add,Mul] [rounds=3] [cap=20] [skip=30] [hard=25]
//     ... -- C log=c.csv
//     ... -- D log=d.csv [sizes=..] [arms=pi_plain,..]
// B: ApFloat ops and text. C: ApInt. D: transcendentals hand-rolled on the public API.
// Every line of the log is CSV; "MEDIAN" lines are what the tables come from. BigInteger appears
// here only as the harness's source of operands and as the oracle; it never touches the library.
// Timing rule (this machine has two speeds): each size runs in rounds, each round rotates the
// order of the ops, and each round starts with a calibration loop that uses nothing of Natural.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Natural;

string section = args.Length > 0 ? args[0] : "B";
var opt = args.Skip(1).Select(a => a.Split('=', 2)).ToDictionary(kv => kv[0], kv => kv[1]);
string Get(string key, string dflt) => opt.TryGetValue(key, out var v) ? v : dflt;
int[] Sizes(string dflt) => Get("sizes", dflt).Split(',').Select(int.Parse).ToArray();

H.Log = Get("log", "bench-" + section + ".csv");
H.Rounds = int.Parse(Get("rounds", "3"));
H.Cap = double.Parse(Get("cap", "20"), CultureInfo.InvariantCulture);
H.SkipAbove = double.Parse(Get("skip", "30"), CultureInfo.InvariantCulture);
double hard = double.Parse(Get("hard", "25"), CultureInfo.InvariantCulture);
var dog = new Thread(() =>
{
    Thread.Sleep(TimeSpan.FromMinutes(hard));
    H.Line($"HARD TIMEOUT after {hard} minutes");
    Environment.Exit(3);
}) { IsBackground = true };
dog.Start();

var asm = typeof(ApFloat).Assembly;
var dbg = asm.GetCustomAttribute<DebuggableAttribute>();
H.Line($"# section={section} start={DateTime.Now:o} runtime={Environment.Version} gcServer={GCSettings.IsServerGC} " +
       $"gcLatency={GCSettings.LatencyMode} procs={Environment.ProcessorCount} hires={Stopwatch.IsHighResolution}");
H.Line($"# library={asm.Location} jitOptimizerDisabled={dbg?.IsJITOptimizerDisabled.ToString() ?? "no attribute"} " +
       $"args={string.Join(' ', args)}");
{
    // The throwaway copy says what it's running; the real library has no such fields.
    var mag = asm.GetType("Natural.Magnitude")!;
    object? Flag(string name) => mag.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    H.Line($"# flags KoggeStone={Flag("KoggeStone") ?? "absent"} KaratsubaLimbs={Flag("KaratsubaLimbs") ?? "absent"} ShortDivide={Flag("ShortDivide") ?? "absent"}");
}
for (int i = 0; i < 4; i++) H.Calibrate();

switch (section)
{
    case "B": Sections.B(Sizes("53,256,1024,4096,16384,65536,262144,1048576"), Get("ops", "")); break;
    case "C": Sections.C(); break;
    case "D": Sections.D(Sizes("256,1024,4096,16384,65536"), Get("arms", "")); break;
    case "N": Sections.N(Sizes("4096,16384,65536,262144,1048576"), Get("ops", "")); break;
    default: throw new ArgumentException("Section is B, C, D or N.");
}
H.Line($"# end={DateTime.Now:o}");
return;

// ----------------------------------------------------------------------------------------------
// Harness
// ----------------------------------------------------------------------------------------------

static class H
{
    public static string Log = "";
    public static int Rounds = 3;
    public static double Cap = 20, SkipAbove = 30;
    public static object? Sink;
    public static ulong Sink2;
    public const double TscHz = 2.496e9;
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [DllImport("kernel32.dll")] private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    public static ulong Cycles() { QueryThreadCycleTime(GetCurrentThread(), out ulong c); return c; }

    public static void Line(string s)
    {
        Console.WriteLine(s);
        File.AppendAllText(Log, s + Environment.NewLine);
    }

    public static string Show(double s) =>
        s < 1e-6 ? $"{s * 1e9:0} ns" : s < 1e-3 ? $"{s * 1e6:0.00} us" : s < 1 ? $"{s * 1e3:0.00} ms" : $"{s:0.00} s";

    public static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

    /// <summary>A fixed loop with nothing of Natural in it: the machine's speed, this moment, in ms.</summary>
    public static double Calibrate()
    {
        long t0 = Stopwatch.GetTimestamp();
        ulong x = 88172645463325252UL;
        for (int i = 0; i < 20_000_000; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
        }
        Sink2 = x;
        return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>Per-operation wall and thread-CPU seconds: repeats until a batch lasts at least minSec.</summary>
    public static (double Wall, double Cpu, long Reps) Measure(Action op, double minSec)
    {
        long n = 1;
        while (true)
        {
            ulong c0 = Cycles();
            long t0 = Stopwatch.GetTimestamp();
            for (long i = 0; i < n; i++) op();
            double el = Stopwatch.GetElapsedTime(t0).TotalSeconds;
            ulong c1 = Cycles();
            if (el >= minSec) return (el / n, (c1 - c0) / TscHz / n, n);
            n = el <= 0 ? n * 8 : Math.Max(n * 2, (long)Math.Ceiling(n * minSec * 1.3 / el));
        }
    }

    public static double Median(List<double> l)
    {
        var s = l.OrderBy(x => x).ToList();
        int n = s.Count;
        return n == 0 ? double.NaN : n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
    }

    public static void Tidy()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>Runs each op for a moment at a few sizes, so the timed runs meet tier-1 code.</summary>
    public static void Warm(IEnumerable<Op> ops, int[] sizes, double seconds)
    {
        foreach (int size in sizes)
            foreach (Op op in ops)
            {
                Action a = op.Make(size);
                long t0 = Stopwatch.GetTimestamp();
                do a(); while (Stopwatch.GetElapsedTime(t0).TotalSeconds < seconds);
            }
    }

    /// <summary>
    /// Rounds of every op at every size, rotating the order. An op stops for good once a timing
    /// passes the cap, or is not started when its last figure and growth predict more than SkipAbove.
    /// </summary>
    public static void Sized(string sec, int[] sizes, List<Op> ops, double minSec)
    {
        H.Line("kind,section,time,round,size,op,wall_s,cpu_s,reps,calib_ms");
        foreach (int size in sizes)
        {
            var live = new List<(Op Op, Action Act, List<double> Wall, List<double> Cpu)>();
            foreach (Op op in ops)
            {
                if (op.Stopped)
                {
                    H.Line($"STOPPED,{sec},{Stamp()},,{size},{op.Name},,,,{op.Why}");
                    continue;
                }
                if (op.LastSize > 0)
                {
                    double g = op.Growth;
                    if (op.PrevSize > 0 && op.LastMedian > 0.005)
                        g = Math.Min(3, Math.Max(g, Math.Log(op.LastMedian / op.PrevMedian) / Math.Log((double)op.LastSize / op.PrevSize)));
                    double predicted = op.LastMedian * Math.Pow((double)size / op.LastSize, g);
                    if (predicted > SkipAbove)
                    {
                        op.Stopped = true;
                        op.Why = $"predicted {predicted:0.0} s at exponent {g:0.00} from {op.LastSize}";
                        H.Line($"SKIP,{sec},{Stamp()},,{size},{op.Name},{predicted:0.###},,,{op.Why}");
                        continue;
                    }
                }
                live.Add((op, op.Make(size), [], []));
            }
            if (live.Count == 0) continue;

            for (int r = 0; r < Rounds; r++)
            {
                double cal = Calibrate();
                for (int i = 0; i < live.Count; i++)
                {
                    var e = live[(i + r) % live.Count];
                    if (e.Op.Capped) continue;
                    Tidy();
                    var (wall, cpu, reps) = Measure(e.Act, minSec);
                    e.Wall.Add(wall);
                    e.Cpu.Add(cpu);
                    H.Line($"T,{sec},{Stamp()},{r},{size},{e.Op.Name},{wall:G6},{cpu:G6},{reps},{cal:0.0}");
                    if (wall > Cap) e.Op.Capped = true;
                }
            }
            foreach (var e in live)
            {
                double med = Median(e.Wall);
                H.Line($"MEDIAN,{sec},{Stamp()},n={e.Wall.Count},{size},{e.Op.Name},{med:G6},{Median(e.Cpu):G6},{Show(med)},");
                e.Op.PrevMedian = e.Op.LastMedian;
                e.Op.PrevSize = e.Op.LastSize;
                e.Op.LastMedian = med;
                e.Op.LastSize = size;
                if (e.Op.Capped)
                {
                    e.Op.Stopped = true;
                    e.Op.Why = $"passed the {Cap:0} s cap at {size}";
                }
            }
        }
    }
}

sealed class Op(string name, double growth, Func<int, Action> make)
{
    public string Name = name;
    public double Growth = growth;
    public Func<int, Action> Make = make;
    public bool Stopped, Capped;
    public double LastMedian, PrevMedian;
    public int LastSize, PrevSize;
    public string Why = "";
}

// ----------------------------------------------------------------------------------------------
// Values, made in the harness (BigInteger) so that setup costs nothing at any size
// ----------------------------------------------------------------------------------------------

static class V
{
    public static ApInt ToAp(BigInteger b) => ApInt.FromMagnitude(BigInteger.Abs(b).ToByteArray(isUnsigned: true, isBigEndian: false), b.Sign < 0);

    public static BigInteger ToBig(ApInt a)
    {
        var m = new BigInteger(a.ToMagnitudeBytes(), isUnsigned: true);
        return a.IsNegative ? -m : m;
    }

    public static BigInteger ISqrt(BigInteger n)
    {
        if (n < 2) return n;
        int bits = (int)n.GetBitLength();
        BigInteger x;
        if (bits <= 64) x = BigInteger.One << ((bits + 1) / 2);
        else
        {
            int k = bits / 4;
            x = (ISqrt(n >> (2 * k)) + 1) << k;
        }
        while (true)
        {
            BigInteger y = (x + n / x) >> 1;
            if (y >= x) return x;
            x = y;
        }
    }

    /// <summary>1/3 rounded to nearest at p bits: a significand full of bits, built without Divide.</summary>
    public static ApFloat Third(int p)
    {
        BigInteger m = BigInteger.DivRem(BigInteger.One << (p + 1), 3, out BigInteger r);
        if (r == 2) m += 1;
        return new ApFloat(ToAp(m), -(long)(p + 1), p);
    }

    /// <summary>The root of 2 rounded to nearest at p bits, built without ApFloat.Sqrt.</summary>
    public static ApFloat Root2(int p)
    {
        BigInteger n = BigInteger.One << (2 * p - 1);
        BigInteger s = ISqrt(n);
        BigInteger t = 2 * s + 1;
        if (t * t < (n << 2)) s += 1;
        return new ApFloat(ToAp(s), -(long)(p - 1), p);
    }

    /// <summary>d significant decimal digits of the root of 2, as text.</summary>
    public static string Root2Text(int d)
    {
        string s = ISqrt(2 * BigInteger.Pow(10, 2 * (d - 1))).ToString(CultureInfo.InvariantCulture);
        return s[0] + "." + s[1..];
    }

    public static ApInt RandBits(Random r, int bits)
    {
        var b = new byte[(bits + 7) / 8];
        r.NextBytes(b);
        int topBit = (bits - 1) % 8;
        b[^1] = (byte)((b[^1] & ((1 << (topBit + 1)) - 1)) | (1 << topBit));
        return ApInt.FromMagnitude(b);
    }
}

// ----------------------------------------------------------------------------------------------
// Timed wrappers: each public call charged to a bucket, so D can say which operation dominates
// ----------------------------------------------------------------------------------------------

static class T
{
    public static long TAdd, TMul, TDsm, TDbg, TOther, NAdd, NMul, NDsm, NDbg;

    public static void Reset() => TAdd = TMul = TDsm = TDbg = TOther = NAdd = NMul = NDsm = NDbg = 0;

    public static ApFloat A(ApFloat a, ApFloat b, int w)
    {
        long t = Stopwatch.GetTimestamp();
        ApFloat r = ApFloat.Add(a, b, w);
        TAdd += Stopwatch.GetTimestamp() - t;
        NAdd++;
        return r;
    }

    public static ApFloat S(ApFloat a, ApFloat b, int w)
    {
        long t = Stopwatch.GetTimestamp();
        ApFloat r = ApFloat.Subtract(a, b, w);
        TAdd += Stopwatch.GetTimestamp() - t;
        NAdd++;
        return r;
    }

    public static ApFloat M(ApFloat a, ApFloat b, int w)
    {
        long t = Stopwatch.GetTimestamp();
        ApFloat r = ApFloat.Multiply(a, b, w);
        TMul += Stopwatch.GetTimestamp() - t;
        NMul++;
        return r;
    }

    /// <summary>Division by anything long.</summary>
    public static ApFloat D(ApFloat a, ApFloat b, int w)
    {
        long t = Stopwatch.GetTimestamp();
        ApFloat r = ApFloat.Divide(a, b, w);
        TDbg += Stopwatch.GetTimestamp() - t;
        NDbg++;
        return r;
    }

    /// <summary>Division by a small integer.</summary>
    public static ApFloat Ds(ApFloat a, long d, int w)
    {
        long t = Stopwatch.GetTimestamp();
        ApFloat r = ApFloat.Divide(a, (ApFloat)d, w);
        TDsm += Stopwatch.GetTimestamp() - t;
        NDsm++;
        return r;
    }

    /// <summary>a × 2^k, at w bits. ApFloat has no ScaleB, so it's a rebuild, O(limbs).</summary>
    public static ApFloat Sc(ApFloat a, int k, int w)
    {
        long t = Stopwatch.GetTimestamp();
        ApFloat r = new ApFloat(a.IsNegative ? -a.Significand : a.Significand, a.Exponent + k, w);
        TOther += Stopwatch.GetTimestamp() - t;
        return r;
    }

    public static string Shares(double seconds)
    {
        double f = 100.0 / (seconds * Stopwatch.Frequency);
        return $"add%={TAdd * f:0.0} mul%={TMul * f:0.0} dsm%={TDsm * f:0.0} dbg%={TDbg * f:0.0} other%={TOther * f:0.0} " +
               $"nAdd={NAdd} nMul={NMul} nDsm={NDsm} nDbg={NDbg}";
    }
}

// ----------------------------------------------------------------------------------------------
// The prototypes
// ----------------------------------------------------------------------------------------------

static class P
{
    public static int W(int p) => p + 64 + 2 * (32 - BitOperations.LeadingZeroCount((uint)p));

    public static long Top(ApFloat x) => x.Exponent + x.Significand.BitLength - 1;

    static readonly ApFloat One = (ApFloat)1L;

    // --- pi by Machin: pi = 16 atan(1/5) - 4 atan(1/239) ---

    static ApFloat AtanInv(long x, int w, bool shrink)
    {
        long x2 = x * x;
        double lg = Math.Log2(x2);
        int n = (int)(w / lg) + 2;
        ApFloat term = T.Ds(One, x, w), sum = term;
        for (int k = 1; k <= n; k++)
        {
            int wk = shrink ? Math.Max(96, w - (int)(k * lg) + 32) : w;
            term = T.Ds(term, x2, wk);
            ApFloat t = T.Ds(term, 2 * k + 1, wk);
            sum = (k & 1) != 0 ? T.S(sum, t, w) : T.A(sum, t, w);
        }
        return sum;
    }

    public static ApFloat Pi(int p, bool shrink)
    {
        int w = W(p);
        ApFloat a = AtanInv(5, w, shrink), b = AtanInv(239, w, shrink);
        return T.S(T.Sc(a, 4, w), T.Sc(b, 2, w), w).WithPrecision(p);
    }

    // --- binary splitting: the constants as one big fraction of integers, then one Divide ---

    static ApInt Mi(ApInt a, ApInt b)
    {
        long t = Stopwatch.GetTimestamp();
        ApInt r = a * b;
        T.TMul += Stopwatch.GetTimestamp() - t;
        T.NMul++;
        return r;
    }

    static ApFloat Fl(ApInt a, int w) => new ApFloat(a, 0, w);

    static (ApInt P, ApInt Q, ApInt R) Chud(long a, long b)
    {
        if (b - a == 1)
        {
            ApInt p = (ApInt)(6 * a - 5) * (2 * a - 1) * (6 * a - 1);
            ApInt q = (ApInt)(a * a) * a * 10939058860032000L;
            ApInt r = p * (13591409 + 545140134L * a);
            return (p, q, (a & 1) != 0 ? -r : r);
        }
        long m = (a + b) / 2;
        var (p1, q1, r1) = Chud(a, m);
        var (p2, q2, r2) = Chud(m, b);
        return (Mi(p1, p2), Mi(q1, q2), Mi(q2, r1) + Mi(p1, r2));
    }

    /// <summary>Chudnovsky: 47 bits a term, a tree of integer products, one Sqrt, one long Divide.</summary>
    public static ApFloat PiChudnovsky(int p)
    {
        int w = W(p);
        long n = w / 47 + 2;
        var (_, q, r) = Chud(1, n);
        long t = Stopwatch.GetTimestamp();
        ApFloat root = ApFloat.Sqrt((ApFloat)10005L, w);
        T.TOther += Stopwatch.GetTimestamp() - t;
        ApFloat top = T.M(T.M((ApFloat)426880L, root, w), Fl(q, w), w);
        ApFloat bottom = Fl(Mi(q, 13591409) + r, w);
        return T.D(top, bottom, w).WithPrecision(p);
    }

    static (ApInt Q, ApInt T) SplitE(long a, long b)
    {
        if (b - a == 1) return (b, 1);
        long m = (a + b) / 2;
        var (q1, t1) = SplitE(a, m);
        var (q2, t2) = SplitE(m, b);
        return (Mi(q1, q2), Mi(t1, q2) + t2);
    }

    /// <summary>e = 1 + T/Q, the sum of 1/k! as one fraction.</summary>
    public static ApFloat EBinarySplit(int p)
    {
        int w = W(p);
        long n = 2;
        for (double bits = 1; bits < w + 8; n++) bits += Math.Log2(n);
        var (q, t) = SplitE(0, n);
        return T.A((ApFloat)1L, T.D(Fl(t, w), Fl(q, w), w), w).WithPrecision(p);
    }

    static (ApInt B, ApInt Q, ApInt T) SplitLn(long a, long b)
    {
        if (b - a == 1) return (2 * a + 1, 9, 1);
        long m = (a + b) / 2;
        var (b1, q1, t1) = SplitLn(a, m);
        var (b2, q2, t2) = SplitLn(m, b);
        return (Mi(b1, b2), Mi(q1, q2), Mi(Mi(t1, b2), q2) + Mi(t2, b1));
    }

    /// <summary>ln 2 = (2/3)(1 + the sum for n >= 1 of 1/((2n+1) 9^n)), as one fraction.</summary>
    public static ApFloat Ln2BinarySplit(int p)
    {
        int w = W(p);
        long n = (long)(w / Math.Log2(9)) + 3;
        var (b, q, t) = SplitLn(1, n);
        ApFloat s = T.A((ApFloat)1L, T.D(Fl(t, w), Fl(Mi(b, q), w), w), w);
        return T.Ds(T.Sc(s, 1, w), 3, w).WithPrecision(p);
    }

    // --- ln 2 = 2 atanh(1/3) ---

    public static ApFloat Ln2(int p, bool shrink)
    {
        int w = W(p);
        double lg = Math.Log2(9);
        int n = (int)(w / lg) + 2;
        ApFloat term = T.Ds(One, 3, w), sum = term;
        for (int k = 1; k <= n; k++)
        {
            int wk = shrink ? Math.Max(96, w - (int)(k * lg) + 32) : w;
            term = T.Ds(term, 9, wk);
            sum = T.A(sum, T.Ds(term, 2 * k + 1, wk), w);
        }
        return T.Sc(sum, 1, w).WithPrecision(p);
    }

    // --- e: Taylor at 2^-r, then r squarings ---

    public static ApFloat E(int p, int r, bool shrink)
    {
        int w = W(p) + r;
        ApFloat term = One, sum = One;
        for (int k = 1; ; k++)
        {
            if (r != 0) term = T.Sc(term, -r, w);
            int wk = shrink ? Math.Max(96, (int)(w + Top(term) + 32)) : w;
            term = T.Ds(term, k, wk);
            sum = T.A(sum, term, w);
            if (Top(term) < -w - 2) break;
        }
        for (int i = 0; i < r; i++) sum = T.M(sum, sum, w);
        return sum.WithPrecision(p);
    }

    // --- sin and cos at 1: only small divides ---

    public static ApFloat Sin1(int w, bool shrink)
    {
        ApFloat term = One, sum = One;
        for (int k = 1; ; k++)
        {
            int wk = shrink ? Math.Max(96, (int)(w + Top(term) + 32)) : w;
            term = T.Ds(term, 2L * k * (2 * k + 1), wk);
            sum = (k & 1) != 0 ? T.S(sum, term, w) : T.A(sum, term, w);
            if (Top(term) < -w - 2) break;
        }
        return sum;
    }

    public static ApFloat Cos1(int w, bool shrink)
    {
        ApFloat term = One, sum = One;
        for (int k = 1; ; k++)
        {
            int wk = shrink ? Math.Max(96, (int)(w + Top(term) + 32)) : w;
            term = T.Ds(term, (2L * k - 1) * (2 * k), wk);
            sum = (k & 1) != 0 ? T.S(sum, term, w) : T.A(sum, term, w);
            if (Top(term) < -w - 2) break;
        }
        return sum;
    }

    /// <summary>sin(1)^2 + cos(1)^2 - 1 at p + guard bits. The result should be a few ulps of the working precision.</summary>
    public static ApFloat Expression(int p, bool shrink)
    {
        int w = W(p);
        ApFloat s = Sin1(w, shrink), c = Cos1(w, shrink);
        return T.S(T.A(T.M(s, s, w), T.M(c, c, w), w), One, w);
    }

    // --- a general argument: a multiply in every term ---

    public static ApFloat SinPlain(ApFloat x, int p)
    {
        int w = W(p);
        ApFloat v = T.M(x, x, w), term = x, sum = x;
        for (int k = 1; ; k++)
        {
            term = T.M(term, v, w);
            term = T.Ds(term, 2L * k * (2 * k + 1), w);
            sum = (k & 1) != 0 ? T.S(sum, term, w) : T.A(sum, term, w);
            if (Top(term) < Top(sum) - w - 2) break;
        }
        return sum.WithPrecision(p);
    }

    public static ApFloat ExpPlain(ApFloat x, int p)
    {
        int w = W(p);
        ApFloat term = One, sum = One;
        for (int k = 1; ; k++)
        {
            term = T.M(term, x, w);
            term = T.Ds(term, k, w);
            sum = T.A(sum, term, w);
            if (Top(term) < -w - 2) break;
        }
        return sum.WithPrecision(p);
    }

    static ApFloat PowInt(ApFloat v, int m, int w)
    {
        ApFloat result = v;
        for (int bit = 30 - BitOperations.LeadingZeroCount((uint)m); bit >= 0; bit--)
        {
            result = T.M(result, result, w);
            if (((m >> bit) & 1) != 0) result = T.M(result, v, w);
        }
        return result;
    }

    /// <summary>
    /// Sum for k = 0..n of (±1)^k v^k / prod(div(1..k)), by Smith's concurrent series: about 2 sqrt(n)
    /// long multiplies however long the series; every term is one small divide and one add.
    /// u is the term without its factor v^(k mod m); it's divided by each div(k), multiplied by
    /// v^m each m steps, and added into S[k mod m]. Horner in v puts the S back together.
    /// </summary>
    static ApFloat Smith(ApFloat v, Func<int, long> div, bool alternate, int w, int n, bool shrink)
    {
        int m = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(n)));
        ApFloat vm = PowInt(v, m, w);
        var s = new ApFloat[m];
        s[0] = One;
        ApFloat u = One;
        for (int k = 1; k <= n; k++)
        {
            int wk = shrink ? Math.Max(96, (int)(w + Top(u) + 32)) : w;
            u = T.Ds(u, div(k), wk);
            if (k % m == 0) u = T.M(u, vm, wk);
            ApFloat c = alternate && (k & 1) != 0 ? -u : u;
            s[k % m] = T.A(s[k % m], c, w);
        }
        ApFloat r = s[m - 1];
        for (int i = m - 2; i >= 0; i--) r = T.A(T.M(r, v, w), s[i], w);
        return r;
    }

    static double Log2Abs(ApFloat x) => Top(x) + 1;   // an upper bound on log2 |x|

    static int TermsSin(ApFloat v, int w)
    {
        double lg = Math.Max(-Log2Abs(v), 0), bits = 0;
        int n = 0;
        while (bits < w + 8)
        {
            n++;
            bits += lg + Math.Log2(2.0 * n) + Math.Log2(2.0 * n + 1);
        }
        return n;
    }

    static int TermsExp(ApFloat x, int w)
    {
        double lg = Math.Max(-Log2Abs(x), 0), bits = 0;
        int n = 0;
        while (bits < w + 8)
        {
            n++;
            bits += lg + Math.Log2(n);
        }
        return n;
    }

    public static ApFloat SinSmith(ApFloat x, int p, bool shrink)
    {
        int w = W(p);
        ApFloat v = T.M(x, x, w);
        int n = TermsSin(v, w);
        return T.M(x, Smith(v, k => 2L * k * (2 * k + 1), true, w, n, shrink), w).WithPrecision(p);
    }

    public static ApFloat CosSmith(ApFloat x, int p, bool shrink)
    {
        int w = W(p);
        ApFloat v = T.M(x, x, w);
        int n = TermsSin(v, w);
        return Smith(v, k => (2L * k - 1) * (2 * k), true, w, n, shrink).WithPrecision(p);
    }

    /// <summary>exp at wr bits for |x| below 1: Taylor on x / 2^r by concurrent series, then r squarings.</summary>
    public static ApFloat ExpSmithAt(ApFloat x, int wr, int r, bool shrink)
    {
        int w = wr + r;
        ApFloat y = r == 0 ? x : T.Sc(x, -r, w);
        int n = TermsExp(y, w);
        ApFloat sum = Smith(y, k => k, false, w, n, shrink);
        for (int i = 0; i < r; i++) sum = T.M(sum, sum, w);
        return sum.WithPrecision(wr);
    }

    public static ApFloat ExpSmith(ApFloat x, int p, int r, bool shrink) => ExpSmithAt(x, W(p), r, shrink).WithPrecision(p);

    /// <summary>ln x by Halley on exp, tripling the precision each step, from double's.</summary>
    public static ApFloat Ln(ApFloat x, int w)
    {
        ApFloat y = (ApFloat)Math.Log((double)x);
        var steps = new Stack<int>();
        for (int g = w; g > 45; g = g / 3 + 8) steps.Push(g);
        foreach (int g in steps)
        {
            int wg = g + 32;
            ApFloat e = ExpSmithAt(y, wg, 0, true);
            ApFloat xg = x.WithPrecision(wg);
            ApFloat num = T.S(xg, e, wg), den = T.A(xg, e, wg);
            y = T.A(y, T.Sc(T.D(num, den, wg), 1, wg), wg);
        }
        return y;
    }

    /// <summary>x^y = exp(y ln x), for y ln x below 1 in size.</summary>
    public static ApFloat Pow(ApFloat x, ApFloat y, int p)
    {
        int w = W(p) + 16;
        ApFloat z = T.M(y, Ln(x, w), w);
        return ExpSmithAt(z, w, 0, true).WithPrecision(p);
    }
}

// ----------------------------------------------------------------------------------------------
// Exploiting smallness: fixed point on limbs, a pass of short division per term.
// bitwise = true keeps the project's rule: shifts, a word compare and a one-word FullAdd.
// bitwise = false is the ceiling: the CPU's own add and 64/32 divide, which the rule forbids.
// ----------------------------------------------------------------------------------------------

static class Fx
{
    static uint HalfAddAll(uint sum, uint pending, ref uint carryOut)
    {
        while (pending != 0)
        {
            uint generated = sum & pending;
            carryOut |= generated >> 31;
            sum ^= pending;
            pending = generated << 1;
        }
        return sum;
    }

    static uint FullAdd(uint a, uint b, ref uint carry)
    {
        uint carryOut = 0;
        uint sum = HalfAddAll(a, b, ref carryOut);
        sum = HalfAddAll(sum, carry, ref carryOut);
        carry = carryOut;
        return sum;
    }

    /// <summary>a[0..top] /= d, one bit at a time, a word of remainder, d below 2^31.</summary>
    static void DivBits(uint[] a, ref int top, uint d)
    {
        uint rem = 0, notD = ~d;
        for (int i = top; i >= 0; i--)
        {
            uint word = a[i], q = 0;
            for (int b = 31; b >= 0; b--)
            {
                rem = (rem << 1) | ((word >> b) & 1);
                q <<= 1;
                if (rem >= d)
                {
                    uint carry = 1;
                    rem = FullAdd(rem, notD, ref carry);
                    q |= 1;
                }
            }
            a[i] = q;
        }
        while (top >= 0 && a[top] == 0) top--;
    }

    static void DivHw(uint[] a, ref int top, uint d)
    {
        ulong rem = 0;
        for (int i = top; i >= 0; i--)
        {
            ulong cur = (rem << 32) | a[i];
            a[i] = (uint)(cur / d);
            rem = cur % d;
        }
        while (top >= 0 && a[top] == 0) top--;
    }

    static void AddTo(uint[] sum, uint[] t, int top, bool bitwise)
    {
        if (bitwise)
        {
            uint carry = 0;
            int i = 0;
            for (; i <= top; i++) sum[i] = FullAdd(sum[i], t[i], ref carry);
            for (; carry != 0 && i < sum.Length; i++) sum[i] = FullAdd(sum[i], 0, ref carry);
        }
        else
        {
            ulong c = 0;
            int i = 0;
            for (; i <= top; i++)
            {
                c += (ulong)sum[i] + t[i];
                sum[i] = (uint)c;
                c >>= 32;
            }
            for (; c != 0 && i < sum.Length; i++)
            {
                c += sum[i];
                sum[i] = (uint)c;
                c >>= 32;
            }
        }
    }

    static void SubFrom(uint[] sum, uint[] t, int top, bool bitwise)
    {
        if (bitwise)
        {
            uint carry = 1;
            int i = 0;
            for (; i <= top; i++) sum[i] = FullAdd(sum[i], ~t[i], ref carry);
            for (; carry == 0 && i < sum.Length; i++) sum[i] = FullAdd(sum[i], ~0u, ref carry);
        }
        else
        {
            long b = 0;
            int i = 0;
            for (; i <= top; i++)
            {
                b += (long)sum[i] - t[i];
                sum[i] = (uint)b;
                b >>= 32;
            }
            for (; b != 0 && i < sum.Length; i++)
            {
                b += sum[i];
                sum[i] = (uint)b;
                b >>= 32;
            }
        }
    }

    static uint[] AtanInv(uint x, int n, bool bitwise)
    {
        var term = new uint[n + 1];
        term[n] = 1;
        int top = n;
        Div(term, ref top, x, bitwise);
        var sum = (uint[])term.Clone();
        var t = new uint[n + 1];
        uint x2 = x * x;
        for (uint k = 1; ; k++)
        {
            Div(term, ref top, x2, bitwise);
            if (top < 0) break;
            Array.Copy(term, t, top + 1);
            int ttop = top;
            Div(t, ref ttop, 2 * k + 1, bitwise);
            if (ttop < 0) continue;
            if ((k & 1) != 0) SubFrom(sum, t, ttop, bitwise);
            else AddTo(sum, t, ttop, bitwise);
        }
        return sum;
    }

    static void Div(uint[] a, ref int top, uint d, bool bitwise)
    {
        if (bitwise) DivBits(a, ref top, d);
        else DivHw(a, ref top, d);
    }

    /// <summary>pi at p bits: all small divides, adds and subtracts, on limbs.</summary>
    public static ApFloat Pi(int p, bool bitwise)
    {
        int w = P.W(p);
        int n = (w + 31) / 32;
        uint[] a = AtanInv(5, n, bitwise), b = AtanInv(239, n, bitwise);
        // 16a - 4b = 4 (4a - b)
        var a4 = new uint[n + 1];
        for (int i = n; i >= 0; i--) a4[i] = (a[i] << 2) | (i > 0 ? a[i - 1] >> 30 : 0);
        SubFrom(a4, b, n, bitwise);
        var bytes = MemoryMarshal.AsBytes(a4.AsSpan());
        return new ApFloat(ApInt.FromMagnitude(bytes), -32L * n + 2, p);
    }
}

// ----------------------------------------------------------------------------------------------
// The oracle: fixed point on BigInteger, used only to check, never timed
// ----------------------------------------------------------------------------------------------

static class O
{
    public static BigInteger Fix(ApFloat a, int f)
    {
        BigInteger m = V.ToBig(a.Significand);
        long e = a.Exponent + f;
        BigInteger v = e >= 0 ? m << (int)e : m >> (int)(-e);
        return a.IsNegative ? -v : v;
    }

    static BigInteger AtanInv(int x, int f)
    {
        BigInteger one = BigInteger.One << f, term = one / x, sum = term;
        int x2 = x * x;
        for (int k = 1; !term.IsZero; k++)
        {
            term /= x2;
            BigInteger t = term / (2 * k + 1);
            sum += (k & 1) != 0 ? -t : t;
        }
        return sum;
    }

    public static BigInteger Pi(int f) => ((16 * AtanInv(5, f + 32) - 4 * AtanInv(239, f + 32))) >> 32;

    public static BigInteger Ln2(int f)
    {
        BigInteger one = BigInteger.One << (f + 32), term = one / 3, sum = term;
        for (int k = 1; !term.IsZero; k++)
        {
            term /= 9;
            sum += term / (2 * k + 1);
        }
        return (2 * sum) >> 32;
    }

    public static BigInteger E(int f)
    {
        BigInteger term = BigInteger.One << (f + 32), sum = term;
        for (int k = 1; !term.IsZero; k++)
        {
            term /= k;
            sum += term;
        }
        return sum >> 32;
    }

    public static BigInteger Sin(BigInteger x, int f)
    {
        BigInteger v = (x * x) >> f, term = x, sum = x;
        for (int k = 1; !term.IsZero; k++)
        {
            term = ((term * v) >> f) / (2L * k * (2 * k + 1));
            sum += (k & 1) != 0 ? -term : term;
        }
        return sum;
    }

    public static BigInteger Cos(BigInteger x, int f)
    {
        BigInteger v = (x * x) >> f, term = BigInteger.One << f, sum = term;
        for (int k = 1; !term.IsZero; k++)
        {
            term = ((term * v) >> f) / ((2L * k - 1) * (2 * k));
            sum += (k & 1) != 0 ? -term : term;
        }
        return sum;
    }

    public static BigInteger Exp(BigInteger x, int f)
    {
        BigInteger term = BigInteger.One << f, sum = term;
        for (int k = 1; !term.IsZero; k++)
        {
            term = ((term * x) >> f) / k;
            sum += term;
        }
        return sum;
    }

    /// <summary>Fractional bits on which the value and the oracle agree.</summary>
    public static long Agree(ApFloat a, BigInteger oracle, int f)
    {
        BigInteger d = BigInteger.Abs(Fix(a, f) - oracle);
        return f - (long)d.GetBitLength();
    }
}

// ----------------------------------------------------------------------------------------------
// Newton on the public API: what a divide and a root would cost built on Multiply. Faithful to
// within a few ulps, not correctly rounded: that takes one more long multiply to settle.
// ----------------------------------------------------------------------------------------------

static class Newton
{
    static readonly ApFloat One = (ApFloat)1L;

    static ApFloat Shift(ApFloat a, long k, int w) => new ApFloat(a.IsNegative ? -a.Significand : a.Significand, a.Exponent + k, w);

    static Stack<int> Steps(int w)
    {
        var steps = new Stack<int>();
        for (int g = w; g > 48; g = g / 2 + 4) steps.Push(g);
        return steps;
    }

    public static ApFloat Divide(ApFloat a, ApFloat b, int p)
    {
        int w = p + 32;
        long eb = P.Top(b);
        ApFloat bs = Shift(b, -eb, w + 16);                 // in [1, 2)
        ApFloat x = (ApFloat)(1.0 / (double)bs);
        foreach (int g in Steps(w))
        {
            int wg = g + 16;
            ApFloat e = ApFloat.Subtract(One, ApFloat.Multiply(bs.WithPrecision(wg), x, wg), wg);
            x = ApFloat.Add(x, ApFloat.Multiply(x, e, wg), wg);
        }
        return Shift(ApFloat.Multiply(a, x, w), -eb, p);
    }

    public static ApFloat Sqrt(ApFloat a, int p)
    {
        int w = p + 32;
        long even = P.Top(a) & ~1L;
        ApFloat s = Shift(a, -even, w + 16);                // in [1, 4)
        ApFloat y = (ApFloat)(1.0 / Math.Sqrt((double)s));  // y is 1 / root s
        foreach (int g in Steps(w))
        {
            int wg = g + 16;
            ApFloat e = ApFloat.Subtract(One, ApFloat.Multiply(s.WithPrecision(wg), ApFloat.Multiply(y, y, wg), wg), wg);
            y = ApFloat.Add(y, Shift(ApFloat.Multiply(y, e, wg), -1, wg), wg);
        }
        return Shift(ApFloat.Multiply(s, y, w), even / 2, p);
    }
}

// ----------------------------------------------------------------------------------------------
// Sections
// ----------------------------------------------------------------------------------------------

static class Sections
{
    public static void N(int[] sizes, string filter)
    {
        foreach (int p in new[] { 256, 4096, 16384 })
        {
            ApFloat a = V.Third(p), b = V.Root2(p);
            ApFloat d = ApFloat.Subtract(Newton.Divide(a, b, p), ApFloat.Divide(a, b, p), p + 8);
            ApFloat s = ApFloat.Subtract(Newton.Sqrt(a, p), ApFloat.Sqrt(a, p), p + 8);
            H.Line($"# newton check p={p}: divide off by 2^{(d.IsZero ? "-inf" : P.Top(d).ToString())}, sqrt off by 2^{(s.IsZero ? "-inf" : P.Top(s).ToString())}");
        }
        var cache = new Dictionary<int, (ApFloat A, ApFloat B)>();
        (ApFloat A, ApFloat B) Operands(int p)
        {
            if (!cache.TryGetValue(p, out var v)) cache[p] = v = (V.Third(p), V.Root2(p));
            return v;
        }
        var ops = new List<Op>
        {
            new("Mul", 2, p => { var (a, b) = Operands(p); return () => H.Sink = ApFloat.Multiply(a, b, p); }),
            new("Div", 2, p => { var (a, b) = Operands(p); return () => H.Sink = ApFloat.Divide(a, b, p); }),
            new("Sqrt", 2, p => { var (a, _) = Operands(p); return () => H.Sink = ApFloat.Sqrt(a, p); }),
            new("DivNewton", 1.6, p => { var (a, b) = Operands(p); return () => H.Sink = Newton.Divide(a, b, p); }),
            new("SqrtNewton", 1.6, p => { var (a, _) = Operands(p); return () => H.Sink = Newton.Sqrt(a, p); }),
        };
        if (filter != "")
        {
            var keep = filter.Split(',').ToHashSet();
            ops = ops.Where(o => keep.Contains(o.Name)).ToList();
        }
        H.Warm(ops, [256, 4096], 0.3);
        H.Sized("N", sizes, ops, 0.02);
    }

    public static void B(int[] sizes, string filter)
    {
        // The operands must be the real values: checked against the library where that's cheap.
        foreach (int p in new[] { 53, 256, 1024, 4096 })
        {
            bool third = V.Third(p).Equals(ApFloat.Divide((ApFloat)1L, (ApFloat)3L, p));
            bool root = V.Root2(p).Equals(ApFloat.Sqrt((ApFloat)2L, p));
            int d = ApFloat.DecimalDigitsFor(p);
            string e = V.Root2(p).ToString("E" + (d - 1), H.Inv);
            bool reads = ApFloat.Parse(e, p, RoundingMode.ToNearestEven, H.Inv).Equals(V.Root2(p));
            string text = V.Root2Text(d);
            int sure = d - 4;                    // the last digits go past what p bits hold, so they needn't be root 2's
            bool digits = e[..sure] == text[..sure];
            H.Line($"# operands p={p}: third={third} root2={root} roundtrip={reads} digits={digits} sigBits(third)={V.Third(p).Significand.BitLength} sigBits(root2)={V.Root2(p).Significand.BitLength}");
            if (!(third && root && reads && digits)) throw new InvalidOperationException("Operand check failed.");
        }

        var cache = new Dictionary<int, (ApFloat A, ApFloat B)>();
        (ApFloat A, ApFloat B) Operands(int p)
        {
            if (!cache.TryGetValue(p, out var v)) cache[p] = v = (V.Third(p), V.Root2(p));
            return v;
        }
        Action Two(Func<ApFloat, ApFloat, int, ApFloat> f, int p)
        {
            var (a, b) = Operands(p);
            return () => H.Sink = f(a, b, p);
        }
        ApFloat small = (ApFloat)25L;
        var ops = new List<Op>
        {
            new("Add", 1, p => Two((a, b, q) => ApFloat.Add(a, b, q), p)),
            new("Mul", 2, p => Two((a, b, q) => ApFloat.Multiply(a, b, q), p)),
            new("Div", 2, p => Two((a, b, q) => ApFloat.Divide(a, b, q), p)),
            new("Sqrt", 2, p => Two((a, b, q) => ApFloat.Sqrt(a, q), p)),
            new("MulSmall", 1, p => Two((a, b, q) => ApFloat.Multiply(b, small, q), p)),
            new("DivSmall", 1, p => Two((a, b, q) => ApFloat.Divide(b, small, q), p)),
            new("TextE50", 1.5, p =>
            {
                ApFloat x = Operands(p).B;
                return () => H.Sink = x.ToString("E49", H.Inv);
            }),
            new("TextEfull", 2, p =>
            {
                ApFloat x = Operands(p).B;
                string f = "E" + (ApFloat.DecimalDigitsFor(p) - 1);
                return () => H.Sink = x.ToString(f, H.Inv);
            }),
            new("TextR", 2.2, p =>
            {
                ApFloat x = Operands(p).B;
                return () => H.Sink = x.ToString("R", H.Inv);
            }),
            new("Parse", 2, p =>
            {
                string s = V.Root2Text(ApFloat.DecimalDigitsFor(p));
                return () => H.Sink = ApFloat.Parse(s, p, RoundingMode.ToNearestEven, H.Inv);
            }),
        };
        if (filter != "")
        {
            var keep = filter.Split(',').ToHashSet();
            ops = ops.Where(o => keep.Contains(o.Name)).ToList();
        }
        H.Warm(ops, [256, 4096], 0.3);
        H.Sized("B", sizes, ops, 0.02);
    }

    public static void C()
    {
        var rng = new Random(20261107);

        ApInt Factorial(int n)
        {
            ApInt f = 1;
            for (int i = 2; i <= n; i++) f *= i;
            return f;
        }

        // Correctness first, once, against BigInteger.
        BigInteger fb = BigInteger.One;
        for (int i = 2; i <= 10000; i++) fb *= i;
        bool factorialOk = V.ToBig(Factorial(10000)) == fb;
        bool powOk = V.ToBig(ApInt.Pow(3, 100000)) == BigInteger.Pow(3, 100000);
        bool textOk = Factorial(1000).ToString() == V.ToBig(Factorial(1000)).ToString(CultureInfo.InvariantCulture);
        H.Line($"# checks: factorial(10000)={factorialOk} pow(3,100000)={powOk} text(1000!)={textOk}");

        var items = new List<(string Name, Action Act)>
        {
            ("Fact1000", () => H.Sink = Factorial(1000)),
            ("Fact10000", () => H.Sink = Factorial(10000)),
            ("Fact100000", () => H.Sink = Factorial(100000)),
            ("Pow3_100000", () => H.Sink = ApInt.Pow(3, 100000)),
            ("Pow2_10000000", () => H.Sink = ApInt.Pow(2, 10000000)),
        };
        foreach (int bits in new[] { 256, 1024, 2048, 4096 })
        {
            ApInt v = V.RandBits(rng, bits), e = V.RandBits(rng, bits), m = V.RandBits(rng, bits) | 1;
            bool ok = V.ToBig(ApInt.ModPow(v, e >> (bits > 1024 ? bits - 256 : 0), m)) == BigInteger.ModPow(V.ToBig(v), V.ToBig(e >> (bits > 1024 ? bits - 256 : 0)), V.ToBig(m));
            H.Line($"# modpow check {bits} bits (exponent cut to 256 bits above 1024): {ok}");
            items.Add(($"ModPow{bits}", () => H.Sink = ApInt.ModPow(v, e, m)));
        }
        {
            ApInt a = V.RandBits(rng, 4096), b = V.RandBits(rng, 4096);
            H.Line($"# gcd check: {V.ToBig(ApInt.GreatestCommonDivisor(a, b)) == BigInteger.GreatestCommonDivisor(V.ToBig(a), V.ToBig(b))}");
            items.Add(("Gcd4096", () => H.Sink = ApInt.GreatestCommonDivisor(a, b)));
            ApInt n = V.RandBits(rng, 100000);
            H.Line($"# sqrt check: {V.ToBig(ApInt.Sqrt(n)) == V.ISqrt(V.ToBig(n))}");
            items.Add(("Sqrt100000", () => H.Sink = ApInt.Sqrt(n)));
        }

        // Warm on small sizes of the same paths.
        {
            ApInt v = V.RandBits(rng, 256), e = V.RandBits(rng, 256), m = V.RandBits(rng, 256) | 1, n = V.RandBits(rng, 2000);
            var warm = new Action[]
            {
                () => H.Sink = Factorial(1000), () => H.Sink = ApInt.Pow(3, 2000), () => H.Sink = ApInt.ModPow(v, e, m),
                () => H.Sink = ApInt.GreatestCommonDivisor(v, e), () => H.Sink = ApInt.Sqrt(n), () => H.Sink = Factorial(300).ToString(),
            };
            foreach (Action a in warm)
            {
                long t0 = Stopwatch.GetTimestamp();
                do a(); while (Stopwatch.GetElapsedTime(t0).TotalSeconds < 0.4);
            }
        }

        H.Line("kind,section,time,round,size,op,wall_s,cpu_s,reps,calib_ms");
        var wall = items.ToDictionary(i => i.Name, _ => new List<double>());
        var cpu = items.ToDictionary(i => i.Name, _ => new List<double>());
        var capped = new HashSet<string>();
        for (int r = 0; r < H.Rounds; r++)
        {
            double cal = H.Calibrate();
            for (int i = 0; i < items.Count; i++)
            {
                var (name, act) = items[(i + r) % items.Count];
                if (capped.Contains(name)) continue;
                H.Tidy();
                var (w, c, reps) = H.Measure(act, 0.02);
                wall[name].Add(w);
                cpu[name].Add(c);
                H.Line($"T,C,{H.Stamp()},{r},0,{name},{w:G6},{c:G6},{reps},{cal:0.0}");
                if (w > H.Cap)
                {
                    capped.Add(name);
                    H.Line($"CAPPED,C,{H.Stamp()},{r},0,{name},{w:G6},,,passed the cap");
                }
            }
        }
        foreach (var (name, _) in items)
            H.Line($"MEDIAN,C,{H.Stamp()},n={wall[name].Count},0,{name},{H.Median(wall[name]):G6},{H.Median(cpu[name]):G6},{H.Show(H.Median(wall[name]))},");

        // Decimal text, by size in bits, so that the quadratic prediction can stop the series.
        var values = new Dictionary<int, ApInt>();
        void Value(ApInt v) => values[(int)v.BitLength] = v;
        Value(V.ToAp(BigInteger.Pow(3, 25000)));
        Value(Factorial(1000));
        Value(Factorial(10000));
        Value(ApInt.Pow(3, 100000));
        Value(ApInt.One << 1000000);
        Value(V.ToAp(BigInteger.Pow(3, 400000)));
        Value(V.ToAp(fb) << 0);                       // 10000! again, same size: harmless
        BigInteger big = BigInteger.One;
        for (int i = 2; i <= 100000; i++) big *= i;
        Value(V.ToAp(big));
        Value(ApInt.One << 10000000);
        int[] sizes = values.Keys.OrderBy(x => x).ToArray();
        H.Line($"# decimal text sizes (bits): {string.Join(' ', sizes)}");
        var ops = new List<Op> { new("ToStringDec", 2, bits => { ApInt v = values[bits]; return () => H.Sink = v.ToString(); }) };
        H.Sized("C", sizes, ops, 0.02);
    }

    public static void D(int[] sizes, string filter)
    {
        var inv = H.Inv;
        const string pi50 = "31415926535897932384626433832795028841971693993751";
        const string e50 = "27182818284590452353602874713526624977572470936999";
        const string ln50 = "69314718055994530941723212145817656807550013436025";

        string First50(ApFloat x)
        {
            string s = x.ToString("E59", inv);
            return s.Replace(".", "")[..50];
        }

        // Checks are run once per size per arm, after its timing, and never timed.
        string CheckAgainst(ApFloat v, Func<int, BigInteger> oracle, int p, string? digits)
        {
            int f = p + 96;
            long agree = O.Agree(v, oracle(f), f);
            string d = digits is null ? "" : p >= 200 ? $" first50={(First50(v) == digits ? "ok" : "WRONG")}" : "";
            return $"agreeBits={agree}/{p}{d}";
        }

        var cacheX = new Dictionary<int, (ApFloat X, ApFloat Y, ApFloat R)>();
        // x = root2 - 1 = 0.41421..., y = 1/3, r = root2: each fills the significand at the working precision.
        (ApFloat X, ApFloat Y, ApFloat R) Args(int p)
        {
            if (cacheX.TryGetValue(p, out var v)) return v;
            int w = P.W(p) + 64;
            ApFloat r = V.Root2(w);
            v = (ApFloat.Subtract(r, (ApFloat)1L, w), V.Third(w), r);
            cacheX[p] = v;
            return v;
        }

        var arms = new List<(string Name, double Growth, Func<int, ApFloat> Run, Func<int, ApFloat, string> Check)>
        {
            ("pi_plain", 2, p => P.Pi(p, false), (p, v) => CheckAgainst(v, O.Pi, p, pi50)),
            ("pi_shrink", 2, p => P.Pi(p, true), (p, v) => CheckAgainst(v, O.Pi, p, pi50)),
            ("pi_limbs_bitwise", 2, p => Fx.Pi(p, true), (p, v) => CheckAgainst(v, O.Pi, p, pi50)),
            ("pi_limbs_cpu", 2, p => Fx.Pi(p, false), (p, v) => CheckAgainst(v, O.Pi, p, pi50)),
            ("pi_chudnovsky", 2, p => P.PiChudnovsky(p), (p, v) => CheckAgainst(v, O.Pi, p, pi50)),
            ("e_binary_split", 2, p => P.EBinarySplit(p), (p, v) => CheckAgainst(v, O.E, p, e50)),
            ("ln2_binary_split", 2, p => P.Ln2BinarySplit(p), (p, v) => CheckAgainst(v, O.Ln2, p, ln50)),
            ("ln2_plain", 2, p => P.Ln2(p, false), (p, v) => CheckAgainst(v, O.Ln2, p, ln50)),
            ("ln2_shrink", 2, p => P.Ln2(p, true), (p, v) => CheckAgainst(v, O.Ln2, p, ln50)),
            ("e_r0_plain", 2, p => P.E(p, 0, false), (p, v) => CheckAgainst(v, O.E, p, e50)),
            ("e_r0_shrink", 2, p => P.E(p, 0, true), (p, v) => CheckAgainst(v, O.E, p, e50)),
            ("e_r8", 2.2, p => P.E(p, 8, true), (p, v) => CheckAgainst(v, O.E, p, e50)),
            ("e_rsqrt", 2.5, p => P.E(p, (int)Math.Sqrt(p), true), (p, v) => CheckAgainst(v, O.E, p, e50)),
            ("sin1_taylor", 2, p => P.Sin1(P.W(p), true).WithPrecision(p), (p, v) => CheckAgainst(v, f => O.Sin(BigInteger.One << f, f), p, null)),
            ("expr_sin2cos2", 2.2, p => P.Expression(p, true), (p, v) => v.IsZero ? $"exactly zero at {P.W(p)} working bits" : $"top={P.Top(v)} working={P.W(p)}"),
            ("sinx_plain", 2.8, p => P.SinPlain(Args(p).X, p), (p, v) => CheckAgainst(v, f => O.Sin(O.Fix(Args(p).X, f), f), p, null)),
            ("sinx_smith", 2.5, p => P.SinSmith(Args(p).X, p, false), (p, v) => CheckAgainst(v, f => O.Sin(O.Fix(Args(p).X, f), f), p, null)),
            ("sinx_smith_shrink", 2.5, p => P.SinSmith(Args(p).X, p, true), (p, v) => CheckAgainst(v, f => O.Sin(O.Fix(Args(p).X, f), f), p, null)),
            ("cosx_smith_shrink", 2.5, p => P.CosSmith(Args(p).X, p, true), (p, v) => CheckAgainst(v, f => O.Cos(O.Fix(Args(p).X, f), f), p, null)),
            ("expx_plain", 2.8, p => P.ExpPlain(Args(p).X, p), (p, v) => CheckAgainst(v, f => O.Exp(O.Fix(Args(p).X, f), f), p, null)),
            ("expx_smith_shrink", 2.5, p => P.ExpSmith(Args(p).X, p, 0, true), (p, v) => CheckAgainst(v, f => O.Exp(O.Fix(Args(p).X, f), f), p, null)),
            ("expx_smith_r8", 2.5, p => P.ExpSmith(Args(p).X, p, 8, true), (p, v) => CheckAgainst(v, f => O.Exp(O.Fix(Args(p).X, f), f), p, null)),
            ("lnx_halley", 2.5, p => P.Ln(Args(p).X, P.W(p)).WithPrecision(p), (p, v) =>
            {
                // exp(ln x) should give x back.
                int f = p + 96;
                BigInteger back = O.Exp(O.Fix(v, f), f);
                return $"expBackAgreeBits={O.Agree(Args(p).X, back, f)}/{p}";
            }),
            ("powxy", 2.5, p => P.Pow(Args(p).R, Args(p).Y, p), (p, v) =>
            {
                // (2^(1/2))^(1/3) to the sixth power is 2.
                int w = p + 64;
                ApFloat s = ApFloat.Multiply(v, v, w), c = ApFloat.Multiply(s, v, w), six = ApFloat.Multiply(c, c, w);
                return $"sixthPowerMinus2Top={P.Top(ApFloat.Subtract(six, (ApFloat)2L, w))}";
            }),
        };
        if (filter != "")
        {
            var keep = filter.Split(',').ToHashSet();
            arms = arms.Where(a => keep.Contains(a.Name)).ToList();
        }

        // Warm every arm, twice, at a small size.
        foreach (var a in arms)
        {
            long t0 = Stopwatch.GetTimestamp();
            do H.Sink = a.Run(1024); while (Stopwatch.GetElapsedTime(t0).TotalSeconds < 0.3);
        }

        H.Line("kind,section,time,round,size,op,wall_s,cpu_s,reps,calib_ms,shares");
        var ops = arms.ToDictionary(a => a.Name, a => new Op(a.Name, a.Growth, _ => () => { }));
        foreach (int p in sizes)
        {
            var live = new List<(string Name, Func<int, ApFloat> Run, Func<int, ApFloat, string> Check, List<double> Wall, List<double> Cpu)>();
            foreach (var a in arms)
            {
                Op op = ops[a.Name];
                if (op.Stopped)
                {
                    H.Line($"STOPPED,D,{H.Stamp()},,{p},{a.Name},,,,{op.Why}");
                    continue;
                }
                if (op.LastSize > 0)
                {
                    double g = op.Growth;
                    if (op.PrevSize > 0 && op.LastMedian > 0.005)
                        g = Math.Min(3, Math.Max(g, Math.Log(op.LastMedian / op.PrevMedian) / Math.Log((double)op.LastSize / op.PrevSize)));
                    double predicted = op.LastMedian * Math.Pow((double)p / op.LastSize, g);
                    if (predicted > H.SkipAbove)
                    {
                        op.Stopped = true;
                        op.Why = $"predicted {predicted:0.0} s at exponent {g:0.00} from {op.LastSize}";
                        H.Line($"SKIP,D,{H.Stamp()},,{p},{a.Name},{predicted:0.###},,,{op.Why}");
                        continue;
                    }
                }
                _ = Args(p);
                live.Add((a.Name, a.Run, a.Check, [], []));
            }
            for (int r = 0; r < H.Rounds; r++)
            {
                double cal = H.Calibrate();
                for (int i = 0; i < live.Count; i++)
                {
                    var e = live[(i + r) % live.Count];
                    Op op = ops[e.Name];
                    if (op.Capped) continue;
                    H.Tidy();
                    T.Reset();
                    int pp = p;
                    var run = e.Run;
                    var (wall, cpu, reps) = H.Measure(() => H.Sink = run(pp), 0.05);
                    // The first batches of a Measure also count in T, so the shares use T's own total.
                    double charged = (T.TAdd + T.TMul + T.TDsm + T.TDbg + T.TOther) / (double)Stopwatch.Frequency;
                    string shares = charged > 0 ? T.Shares(charged) : "no wrapped calls";
                    e.Wall.Add(wall);
                    e.Cpu.Add(cpu);
                    H.Line($"T,D,{H.Stamp()},{r},{p},{e.Name},{wall:G6},{cpu:G6},{reps},{cal:0.0},{shares}");
                    if (wall > H.Cap) op.Capped = true;
                }
            }
            foreach (var e in live)
            {
                Op op = ops[e.Name];
                double med = H.Median(e.Wall);
                string check;
                try { check = op.Capped && med > 2 * H.Cap ? "not rechecked" : e.Check(p, e.Run(p)); }
                catch (Exception ex) { check = "CHECK THREW " + ex.GetType().Name + ": " + ex.Message; }
                H.Line($"MEDIAN,D,{H.Stamp()},n={e.Wall.Count},{p},{e.Name},{med:G6},{H.Median(e.Cpu):G6},{H.Show(med)},{check}");
                op.PrevMedian = op.LastMedian;
                op.PrevSize = op.LastSize;
                op.LastMedian = med;
                op.LastSize = p;
                if (op.Capped)
                {
                    op.Stopped = true;
                    op.Why = $"passed the {H.Cap:0} s cap at {p}";
                }
            }
        }
    }
}
