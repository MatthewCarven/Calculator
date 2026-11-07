extern alias plain;
extern alias poll;

using System.Diagnostics;
using PlainInt = plain::Natural.ApInt;
using PollInt = poll::Natural.ApInt;
using StopFlag = poll::Natural.StopFlag;

// Option (c): the same arithmetic, plain against a copy with a step point per row. Both arms live
// in one process and take turns round by round, so the machine's two speeds fall on both.
static class P
{
    static object? _sink;
    static double Ms(long t) => t * 1000.0 / Stopwatch.Frequency;

    static string Hex(int bits, Random r)
    {
        var hex = new char[bits / 4 + 3];
        hex[0] = '0'; hex[1] = 'x'; hex[2] = 'f';
        for (int i = 3; i < hex.Length; i++) hex[i] = "0123456789abcdef"[r.Next(16)];
        return new string(hex);
    }

    static double Run(Action f, int reps)
    {
        long t = Stopwatch.GetTimestamp();
        for (int i = 0; i < reps; i++) f();
        return Ms(Stopwatch.GetTimestamp() - t) / reps;
    }

    static void Overhead(int n, int reps, int rounds)
    {
        var r = new Random(n);
        string ax = Hex(n, r), bx = Hex(n, r), wx = Hex(2 * n, r);
        PlainInt pa = PlainInt.Parse(ax), pb = PlainInt.Parse(bx), pw = PlainInt.Parse(wx);
        PollInt qa = PollInt.Parse(ax), qb = PollInt.Parse(bx), qw = PollInt.Parse(wx);

        var arms = new (string name, Action a)[]
        {
            ("mul plain", () => _sink = pa * pb),
            ("mul poll", () => _sink = qa * qb),
            ("mul job", () => { var j = PollInt.StartMultiply(qa, qb); _sink = j.RunToEnd(); j.Dispose(); }),
            ("div plain", () => _sink = pw / pb),
            ("div poll", () => _sink = qw / qb),
            ("sqrt plain", () => _sink = PlainInt.Sqrt(pw)),
            ("sqrt poll", () => _sink = PollInt.Sqrt(qw)),
        };
        // warm every arm first (tiering)
        foreach (var (_, a) in arms) for (int i = 0; i < Math.Max(reps, 40); i++) { if (n > 20000 && i >= 2) break; a(); }

        var times = arms.ToDictionary(a => a.name, _ => new List<double>());
        for (int round = 0; round < rounds; round++)
        {
            // the order rotates, so no arm always follows the same neighbour
            for (int i = 0; i < arms.Length; i++)
            {
                var (name, a) = arms[(i + round) % arms.Length];
                times[name].Add(Run(a, reps));
            }
        }
        double Med(string k) { var l = times[k].OrderBy(x => x).ToList(); return l[l.Count / 2]; }
        void Line(string plainArm, string other)
        {
            var ratios = times[other].Zip(times[plainArm], (o, p) => o / p).OrderBy(x => x).ToList();
            Console.WriteLine($"{n,8} bits  {other,-10} {Med(other),11:F4} ms vs {Med(plainArm),11:F4} ms   ratio median {ratios[ratios.Count / 2]:F4}  (round ratios {ratios[0]:F3}..{ratios[^1]:F3})");
        }
        Line("mul plain", "mul poll");
        Line("mul plain", "mul job");
        Line("div plain", "div poll");
        Line("sqrt plain", "sqrt poll");
    }

    static void Latency(int n)
    {
        var r = new Random(n + 1);
        PollInt a = PollInt.Parse(Hex(n, r)), b = PollInt.Parse(Hex(n, r)), w = PollInt.Parse(Hex(2 * n, r));
        foreach (var (name, op) in new (string, Action)[]
        {
            ("mul", () => _sink = a * b), ("div", () => _sink = w / b), ("sqrt", () => _sink = PollInt.Sqrt(w)),
        })
        {
            var lat = new List<double>();
            for (int t = 0; t < 5; t++)
            {
                StopFlag.Requested = false;
                long ended = 0;
                var th = new Thread(() =>
                {
                    try { op(); }
                    catch (OperationCanceledException) { }
                    ended = Stopwatch.GetTimestamp();
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                th.Start();
                Thread.Sleep(60 + 10 * t);
                long asked = Stopwatch.GetTimestamp();
                StopFlag.Requested = true;
                th.Join();
                lat.Add(Ms(ended - asked));
            }
            StopFlag.Requested = false;
            lat.Sort();
            Console.WriteLine($"{n,8} bits {name,-5} stop latency ms: min {lat[0]:F3} median {lat[2]:F3} max {lat[^1]:F3}");
        }
    }

    static void JobStop(int n)
    {
        var r = new Random(n + 2);
        PollInt a = PollInt.Parse(Hex(n, r)), b = PollInt.Parse(Hex(n, r));
        var job = PollInt.StartMultiply(a, b);
        long t = Stopwatch.GetTimestamp();
        double worstSlice = 0;
        double lastProgress = 0;
        for (int i = 0; i < 20; i++)
        {
            long s = Stopwatch.GetTimestamp();
            bool done = job.Continue(TimeSpan.FromMilliseconds(50));
            worstSlice = Math.Max(worstSlice, Ms(Stopwatch.GetTimestamp() - s));
            lastProgress = job.Progress;
            if (done) break;
        }
        Console.WriteLine($"{n,8} bits job: 20 slices of 50 ms, worst slice {worstSlice:F1} ms, progress {lastProgress:P2}, WorkDone {job.WorkDone:N0} after {Ms(Stopwatch.GetTimestamp() - t):F0} ms (a stop would be: not calling Continue)");
        job.Dispose();
    }

    static void Main(string[] args)
    {
        switch (args[0])
        {
            case "overhead":
                Overhead(256, 4000, 9);
                Overhead(4096, 60, 9);
                Overhead(16384, 6, 7);
                Overhead(65536, 1, 5);
                break;
            case "latency":
                Latency(65536); Latency(262144); Latency(1048576);
                JobStop(1048576);
                break;
        }
    }
}
