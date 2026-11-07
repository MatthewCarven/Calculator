#:project C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\natural-api\src\Natural\Natural.csproj
#:property PublishAot=false
#pragma warning disable SYSLIB0046, SYSLIB0006, CS0618
// Behaviour at the extremes. The driver runs each case as a child process of this same program,
// with DOTNET_GCHeapHardLimit=0x40000000 and a timeout, and kills the whole child at the timeout.
// Run: Extremes [case ...]   With no case names, runs them all, one at a time.
// All timings are one run each on a machine with two speeds: read them as orders of magnitude.
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using Natural;
using Natural.Jobs;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var inv = CultureInfo.InvariantCulture;

static void T(string label, Func<object?> f)
{
    var sw = Stopwatch.StartNew();
    try
    {
        object? v = f();
        string t = v?.ToString() ?? "null";
        if (t.Length > 100) t = t[..60] + $"...[{t.Length} chars]..." + t[^20..];
        Console.WriteLine($"  {label,-52} = {t}   [{sw.Elapsed.TotalMilliseconds:F1} ms]");
    }
    catch (Exception e)
    {
        string m = e.Message.Replace("\r", " ").Replace("\n", " ");
        if (m.Length > 70) m = m[..70] + "...";
        Console.WriteLine($"  {label,-52} ! {e.GetType().Name}: {m}   [{sw.Elapsed.TotalMilliseconds:F1} ms]");
    }
}

static string Heap() => $"{GC.GetTotalMemory(false) >> 20} MiB";
ApFloat Huge(long e) => new ApFloat(1, e);

var cases = new (string Name, int Seconds, Action Body)[]
{
    ("pow2", 30, () =>
    {
        T("ApInt.Pow(2, 10^6).BitLength", () => ApInt.Pow(2, 1_000_000).BitLength);
        T("ApInt.Pow(2, 10^8).BitLength", () => ApInt.Pow(2, 100_000_000).BitLength);
        T("ApInt.Pow(2, 10^9).BitLength", () => ApInt.Pow(2, 1_000_000_000).BitLength);
    }),
    ("pow2max", 30, () => T("ApInt.Pow(2, int.MaxValue).BitLength", () => ApInt.Pow(2, int.MaxValue).BitLength)),
    ("pow3ladder", 25, () =>
    {
        foreach (int n in new[] { 1000, 10_000, 100_000, 300_000, 1_000_000, 3_000_000, 10_000_000 })
            T($"ApInt.Pow(3, {n}).BitLength", () => ApInt.Pow(3, n).BitLength);
    }),
    ("sq3ladder", 25, () =>
    {
        ApInt x = 3;
        for (int k = 1; k <= 40; k++) { int kk = k; ApInt y = x; T($"x := x*x, press {kk} (from 3)", () => { y = y * y; return y.BitLength; }); x = y; }
    }),
    ("shl", 15, () =>
    {
        T("(One << int.MaxValue).BitLength", () => (ApInt.One << int.MaxValue).BitLength);
        T("(One >> int.MinValue).BitLength  [neg: left 2^31]", () => (ApInt.One >> int.MinValue).BitLength);
        T("One << int.MinValue             [neg: right 2^31]", () => ApInt.One << int.MinValue);
        T("(-1) >> int.MaxValue", () => (ApInt)(-1) >> int.MaxValue);
        T("(-1) << int.MinValue", () => (ApInt)(-1) << int.MinValue);
        T("One >> int.MaxValue", () => ApInt.One >> int.MaxValue);
    }),
    ("shlchain", 20, () =>
    {
        ApInt x = ApInt.One;
        try
        {
            for (int i = 1; i <= 8; i++)
            {
                x <<= int.MaxValue;
                Console.WriteLine($"  shift {i}: BitLength {x.BitLength}, managed heap {Heap()}");
            }
        }
        catch (Exception e) { Console.WriteLine($"  caught {e.GetType().Name}: {e.Message}"); }
        x = default;
        GC.Collect();
        T("1 + 1 after catching it", () => (ApInt)1 + 1);
        T("Pow(3, 1000).BitLength after", () => ApInt.Pow(3, 1000).BitLength);
    }),
    ("squarechain", 20, () =>
    {
        ApInt x = ApInt.One << (1 << 27);
        try
        {
            for (int i = 1; i <= 6; i++)
            {
                x = x * x;
                Console.WriteLine($"  x*x {i}: BitLength {x.BitLength}, managed heap {Heap()}");
            }
        }
        catch (Exception e) { Console.WriteLine($"  caught {e.GetType().Name}: {e.Message}"); }
    }),
    ("hexbig", 25, () =>
    {
        T("(One << 2^26).ToString(\"X\").Length", () => (ApInt.One << (1 << 26)).ToString("X").Length);
        T("(One << 2^28).ToString(\"X\").Length", () => (ApInt.One << (1 << 28)).ToString("X").Length);
        T("(One << 2^30).ToString(\"X\").Length", () => (ApInt.One << (1 << 30)).ToString("X").Length);
    }),
    ("decbig", 12, () =>
    {
        foreach (int b in new[] { 1 << 12, 1 << 14, 1 << 16, 1 << 18, 1 << 20, 1 << 22, 1 << 24 })
            T($"(One << {b}).ToString().Length", () => (ApInt.One << b).ToString().Length);
    }),
    ("maxprec", 40, () =>
    {
        T("MaxPrecision", () => ApFloat.MaxPrecision);
        T("new ApFloat(1, 0, MaxPrecision).Precision", () => new ApFloat(1, 0, ApFloat.MaxPrecision).Precision);
        T("new ApFloat(1, 0, MaxPrecision + 1)", () => new ApFloat(1, 0, ApFloat.MaxPrecision + 1));
        T("One.WithPrecision(MaxPrecision).Precision", () => ApFloat.One.WithPrecision(ApFloat.MaxPrecision).Precision);
        T("DecimalDigitsFor(MaxPrecision)", () => ApFloat.DecimalDigitsFor(ApFloat.MaxPrecision));
        T("new IeeeFormat(62, MaxPrecision)", () => new IeeeFormat(62, ApFloat.MaxPrecision).Width);
        foreach (int p in new[] { 1 << 20, 1 << 22, 1 << 24, 1 << 26, 1 << 28, 1 << 30 })
            T($"Divide(1, 3, {p}) significand bits", () => ApFloat.Divide(1, 3, p).Significand.BitLength);
    }),
    ("sqrtladder", 40, () =>
    {
        foreach (int p in new[] { 1 << 12, 1 << 14, 1 << 16, 1 << 18, 1 << 20, 1 << 22 })
            T($"Sqrt(2, {p}) significand bits", () => ApFloat.Sqrt(2, p).Significand.BitLength);
    }),
    ("mulladder", 30, () =>
    {
        foreach (int p in new[] { 1 << 12, 1 << 14, 1 << 16, 1 << 18, 1 << 20 })
        {
            ApFloat x = ApFloat.Divide(1, 3, p);
            T($"Multiply(x, x, 64), x has {p} bits", () => ApFloat.Multiply(x, x, 64).Precision);
            T($"  the same after WithPrecision(64)", () => { var y = x.WithPrecision(64); return ApFloat.Multiply(y, y, 64).Precision; });
        }
    }),
    ("expmul", 10, () =>
    {
        T("2^(2^62) * 2^(2^62)", () => { var x = Huge(1L << 62); return x * x; });
        T("2^(2^62) squared in IeeeContext(256)", () => { var x = Huge(1L << 62); var c = new IeeeContext(256); var r = c.Multiply(x, x); return $"{r:E2} {c.Flags}"; });
        T("2^(2^62) squared in IeeeContext(Binary64)", () => { var x = Huge(1L << 62); var c = new IeeeContext(IeeeFormat.Binary64); var r = c.Multiply(x, x); return $"{r:E2} {c.Flags}"; });
        T("Multiply(2^(2^62), same, Binary64)", () => { var x = Huge(1L << 62); return ApFloat.Multiply(x, x, IeeeFormat.Binary64).ToString("R"); });
        T("2^(-2^62) * 2^(-2^62)", () => { var x = Huge(-(1L << 62)); return (x * x).Exponent; });
        T("2^(-2^62) * 2^(-2^62) * 0.5", () => { var x = Huge(-(1L << 62)); return (x * x * 0.5).Exponent; });
        T("2^(2^62) / 2^(-2^62)", () => Huge(1L << 62) / Huge(-(1L << 62)));
        T("2^(-2^62) / 2^(2^62)", () => (Huge(-(1L << 62)) / Huge(1L << 62)).Exponent);
        T("1 / 2^(long.MinValue)", () => (ApFloat)1 / Huge(long.MinValue));
        T("1 / 2^(long.MaxValue)", () => ((ApFloat)1 / Huge(long.MaxValue)).Exponent);
        T("3 * 2^(2^63-1) built by ctor: Exponent", () => new ApFloat(3, long.MaxValue).Exponent);
        T("new ApFloat(2, long.MaxValue)  [odd-ing overflows]", () => new ApFloat(2, long.MaxValue).Exponent);
    }),
    ("expsquare", 10, () =>
    {
        foreach (var (name, start) in new (string, ApFloat)[] { ("2", (ApFloat)2), ("3", (ApFloat)3), ("1.5", (ApFloat)1.5), ("0.5", (ApFloat)0.5), ("1/3", (ApFloat)1 / 3) })
        {
            ApFloat x = start;
            int k = 0;
            try
            {
                while (k < 200) { x = x * x; k++; }
                Console.WriteLine($"  x := x*x from {name}: no failure in {k} presses, E = {x.Exponent}");
            }
            catch (Exception e)
            {
                long top = x.Exponent + x.Significand.BitLength - 1;
                Console.WriteLine($"  x := x*x from {name}: {e.GetType().Name} at press {k + 1}; last good top bit 2^{top}");
            }
        }
    }),
    ("topwrap", 10, () =>
    {
        ApFloat x = ApFloat.Parse("0x3p9223372036854775807");
        T("parsed 0x3p(2^63-1): Exponent", () => x.Exponent);
        T("computed top = Exponent + BitLength - 1", () => unchecked(x.Exponent + x.Significand.BitLength - 1));
        T("x > 1  [true value: true]", () => x > 1);
        T("x > 0  [true value: true]", () => x > 0);
        T("x < 1  [true value: false]", () => x < 1);
        T("x == 0", () => x == 0);
        T("x + 1 == x", () => (x + 1) == x);
        T("(x + 1).Exponent", () => (x + 1).Exponent);
        T("Max(x, 1) == x", () => ApFloat.Max(x, 1) == x);
        T("(double)x", () => (double)x);
        T("x.ToDouble(TowardZero)", () => x.ToDouble(RoundingMode.TowardZero));
        T("x in binary64 bytes", () => Convert.ToHexString(x.ToIeeeBytes(IeeeFormat.Binary64)));
        T("Sqrt(x).Exponent", () => ApFloat.Sqrt(x).Exponent);
        T("x * 2", () => (x * 2).Exponent);
        T("x / 2 exponent", () => (x / 2).Exponent);
        T("Floor(x).Exponent", () => ApFloat.Floor(x).Exponent);
    }),
    ("topwrapE", 8, () => { var x = ApFloat.Parse("0x3p9223372036854775807"); Console.WriteLine("  start E6"); T("x.ToString(\"E6\")", () => x.ToString("E6")); }),
    ("topwrapR", 8, () => { var x = ApFloat.Parse("0x3p9223372036854775807"); Console.WriteLine("  start R"); T("x.ToString(\"R\")", () => x.ToString("R")); }),
    ("topwrapDefault", 8, () => { var x = ApFloat.Parse("0x3p9223372036854775807"); Console.WriteLine("  start default"); T("x.ToString()", () => x.ToString()); }),
    ("topwrapJob", 8, () => { var x = ApFloat.Parse("0x3p9223372036854775807"); T("x.StartFormat(\"E6\") one slice", () => { using var j = x.StartFormat("E6"); return j.Continue(TimeSpan.FromMilliseconds(200)) + " " + j.Progress; }); }),
    ("extremesPrint", 10, () =>
    {
        foreach (string t in new[] { "0x1p9223372036854775807", "0x1p9223372036854775000", "0x1p9000000000000000000", "0x1p-9223372036854775808", "0x1p-9000000000000000000" })
        {
            ApFloat x = ApFloat.Parse(t);
            T($"{t} E6", () => x.ToString("E6"));
            T($"{t} A", () => x.ToString("A"));
        }
        foreach (string t in new[] { "1e2000000000000000000", "1e2776511644261678000", "1e2776511644261678500", "1e2776511644261678565", "1e2776511644261678566", "1e2776511644261678600", "1e2776511644261679000", "1e2776511644261680000", "1e2776511644261700000", "1e2776511644262000000", "1e2776511644270000000", "1e2776511645000000000", "1e2776512000000000000", "1e2800000000000000000" })
        {
            T($"Parse {t} Exponent", () => { var x = ApFloat.Parse(t, 64); return $"{x.Exponent} top {unchecked(x.Exponent + x.Significand.BitLength - 1)} >1:{x > 1}"; });
        }
        foreach (string t in new[] { "1e-2000000000000000000", "1e-2776511644261678000", "1e-2776511644261678500", "1e-2776511644261678566", "1e-2776511644261679000", "1e-2776511644262000000" })
        {
            T($"Parse {t} Exponent", () => { var x = ApFloat.Parse(t, 64); return $"{x.Exponent} <1:{x < 1}"; });
        }
    }),
    ("toint", 15, () =>
    {
        T("(ApInt)2^(2^31) BitLength", () => ((ApInt)Huge(1L << 31)).BitLength);
        T("(ApInt)2^(2^33) BitLength  [1 GiB]", () => ((ApInt)Huge(1L << 33)).BitLength);
        T("(ApInt)2^(2^35) BitLength  [4 GiB]", () => ((ApInt)Huge(1L << 35)).BitLength);
        T("(ApInt)2^(2^36) BitLength", () => ((ApInt)Huge(1L << 36)).BitLength);
        T("(ApInt)2^(10^18) BitLength", () => ((ApInt)Huge(1_000_000_000_000_000_000)).BitLength);
        T("(long)2^(2^35)", () => (long)Huge(1L << 35));
        T("long.CreateChecked(2^(2^35))", () => long.CreateChecked(Huge(1L << 35)));
        T("ApInt.CreateChecked(2^(2^35))", () => ApInt.CreateChecked(Huge(1L << 35)).BitLength);
        T("ApFloat.Floor(2^(2^35)) (no building)", () => ApFloat.Floor(Huge(1L << 35)).Exponent);
        T("ApFloat.IsInteger(2^(2^35))", () => ApFloat.IsInteger(Huge(1L << 35)));
    }),
    ("binarytext", 25, () =>
    {
        T("2^(2^20) \"B\" length", () => Huge(1L << 20).ToString("B").Length);
        T("2^(2^31 - 2) \"B\" (2^31 - 1 digits)", () => { string s = Huge((1L << 31) - 2).ToString("B"); return s.Length; });
        T("2^(2^31) \"B\"", () => { string s = Huge(1L << 31).ToString("B"); return $"{s.Length} chars: {s[..Math.Min(20, s.Length)]}"; });
        T("2^(2^32 + 4) \"B\"  [true text: 1 then 2^32+4 zeros]", () => { string s = Huge((1L << 32) + 4).ToString("B"); return $"{s.Length} chars: {s[..Math.Min(20, s.Length)]}"; });
        T("2^(2^40) \"B\"", () => Huge(1L << 40).ToString("B").Length);
    }),
    ("hexfloatnear", 10, () =>
    {
        T("2^(10^18) \"A\"", () => Huge(1_000_000_000_000_000_000).ToString("A"));
        T("2^(-10^18) \"A\"", () => Huge(-1_000_000_000_000_000_000).ToString("A"));
        T("2^(-2^24) \"B\" length", () => Huge(-(1L << 24)).ToString("B").Length);
        T("2^(-10^18) \"B\"", () => Huge(-1_000_000_000_000_000_000).ToString("B").Length);
        T("2^(-10^18) \"B10\"", () => Huge(-1_000_000_000_000_000_000).ToString("B10"));
        T("2^(-10^18) \"F2\"", () => Huge(-1_000_000_000_000_000_000).ToString("F2"));
        T("2^(10^18) \"F2\"", () => Huge(1_000_000_000_000_000_000).ToString("F2").Length);
        T("2^(10^18) \"#,##0\"", () => Huge(1_000_000_000_000_000_000).ToString("#,##0").Length);
        T("2^(10^18) \"E6\"", () => Huge(1_000_000_000_000_000_000).ToString("E6"));
        T("2^(10^18) \"G\"", () => Huge(1_000_000_000_000_000_000).ToString("G"));
        T("2^(10^18) default", () => Huge(1_000_000_000_000_000_000).ToString());
    }),
    ("remgap", 20, () =>
    {
        T("3 % 2^(10^12)", () => ((ApFloat)3 % Huge(1_000_000_000_000)).ToString("R"));
        T("Ieee754Remainder(3, 2^(10^12))", () => ApFloat.Ieee754Remainder(3, Huge(1_000_000_000_000)).ToString("R"));
        T("Ieee754Remainder(3, 2^(2^30))", () => ApFloat.Ieee754Remainder(3, Huge(1L << 30)).ToString("R"));
        T("Ieee754Remainder(3, 2^(2^33))", () => ApFloat.Ieee754Remainder(3, Huge(1L << 33)).ToString("R"));
        T("Ieee754Remainder(2^(10^12), 3)", () => ApFloat.Ieee754Remainder(Huge(1_000_000_000_000), 3).ToString("R"));
        T("2^(10^12) % 3", () => (Huge(1_000_000_000_000) % 3).ToString("R"));
        T("2^(-10^12) % 3", () => (Huge(-1_000_000_000_000) % 3).ToString("E2"));
        T("Ieee754Remainder(2^(-10^12), 3)", () => ApFloat.Ieee754Remainder(Huge(-1_000_000_000_000), 3).ToString("E2"));
        T("3 % 2^(-10^12)", () => ((ApFloat)3 % Huge(-1_000_000_000_000)).ToString("R"));
    }),
    ("addgap", 10, () =>
    {
        T("2^(10^12) + 1 exponent", () => (Huge(1_000_000_000_000) + 1).Exponent);
        T("1 + 2^(-10^12)", () => (ApFloat.One + Huge(-1_000_000_000_000)).ToString("R"));
        T("Add(1, 2^(-10^12), 53, TowardPositive) ulps", () => ApFloat.Add(1.0, Huge(-1_000_000_000_000), 53, RoundingMode.TowardPositive).ToString("R"));
        T("Compare 2^(10^12) with 2^(10^12)+1", () => Huge(1_000_000_000_000).CompareTo(ApFloat.Add(Huge(1_000_000_000_000), 1, 2000000)));
    }),
    ("intexact", 15, () =>
    {
        ApInt v = (ApInt.One << (1 << 30)) + 1;
        ApFloat f = v;
        T("v = 2^(2^30)+1, BitLength", () => v.BitLength);
        T("((ApFloat)v).Precision", () => f.Precision);
        T("(ApInt)(ApFloat)v == v  [doc says Exact]", () => (ApInt)f == v);
        ApInt w = (ApInt.One << 300) + 1;
        T("(ApInt)(ApFloat)(2^300+1) == it (implicit)", () => (ApInt)(ApFloat)w == w);
        T("(ApInt)new ApFloat(2^300+1) == it (ctor: 256 bits)", () => (ApInt)new ApFloat(w) == w);
    }),
    ("parsehang", 10, () =>
    {
        foreach (string t in new[] { "1e1000", "1e10000", "1e100000", "1e1000000", "1e10000000" })
            T($"ApInt.Parse(\"{t}\", Float)", () => ApInt.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture).BitLength);
    }),
    ("fmthuge", 12, () =>
    {
        foreach (int n in new[] { 1000, 10_000, 100_000, 300_000, 1_000_000 })
            T($"1.5 \"F{n}\" length", () => ((ApFloat)1.5).ToString("F" + n).Length);
    }),
    ("fmtF999", 8, () => T("1.5 \"F999999999\"", () => ((ApFloat)1.5).ToString("F999999999").Length)),
    ("fmtE999", 8, () => T("1.5 \"E999999999\"", () => ((ApFloat)1.5).ToString("E999999999").Length)),
    ("fmtG999", 8, () => T("(1/3) \"G999999999\"", () => ((ApFloat)1 / 3).ToString("G999999999").Length)),
    ("fmtB999", 8, () => T("1.5 \"B999999999\"", () => ((ApFloat)1.5).ToString("B999999999").Length)),
    ("fmtA999", 8, () => T("1.5 \"A999999999\"", () => ((ApFloat)1.5).ToString("A999999999").Length)),
    ("interrupt", 12, () =>
    {
        string? outcome = null;
        var t = new Thread(() =>
        {
            try { ApInt.Pow(3, 10_000_000); outcome = "finished"; }
            catch (Exception e) { outcome = e.GetType().Name; }
        }) { IsBackground = true };
        t.Start();
        Thread.Sleep(1000);
        t.Interrupt();
        Console.WriteLine($"  after Thread.Interrupt and 3 s more: stopped = {t.Join(3000)}, outcome = {outcome ?? "still running"}");
        T("Thread.Abort", () => { t.Abort(); return "no throw"; });
        Console.WriteLine($"  1 s later: alive = {!t.Join(1000)}");
    }),
    ("controlled", 12, () =>
    {
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(1000);
        try
        {
            ControlledExecution.Run(() => ApInt.Pow(3, 10_000_000), cts.Token);
            Console.WriteLine("  finished");
        }
        catch (Exception e) { Console.WriteLine($"  ControlledExecution.Run: {e.GetType().Name} after {sw.ElapsedMilliseconds} ms (asked to stop at 1000)"); }
        T("Pow(3, 1000).BitLength afterwards", () => ApInt.Pow(3, 1000).BitLength);
        T("(1/3) R afterwards", () => ((ApFloat)1 / 3).ToString("G10"));
        sw.Restart();
        using var cts2 = new CancellationTokenSource(1000);
        try
        {
            ControlledExecution.Run(() => (ApInt.One << (1 << 24)).ToString(), cts2.Token);
            Console.WriteLine("  finished");
        }
        catch (Exception e) { Console.WriteLine($"  (again, on double dabble): {e.GetType().Name} after {sw.ElapsedMilliseconds} ms"); }
        sw.Restart();
        using var cts3 = new CancellationTokenSource(1000);
        try
        {
            ControlledExecution.Run(() => ApFloat.Sqrt(2, 1 << 22), cts3.Token);
            Console.WriteLine("  finished");
        }
        catch (Exception e) { Console.WriteLine($"  (again, on Sqrt): {e.GetType().Name} after {sw.ElapsedMilliseconds} ms"); }
    }),
    ("jobslice", 20, () =>
    {
        var x = new ApFloat(1, 1 << 24);
        using (var job = x.StartFormat("F0"))
        {
            var sw = Stopwatch.StartNew();
            double worst = 0; int calls = 0;
            while (sw.ElapsedMilliseconds < 3000 && !job.IsComplete)
            {
                var c = Stopwatch.StartNew();
                job.Continue(TimeSpan.FromMilliseconds(50));
                worst = Math.Max(worst, c.Elapsed.TotalMilliseconds);
                calls++;
            }
            Console.WriteLine($"  2^(2^24) F0 job: {calls} Continue(50 ms) calls in 3 s, worst call {worst:F1} ms, Progress {job.Progress:P2}, WorkDone {job.WorkDone}");
            job.Dispose();
            T("Continue after Dispose", () => job.Continue(1));
            T("Result before complete", () => job.Result);
        }
        foreach (int p in new[] { 4096, 16384, 65536 })
        {
            var y = ApFloat.Divide(1, 3, p);
            foreach (string f in new[] { "R", "" })
            {
                using var job = y.StartFormat(f);
                var sw = Stopwatch.StartNew();
                double worst = 0; int calls = 0;
                while (sw.ElapsedMilliseconds < 2500 && !job.IsComplete)
                {
                    var c = Stopwatch.StartNew();
                    job.Continue(1);   // one step
                    worst = Math.Max(worst, c.Elapsed.TotalMilliseconds);
                    calls++;
                }
                Console.WriteLine($"  1/3 at {p} bits \"{f}\": {calls} steps, {sw.ElapsedMilliseconds} ms, worst single step {worst:F1} ms, complete {job.IsComplete}");
            }
        }
    }),
    ("display", 60, () =>
    {
        foreach (int p in new[] { 64, 256, 1024, 4096, 16384, 65536 })
        {
            ApFloat x = ApFloat.Divide(1, 3, p);
            int digits = ApFloat.DecimalDigitsFor(p);
            T($"1/3 at {p} bits, default ({digits} digits) length", () => x.ToString().Length);
            T($"1/3 at {p} bits, \"G{digits - 2}\" length", () => x.ToString("G" + (digits - 2)).Length);
            T($"1/3 at {p} bits, \"R\" length", () => x.ToString("R").Length);
            T($"1/3 at {p} bits, \"E20\" length", () => x.ToString("E20").Length);
        }
    }),
};

if (args.Length == 2 && args[0] == "child")
{
    cases.First(c => c.Name == args[1]).Body();
    return;
}

string exe = Environment.ProcessPath!;
foreach (var c in cases)
{
    if (args.Length > 0 && !args.Contains(c.Name)) continue;
    var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    psi.ArgumentList.Add("child");
    psi.ArgumentList.Add(c.Name);
    psi.Environment["DOTNET_GCHeapHardLimit"] = "0x40000000";
    psi.Environment["DOTNET_DbgEnableMiniDump"] = "0";
    psi.StandardOutputEncoding = Encoding.UTF8;
    Console.WriteLine($"[{c.Name}] limit {c.Seconds} s");
    var sw = Stopwatch.StartNew();
    using var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine("  stderr: " + e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    long peak = 0;
    while (!p.WaitForExit(100) && sw.Elapsed.TotalSeconds < c.Seconds)
    {
        try { p.Refresh(); peak = Math.Max(peak, p.PeakWorkingSet64); } catch { }
    }
    if (!p.HasExited)
    {
        p.Kill(entireProcessTree: true);
        p.WaitForExit(5000);
        Console.WriteLine($"[{c.Name}] KILLED at the {c.Seconds} s limit, peak working set {peak >> 20} MiB");
    }
    else
    {
        p.WaitForExit();
        Console.WriteLine($"[{c.Name}] exit code {p.ExitCode} after {sw.Elapsed.TotalSeconds:F1} s, peak working set {peak >> 20} MiB");
    }
    Console.WriteLine();
}
