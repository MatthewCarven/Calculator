#:project C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\natural-api\src\Natural\Natural.csproj
#:property PublishAot=false
// Loose ends. Run with DOTNET_GCHeapHardLimit=0x40000000.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Natural;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
static void S(string label, Func<object?> f)
{
    var sw = Stopwatch.StartNew();
    try { Console.WriteLine($"{label,-58} = {f()}   [{sw.ElapsedMilliseconds} ms]"); }
    catch (Exception e) { Console.WriteLine($"{label,-58} ! {e.GetType().Name}: {e.Message.Split('\n')[0]}   [{sw.ElapsedMilliseconds} ms]"); }
}

Console.WriteLine("nulls");
S("ApFloat.Parse(null!)", () => ApFloat.Parse((string)null!));
S("ApFloat.Parse(null!, 64)", () => ApFloat.Parse((string)null!, 64));
S("ApFloat.TryParse(null, out _)", () => ApFloat.TryParse((string?)null, out _));
S("ApInt.TryParse(null, out _)", () => ApInt.TryParse((string?)null, out _));
S("ApFloat.StartParse(null!)", () => ApFloat.StartParse(null!));
S("ApFloat.Parse(null!, style, inv)", () => ApFloat.Parse((string)null!, NumberStyles.Float, null));
S("job on bad text", () => ApFloat.StartParse("x"));
S("job on a bad format", () => ((ApFloat)1).StartFormat("Z"));
S("job on too-long F", () => new ApFloat(1, 1L << 40).StartFormat("F0"));
S("CreateChecked from Complex", () => ApFloat.CreateChecked(new Complex(1, 2)));
S("CreateChecked from Complex, ApInt", () => ApInt.CreateChecked(new Complex(1, 2)));

Console.WriteLine("\nbinary text past 2^32 digits: 32 set bits at the top of a 2^32+33-bit value");
{
    var x = new ApFloat(0xFFFFFFFFu, (1L << 32) + 1, 64);
    GC.Collect();
    S("true digits", () => (1L << 32) + 33);
    S("ToString(\"B\")", () => { string s = x.ToString("B"); return $"{s.Length} chars: {s[..Math.Min(40, s.Length)]}"; });
}

Console.WriteLine("\nradix conversion: single digits against chunks of a word");
{
    const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
    static string One(ApInt x, int radix)
    {
        var sb = new List<char>(); ApInt m = ApInt.Abs(x);
        while (!m.IsZero) { var (q, r) = ApInt.DivRem(m, radix); sb.Add(Digits[(int)(long)r]); m = q; }
        sb.Reverse(); return new string(sb.ToArray());
    }
    static string Chunked(ApInt x, int radix)
    {
        int k = 0; long pow = 1; while (pow * radix <= uint.MaxValue) { pow *= radix; k++; }
        var sb = new List<char>(); ApInt m = ApInt.Abs(x);
        while (!m.IsZero)
        {
            var (q, r) = ApInt.DivRem(m, pow);
            long c = (long)r;
            for (int i = 0; i < k && (c != 0 || !q.IsZero); i++) { sb.Add(Digits[(int)(c % radix)]); c /= radix; }
            m = q;
        }
        sb.Reverse(); return new string(sb.ToArray());
    }
    ApInt v = ApInt.Pow(3, 10000);
    string a = "", b = "";
    var sw = Stopwatch.StartNew(); a = One(v, 7); long m1 = sw.ElapsedMilliseconds;
    sw.Restart(); b = Chunked(v, 7); long m2 = sw.ElapsedMilliseconds;
    Console.WriteLine($"  {v.BitLength} bits to radix 7: one digit at a time {m1} ms, chunked {m2} ms, equal {a == b}");
    sw.Restart(); string dec = v.ToString(); long m3 = sw.ElapsedMilliseconds;
    Console.WriteLine($"  the same value in decimal by double dabble: {m3} ms");
}

Console.WriteLine("\nbyte and sbyte into ApFloat (ApInt's are a compile error, CS0457)");
{
    byte by = 200; ushort us = 60000; short sh = -5; sbyte sb = -5;
    ApFloat a = by, b = us, c = sh, d = sb;
    Console.WriteLine($"  {a:R} p={a.Precision}, {b:R} p={b.Precision}, {c:R} p={c.Precision}, {d:R} p={d.Precision}");
}
