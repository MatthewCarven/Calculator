using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime;
using System.Text;
using Natural;

// Throwaway threading probes for the calculator plan. Modes:
//   curve            cost of one op against size (the uninterruptible window)
//   stop-between     (a) worker thread, flag checked between ops: stop latency
//   controlled       (d) ControlledExecution.Run: stop latency of an aborted multiply
//   abort            what Thread.Abort / Interrupt do on .NET 10
//   worker           child worker: reads requests on stdin, answers on stdout
//   child            (b) the parent side: spawn, ping, round trip, transfer, kill, orphan
//   gc               heartbeat thread beside an in-process worker, at a chosen size
static class P
{
    static readonly Random Rng = new(12345);

    public static ApInt RandomBits(int bits, Random? r = null)
    {
        r ??= Rng;
        var hex = new char[bits / 4 + 3];
        hex[0] = '0'; hex[1] = 'x'; hex[2] = 'f';       // top nibble set, so the length is exact
        for (int i = 3; i < hex.Length; i++) hex[i] = "0123456789abcdef"[r.Next(16)];
        return ApInt.Parse(new string(hex));
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Runs f at least minRuns times (and for at least minMs in all); returns the fastest and slowest.</summary>
    static (double min, double max, int runs) Time(Action f, int minRuns = 3, double minMs = 60, double capMs = 20000)
    {
        double lo = double.MaxValue, hi = 0, total = 0;
        int runs = 0;
        while (runs < minRuns || total < minMs)
        {
            long t = Stopwatch.GetTimestamp();
            f();
            double ms = Ms(Stopwatch.GetTimestamp() - t);
            lo = Math.Min(lo, ms); hi = Math.Max(hi, ms); total += ms; runs++;
            if (total > capMs) break;
        }
        return (lo, hi, runs);
    }

    static object? _sink;

    static void Curve(string[] args)
    {
        int top = args.Length > 1 ? int.Parse(args[1]) : 262144;
        Console.WriteLine("bits      op        min_ms     max_ms  runs");
        for (int n = 256; n <= top; n *= 4)
        {
            ApInt a = RandomBits(n), b = RandomBits(n), wide = RandomBits(2 * n);
            ApFloat fa = new(a, -n, n), fb = new(b, -n, n);
            void Row(string name, Action f, int minRuns = 3)
            {
                var (lo, hi, runs) = Time(f, minRuns);
                Console.WriteLine($"{n,-9} {name,-8} {lo,10:F4} {hi,10:F4} {runs,5}");
            }
            Row("int*", () => _sink = a * b);
            Row("int/", () => _sink = wide / b);
            Row("intsqrt", () => _sink = ApInt.Sqrt(wide));
            Row("float+", () => _sink = ApFloat.Add(fa, fb, n));
            Row("float*", () => _sink = ApFloat.Multiply(fa, fb, n));
            Row("float/", () => _sink = ApFloat.Divide(fa, fb, n));
            Row("fsqrt", () => _sink = ApFloat.Sqrt(fa, n));
            Row("E20", () => _sink = fa.ToString("E20"));
            Row("hex", () => _sink = a.ToString("X"));
            if (n <= 65536)
            {
                string dec = a.ToString();
                Row("fmt-dec", () => _sink = a.ToString());
                Row("parse", () => _sink = ApInt.Parse(dec));
            }
        }
    }

    // Stop between operations: the worker does a series of float multiplies at a size; "Stop" lands
    // at a random moment; latency is flag to the worker's exit from its loop.
    static void StopBetween(string[] args)
    {
        foreach (int n in new[] { 4096, 16384, 65536, 262144 })
        {
            ApFloat x = new(RandomBits(n), -n, n), y = new(RandomBits(n), -n, n);
            var lat = new List<double>();
            int trials = n >= 262144 ? 3 : n >= 65536 ? 8 : 12;
            for (int t = 0; t < trials; t++)
            {
                var stop = new ManualResetEventSlim(false);
                long stoppedAt = 0;
                var done = new TaskCompletionSource<long>();
                var worker = new Thread(() =>
                {
                    ApFloat r = x;
                    while (!Volatile.Read(ref _flag)) r = ApFloat.Multiply(r, y, n);
                    _sink = r;
                    done.SetResult(Stopwatch.GetTimestamp());
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                Volatile.Write(ref _flag, false);
                worker.Start();
                Thread.Sleep(Rng.Next(20, 120));
                stoppedAt = Stopwatch.GetTimestamp();
                Volatile.Write(ref _flag, true);
                long end = done.Task.Result;
                lat.Add(Ms(end - stoppedAt));
            }
            lat.Sort();
            Console.WriteLine($"bits {n,-8} trials {trials,3}  stop latency ms: min {lat[0],9:F2}  median {lat[lat.Count / 2],9:F2}  max {lat[^1],9:F2}");
        }
    }
    static bool _flag;

    static void Abort()
    {
        var t = new Thread(() => { while (true) Thread.SpinWait(100); }) { IsBackground = true };
        t.Start();
#pragma warning disable SYSLIB0006
        try { t.Abort(); Console.WriteLine("Thread.Abort: returned"); }
        catch (Exception e) { Console.WriteLine($"Thread.Abort: {e.GetType().Name}: {e.Message}"); }
#pragma warning restore SYSLIB0006
        t.Interrupt();
        Console.WriteLine($"after Thread.Interrupt on a spinning thread, 300 ms later IsAlive = {(t.Join(300) ? false : true)}");
    }

    // ControlledExecution.Run: the runtime's own "abort this thread" for REPL-like hosts.
    static void Controlled(string[] args)
    {
        foreach (int n in new[] { 65536, 1048576 })
        {
            ApInt a = RandomBits(n), b = RandomBits(n);
            var lat = new List<double>();
            for (int t = 0; t < 5; t++)
            {
                using var cts = new CancellationTokenSource();
                long cancelled = 0, ended = 0;
                string how = "?";
                var th = new Thread(() =>
                {
                    try
                    {
                        ControlledExecution.Run(() => { _sink = a * b; _sink = a * b; _sink = a * b; _sink = a * b; }, cts.Token);
                        how = "finished";
                    }
                    catch (OperationCanceledException) { how = "OCE"; }
                    ended = Stopwatch.GetTimestamp();
                });
                th.Start();
                Thread.Sleep(40);
                cancelled = Stopwatch.GetTimestamp();
                cts.Cancel();
                th.Join();
                lat.Add(Ms(ended - cancelled));
                if (t == 0) Console.WriteLine($"bits {n}: first result {how}");
            }
            lat.Sort();
            Console.WriteLine($"bits {n,-8} controlled abort latency ms: min {lat[0]:F2} median {lat[lat.Count / 2]:F2} max {lat[^1]:F2}");
            // is the library still right afterwards?
            Console.WriteLine($"   after abort: 3*7 = {(ApInt)3 * 7}, 1e5000 E5 = {ApFloat.Parse("1e5000").ToString("E5")}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // The child worker: a reader thread takes requests from stdin, so it hears EOF (its parent
    // has gone) and "cancel" even while the compute thread is busy.
    // ---------------------------------------------------------------------------------------
    static void Worker()
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        var reader = new StreamReader(stdin, Encoding.ASCII);
        var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var work = new System.Collections.Concurrent.BlockingCollection<string>();
        new Thread(() =>
        {
            foreach (string req in work.GetConsumingEnumerable())
            {
                string[] p = req.Split(' ');
                switch (p[0])
                {
                    case "ping": writer.WriteLine("pong"); break;
                    case "add":
                        // the small round trip: a real parse, add and format at 256 bits
                        writer.WriteLine((ApFloat.Parse(p[1]) + ApFloat.Parse(p[2])).ToString("R")); break;
                    case "hexback":     // value text in, the same text out: the transfer of a big value
                        writer.WriteLine(ApInt.Parse(p[1]).ToString("X")); break;
                    case "mul":         // an op the parent will kill
                        int n = int.Parse(p[1]);
                        var a = RandomBits(n, new Random(1)); var b = RandomBits(n, new Random(2));
                        writer.WriteLine("started");
                        writer.WriteLine((a * b).BitLength); break;
                    case "mem":
                        using (var me = Process.GetCurrentProcess())
                            writer.WriteLine($"ws {me.WorkingSet64 / 1048576.0:F1} private {me.PrivateMemorySize64 / 1048576.0:F1}");
                        break;
                }
            }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "compute" }.Start();

        string? line;
        while ((line = reader.ReadLine()) != null) work.Add(line);
        Environment.Exit(0);    // stdin closed: the parent has gone
    }

    static Process Spawn()
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, "worker")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
        };
        psi.StandardOutputEncoding = Encoding.UTF8;
        var p = Process.Start(psi)!;
        p.StandardInput.AutoFlush = true;
        p.StandardInput.NewLine = "\n";
        return p;
    }

    static string Ask(Process p, string req)
    {
        p.StandardInput.WriteLine(req);
        return p.StandardOutput.ReadLine()!;
    }

    static void Child(string[] args)
    {
        // cold spawn to first answer, A few times
        var cold = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            long t = Stopwatch.GetTimestamp();
            var p = Spawn();
            Ask(p, "ping");
            double first = Ms(Stopwatch.GetTimestamp() - t);
            Ask(p, "add 0.1 0.2");          // JIT of the numeric path
            double second = Ms(Stopwatch.GetTimestamp() - t);
            cold.Add(second);
            Console.WriteLine($"cold spawn: pong {first:F1} ms, first real answer {second:F1} ms");
            p.Kill(); p.WaitForExit();
        }

        var w = Spawn();
        Console.WriteLine("worker says: " + Ask(w, "mem"));

        // warm round trip
        for (int i = 0; i < 200; i++) Ask(w, "add 0.1 0.2");     // past tier-0 enough to compare
        var rt = new List<double>();
        for (int i = 0; i < 2000; i++)
        {
            long t = Stopwatch.GetTimestamp();
            Ask(w, "add 0.1 0.2");
            rt.Add(Ms(Stopwatch.GetTimestamp() - t));
        }
        rt.Sort();
        Console.WriteLine($"warm round trip (add at 256 bits) ms: median {rt[rt.Count / 2]:F3}  p99 {rt[(int)(rt.Count * 0.99)]:F3}  max {rt[^1]:F3}");
        var inproc = new List<double>();
        for (int i = 0; i < 2000; i++)
        {
            long t = Stopwatch.GetTimestamp();
            _sink = (ApFloat.Parse("0.1") + ApFloat.Parse("0.2")).ToString("R");
            inproc.Add(Ms(Stopwatch.GetTimestamp() - t));
        }
        inproc.Sort();
        Console.WriteLine($"same in process ms: median {inproc[inproc.Count / 2]:F3}");

        // transfer of a value as hex text (about 3.32 Mbit, i.e. a million decimal digits' worth)
        foreach (int bits in new[] { 3321928, 33219280 })
        {
            string hex = "0x" + new string('a', bits / 4);
            for (int i = 0; i < 3; i++)
            {
                long t = Stopwatch.GetTimestamp();
                string back = Ask(w, "hexback " + hex);
                Console.WriteLine($"transfer {bits} bits as {hex.Length} chars of hex, there and back through Natural: {Ms(Stopwatch.GetTimestamp() - t):F1} ms ({back.Length} chars back)");
            }
        }

        // kill during a huge multiply
        Console.WriteLine("kill during a 2,000,000-bit multiply:");
        for (int i = 0; i < 5; i++)
        {
            w.StandardInput.WriteLine("mul 2000000");
            w.StandardOutput.ReadLine();    // started
            Thread.Sleep(150);
            long t = Stopwatch.GetTimestamp();
            w.Kill();
            w.WaitForExit();
            double killed = Ms(Stopwatch.GetTimestamp() - t);
            long r = Stopwatch.GetTimestamp();
            w = Spawn();
            Ask(w, "add 0.1 0.2");
            Console.WriteLine($"  Kill to exit {killed:F1} ms; respawn to first answer {Ms(Stopwatch.GetTimestamp() - r):F1} ms");
        }
        w.Kill();
    }

    /// <summary>Orphan test: this parent spawns a worker, prints its pid and then kills itself hard.</summary>
    static void Orphan()
    {
        var w = Spawn();
        Ask(w, "ping");
        Console.WriteLine(w.Id);
        Console.Out.Flush();
        Process.GetCurrentProcess().Kill();
    }

    // A heartbeat thread beside an in-process worker doing big allocating arithmetic: the GC stops
    // every managed thread, so this is what a UI thread would feel with the work in its process.
    static void OrphanBusy()
    {
        var w = Spawn();
        Ask(w, "ping");
        w.StandardInput.WriteLine("mul 2000000");
        w.StandardOutput.ReadLine();
        Thread.Sleep(200);
        Console.WriteLine(w.Id);
        Console.Out.Flush();
        Process.GetCurrentProcess().Kill();
    }

    static void Cold()
    {
        var a = new List<double>(); var b = new List<double>(); var c = new List<double>();
        for (int i = 0; i < 7; i++)
        {
            long t = Stopwatch.GetTimestamp();
            var p = Spawn();
            Ask(p, "ping");
            a.Add(Ms(Stopwatch.GetTimestamp() - t));
            Ask(p, "add 0.1 0.2");
            b.Add(Ms(Stopwatch.GetTimestamp() - t));
            long t2 = Stopwatch.GetTimestamp();
            Ask(p, "add 0.5 0.25");
            c.Add(Ms(Stopwatch.GetTimestamp() - t2));
            p.Kill(); p.WaitForExit();
        }
        a.Sort(); b.Sort(); c.Sort();
        Console.WriteLine($"cold: pong {a[3]:F1} ms, first answer {b[3]:F1} ms, second answer {c[3]:F2} ms (medians of 7)");
    }

    static void Big()
    {
        const int bits = 3321929;   // a million decimal digits' worth
        var sw = Stopwatch.StartNew();
        ApFloat third = ApFloat.Divide(1, 3, bits);
        Console.WriteLine($"1/3 at {bits} bits: {sw.Elapsed.TotalMilliseconds:F0} ms");
        sw.Restart();
        string e = third.ToString("E20");
        Console.WriteLine($"E20 of it: {sw.Elapsed.TotalMilliseconds:F1} ms: {e}");
        sw.Restart();
        string a = third.ToString("A");
        Console.WriteLine($"A (hex float) of it: {sw.Elapsed.TotalMilliseconds:F1} ms, {a.Length} chars");
        sw.Restart();
        ApFloat rnd = new(RandomBits(bits), -bits, bits);
        Console.WriteLine($"random {bits}-bit value built: {sw.Elapsed.TotalMilliseconds:F1} ms");
        sw.Restart();
        string e2 = rnd.ToString("E20");
        Console.WriteLine($"E20 of it: {sw.Elapsed.TotalMilliseconds:F1} ms: {e2}");
        sw.Restart();
        var job = third.StartFormat("E999999");
        for (int i = 0; i < 40; i++) if (job.Continue(TimeSpan.FromMilliseconds(50))) break;
        Console.WriteLine($"format job for a million digits: {sw.Elapsed.TotalMilliseconds:F0} ms, Progress {job.Progress:P3}, WorkDone {job.WorkDone:N0}, complete {job.IsComplete}; straight line to the end: {sw.Elapsed.TotalSeconds / Math.Max(job.Progress, 1e-9):F0} s");
        job.Dispose();
    }

    static void FmtJob(string[] args)
    {
        foreach (int digits in args.Skip(1).Select(int.Parse))
        {
            int bits = (int)(digits * 3.3219281) + 2;
            ApFloat third = ApFloat.Divide(1, 3, bits);
            ApFloat whole = new(RandomBits(bits), 0, bits);
            foreach (var (name, value, fmt) in new[] { ("1/3 E", third, $"E{digits - 1}"), ("integer F0", whole, "F0") })
            {
                var job = value.StartFormat(fmt);
                var sw = Stopwatch.StartNew();
                double worst = 0;
                int slices = 0;
                while (true)
                {
                    long s = Stopwatch.GetTimestamp();
                    bool done = job.Continue(TimeSpan.FromMilliseconds(50));
                    worst = Math.Max(worst, Ms(Stopwatch.GetTimestamp() - s));
                    slices++;
                    if (done) break;
                }
                Console.WriteLine($"{digits,8} digits {name,-11} total {sw.Elapsed.TotalMilliseconds,10:F0} ms  slices {slices,5}  worst slice {worst,10:F0} ms  chars {job.Result.Length}");
                job.Dispose();
            }
        }
    }

    static void Gc(string[] args)
    {
        int n = int.Parse(args[1]);
        double seconds = double.Parse(args[2]);
        bool inproc = args[3] == "inproc";
        ApFloat x = new(RandomBits(n), -n, n), y = new(RandomBits(n), -n, n);
        var stop = false;
        int ops = 0;
        Thread? worker = null;
        Process? child = null;
        if (inproc)
        {
            worker = new Thread(() =>
            {
                ApFloat r = x;
                while (!Volatile.Read(ref stop)) { r = ApFloat.Multiply(r, y, n); ops++; }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            worker.Start();
        }
        else if (args[3] == "child")
        {
            child = Spawn();
            child.StandardInput.WriteLine($"mul {n * 8}");
        }
        var gaps = new List<double>();
        long end = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        long prev = Stopwatch.GetTimestamp();
        int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();
        // A spinning heartbeat: Thread.Sleep(1) only wakes every 15.6 ms here, which would hide
        // everything shorter. Any stall of this thread (GC suspension, or the OS) shows as a gap.
        long loops = 0;
        while (true)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= end) break;
            double gap = Ms(now - prev);
            if (gap > 0.5) gaps.Add(gap);
            prev = now;
            if ((++loops & 1023) == 0) _sink = new byte[256];   // a little allocation, as a UI thread does
        }
        Volatile.Write(ref stop, true);
        var pause = GC.GetTotalPauseDuration() - pause0;
        child?.Kill();
        gaps.Sort();
        Func<double, int> over = t => gaps.Count(g => g > t);
        Console.WriteLine($"{args[3],-7} bits {n,-8} {seconds}s ops {ops,5}  stalls >0.5ms {gaps.Count,5}  >4ms {over(4),4}  >16ms {over(16),3}  worst {(gaps.Count > 0 ? gaps[^1] : 0),7:F2} ms  total stalled {gaps.Sum(),8:F1} ms  gen0 {GC.CollectionCount(0) - gc0,4} gen2 {GC.CollectionCount(2) - gc2,3} GC pause {pause.TotalMilliseconds,7:F1} ms");
    }

    static int Main(string[] args)
    {
        switch (args.Length > 0 ? args[0] : "")
        {
            case "curve": Curve(args); break;
            case "stop-between": StopBetween(args); break;
            case "abort": Abort(); break;
            case "controlled": Controlled(args); break;
            case "worker": Worker(); break;
            case "child": Child(args); break;
            case "orphan": Orphan(); break;
            case "orphan-busy": OrphanBusy(); break;
            case "cold": Cold(); break;
            case "big": Big(); break;
            case "fmtjob": FmtJob(args); break;
            case "gc": Gc(args); break;
            default: Console.WriteLine("mode?"); return 2;
        }
        return 0;
    }
}
