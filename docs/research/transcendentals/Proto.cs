// A throwaway prototype of the design: a mid-radius Ball on ApFloat's public API, binary splitting
// for the constants, and competing routes for exp, ln, sin/cos and atan. Shared by the probe apps.
// Everything is rounded through ApFloat's own directed modes, so the radii are rigorous by construction,
// and the one hand-proved thing in each function is its series' tail.
using Natural;
using static Natural.RoundingMode;

internal readonly struct Ball
{
    public const int RB = 32;
    public readonly ApFloat M, R;
    public Ball(ApFloat m, ApFloat r) { M = m; R = r; }

    public static readonly ApFloat Z = ApFloat.Zero;
    public static bool NoRadius;
    public static long Top(ApFloat x) => x.Exponent + (long)x.Significand.BitLength - 1;
    public static ApFloat Pow2(long e) => new ApFloat(ApInt.One, e, RB);
    public static ApFloat Scaled(ApFloat x, long k) =>
        x.IsZero ? x : new ApFloat(x.IsNegative ? -x.Significand : x.Significand, x.Exponent + k, x.Precision);
    static ApFloat HalfUlp(ApFloat m, int w) => m.IsZero || NoRadius ? Z : Pow2(Top(m) - w);
    public static ApFloat UpAdd(ApFloat a, ApFloat b) => a.IsZero ? b : b.IsZero ? a : ApFloat.Add(a, b, RB, TowardPositive);
    public static ApFloat UpMul(ApFloat a, ApFloat b) => ApFloat.Multiply(a, b, RB, TowardPositive);
    public static ApFloat UpDiv(ApFloat a, ApFloat b) => ApFloat.Divide(a, b, RB, TowardPositive);
    public static ApFloat UpMag(ApFloat x) => ApFloat.Abs(x).WithPrecision(RB, TowardPositive);
    public static ApFloat DownMag(ApFloat x) => ApFloat.Abs(x).WithPrecision(RB, TowardNegative);

    public static Ball Of(ApFloat x, int w)
    {
        ApFloat m = x.WithPrecision(w);
        return new(m, m == x ? Z : Pow2(Top(x) - w + 1));
    }
    public static Ball Exact(ApFloat x) => new(x, Z);

    public static Ball operator -(Ball a) => new(-a.M, a.R);
    public static Ball Scale(Ball a, long k) => new(Scaled(a.M, k), Scaled(a.R, k));

    public static Ball Add(Ball a, Ball b, int w)
    {
        ApFloat m = ApFloat.Add(a.M, b.M, w);
        return new(m, UpAdd(HalfUlp(m, w), UpAdd(a.R, b.R)));
    }
    public static Ball Sub(Ball a, Ball b, int w) => Add(a, -b, w);

    public static Ball Mul(Ball a, Ball b, int w)
    {
        ApFloat m = ApFloat.Multiply(a.M, b.M, w);
        ApFloat r = HalfUlp(m, w);
        if (!b.R.IsZero) r = UpAdd(r, UpMul(UpMag(a.M), b.R));
        if (!a.R.IsZero)
        {
            r = UpAdd(r, UpMul(UpMag(b.M), a.R));
            if (!b.R.IsZero) r = UpAdd(r, UpMul(a.R, b.R));
        }
        return new(m, r);
    }

    public static Ball MulInt(Ball a, ApFloat k, int w)
    {
        ApFloat m = ApFloat.Multiply(a.M, k, w);
        return new(m, UpAdd(HalfUlp(m, w), UpMul(a.R, UpMag(k))));
    }

    public static Ball DivSmall(Ball a, long k, int w)
    {
        ApFloat d = k;
        ApFloat m = ApFloat.Divide(a.M, d, w);
        return new(m, UpAdd(HalfUlp(m, w), a.R.IsZero ? Z : UpDiv(a.R, d)));
    }

    public static Ball Div(Ball a, Ball b, int w)
    {
        ApFloat m = ApFloat.Divide(a.M, b.M, w);
        // |x/y - a/b| <= (ra + |a/b| rb) / (|b| - rb)
        ApFloat num = UpAdd(a.R, UpMul(UpAdd(UpMag(m), Pow2(Top(m) - RB + 2)), b.R));
        ApFloat den = ApFloat.Subtract(DownMag(b.M), b.R, RB, TowardNegative);
        return new(m, UpAdd(HalfUlp(m, w), num.IsZero ? Z : UpDiv(num, den)));
    }

    public static Ball Sqrt(Ball a, int w)
    {
        ApFloat m = ApFloat.Sqrt(a.M, w);
        if (a.R.IsZero) return new(m, HalfUlp(m, w));
        ApFloat low = ApFloat.Subtract(DownMag(m), Pow2(Top(m) - RB + 3), RB, TowardNegative);  // <= sqrt(a.M)
        return new(m, UpAdd(HalfUlp(m, w), UpDiv(a.R, low)));
    }

    public Ball Widen(ApFloat e) => NoRadius ? this : new(M, UpAdd(R, e));
    public Ball Trim(int w) { if (Top(M) - M.Exponent < w) return this; ApFloat m = M.WithPrecision(w); return new(m, UpAdd(R, HalfUlp(m, w))); }
    public bool ContainsZero => ApFloat.Abs(M) <= R;
    /// <summary>Bits of relative accuracy this ball holds.</summary>
    public long Bits => M.IsZero ? 0 : R.IsZero ? long.MaxValue : Top(M) - Top(R);
    public override string ToString() => $"{M.ToString("E20")} +- {R.ToString("E3")}";
}

internal static class Constants
{
    // Binary splitting over ApInt, bottom-free recursive form: exact rationals, one division at the end.
    public static (ApInt P, ApInt Q, ApInt T) Chudnovsky(long a, long b)
    {
        if (b - a == 1)
        {
            if (a == 0) return (ApInt.One, ApInt.One, (ApInt)13591409L);
            ApInt p = (ApInt)(6 * a - 5) * (ApInt)(2 * a - 1) * (ApInt)(6 * a - 1);
            ApInt q = (ApInt)a * (ApInt)a * (ApInt)a * (ApInt)10939058860032000L;
            ApInt t = p * ((ApInt)13591409L + (ApInt)545140134L * (ApInt)a);
            return (p, q, (a & 1) != 0 ? -t : t);
        }
        long m = (a + b) / 2;
        var (pl, ql, tl) = Chudnovsky(a, m);
        var (pr, qr, tr) = Chudnovsky(m, b);
        return (pl * pr, ql * qr, tl * qr + pl * tr);
    }

    public static Ball PiChudnovsky(int w)
    {
        long n = w / 47 + 2;
        var (_, q, t) = Chudnovsky(0, n);
        int wi = w + 16;
        ApFloat qf = new(q, 0, wi), tf = new(t, 0, wi);
        Ball rootBall = Ball.Sqrt(Ball.Exact((ApFloat)10005L), wi);
        Ball r = Ball.MulInt(Ball.Of(new ApFloat(q, 0, (int)Math.Min(ApFloat.MaxPrecision, Math.Max(wi, q.BitLength))), wi), (ApFloat)426880L, wi);
        r = Ball.Mul(r, rootBall, wi);
        r = Ball.Div(r, Ball.Of(new ApFloat(t, 0, (int)Math.Min(ApFloat.MaxPrecision, Math.Max(wi, t.BitLength))), wi), wi);
        return r.Widen(Ball.Pow2(-w));   // tail: 2^-47 per term, generously
    }

    // atan(1/n) or atanh(1/n) as a sum of Pi 1/(n^2) with 1/(2k+1): T/(B Q) form.
    public static (ApInt Q, ApInt B, ApInt T) ArcSplit(long a, long b, ApInt n2, int sign)
    {
        if (b - a == 1)
        {
            ApInt bk = (ApInt)(2 * a + 1);
            ApInt qk = a == 0 ? ApInt.One : n2;
            ApInt tk = (sign < 0 && (a & 1) != 0) ? -ApInt.One : ApInt.One;
            return (qk, bk, tk);
        }
        long m = (a + b) / 2;
        var (ql, bl, tl) = ArcSplit(a, m, n2, sign);
        var (qr, br, tr) = ArcSplit(m, b, n2, sign);
        return (ql * qr, bl * br, br * qr * tl + bl * tr);
    }

    /// <summary>arc(1/n) as a ball: sign -1 for atan, +1 for atanh.</summary>
    public static Ball Arc(long n, int sign, int w)
    {
        ApInt n2 = (ApInt)n * (ApInt)n;
        int bitsPerTerm = 2 * (int)(63 - System.Numerics.BitOperations.LeadingZeroCount((ulong)n));
        long terms = w / bitsPerTerm + 2;
        var (q, b, t) = ArcSplit(0, terms, n2, sign);
        int wi = w + 16;
        // value = T / (B Q n)
        ApInt den = b * q * (ApInt)n;
        Ball r = Ball.Div(Ball.Of(FromInt(t), wi), Ball.Of(FromInt(den), wi), wi);
        return r.Widen(Ball.Pow2(-w));
    }

    public static ApFloat FromInt(ApInt x) => new(x, 0, (int)Math.Max(16, Math.Min(ApFloat.MaxPrecision, x.BitLength)));

    public static Ball PiMachin(int w)
    {
        int wi = w + 8;
        Ball a = Arc(5, -1, wi + 4), b = Arc(239, -1, wi + 4);
        return Ball.Sub(Ball.Scale(a, 4), Ball.Scale(b, 2), wi);
    }

    // Plain fixed-point Machin with short divisions: the "simple series" arm, no splitting.
    public static ApInt PiFixed(int w)
    {
        int g = w + 32;
        ApInt ArcFixed(int n)
        {
            ApInt n2 = (ApInt)(n * n), term = (ApInt.One << g) / (ApInt)n, sum = term;
            for (long k = 1; !term.IsZero; k++)
            {
                term = term / n2;
                ApInt t = term / (ApInt)(2 * k + 1);
                sum = (k & 1) != 0 ? sum - t : sum + t;
            }
            return sum;
        }
        return ((ArcFixed(5) << 4) - (ArcFixed(239) << 2)) >> 32;
    }

    // Gauss-Legendre (Brent-Salamin), the AGM arm.
    public static Ball PiAgm(int w)
    {
        int wi = w + 40;
        Ball a = Ball.Exact((ApFloat)1L), b = Ball.Sqrt(Ball.Exact(new ApFloat(ApInt.One, -1, wi)), wi);
        Ball t = Ball.Exact(new ApFloat(ApInt.One, -2, wi));
        int k = 0;
        while (true)
        {
            Ball an = Ball.Scale(Ball.Add(a, b, wi), -1);
            Ball bn = Ball.Sqrt(Ball.Mul(a, b, wi), wi);
            Ball d = Ball.Sub(a, an, wi);
            t = Ball.Sub(t, Ball.Scale(Ball.Mul(d, d, wi), k), wi);
            a = an; b = bn; k++;
            Ball gap = Ball.Sub(a, b, wi);
            if (gap.M.IsZero || Ball.Top(gap.M) < -wi / 2 - 4) break;
        }
        Ball s = Ball.Add(a, b, wi);
        return Ball.Div(Ball.Mul(s, s, wi), Ball.Scale(t, 2), wi).Widen(Ball.Pow2(-w));
    }

    public static (ApInt Q, ApInt T) ESplit(long a, long b)
    {
        if (b - a == 1) return ((ApInt)b, ApInt.One);
        long m = (a + b) / 2;
        var (ql, tl) = ESplit(a, m);
        var (qr, tr) = ESplit(m, b);
        return (ql * qr, tl * qr + tr);
    }

    public static Ball E(int w)
    {
        long n = 2;
        for (long bits = 0; bits < w + 2; n++) bits += 63 - System.Numerics.BitOperations.LeadingZeroCount((ulong)n);
        var (q, t) = ESplit(0, n);
        int wi = w + 16;
        Ball r = Ball.Div(Ball.Of(FromInt(t + q), wi), Ball.Of(FromInt(q), wi), wi);
        return r.Widen(Ball.Pow2(-w));
    }

    public static Ball Ln2Machin(int w)
    {
        int wi = w + 12;
        Ball r = Ball.Scale(Ball.MulInt(Arc(26, 1, wi), (ApFloat)9L, wi), 1);
        r = Ball.Sub(r, Ball.Scale(Arc(4801, 1, wi), 1), wi);
        r = Ball.Add(r, Ball.Scale(Arc(8749, 1, wi), 3), wi);
        return r;
    }

    public static (ApInt P, ApInt Q, ApInt T) Ln2Split(long a, long b)
    {
        // MPFR's: ln2 = 3/4 sum (-1)^n n!^2 / (2^n (2n+1)!), ratio -n / (4 (2n+1)).
        if (b - a == 1)
        {
            if (a == 0) return (ApInt.One, ApInt.One, ApInt.One);
            ApInt p = (ApInt)(-a), q = (ApInt)(4 * (2 * a + 1));
            return (p, q, p);
        }
        long m = (a + b) / 2;
        var (pl, ql, tl) = Ln2Split(a, m);
        var (pr, qr, tr) = Ln2Split(m, b);
        return (pl * pr, ql * qr, tl * qr + pl * tr);
    }

    public static Ball Ln2Mpfr(int w)
    {
        long n = w / 3 + 2;
        var (_, q, t) = Ln2Split(0, n);
        int wi = w + 16;
        Ball r = Ball.Div(Ball.Of(FromInt(t * (ApInt)3), wi), Ball.Of(FromInt(q << 2), wi), wi);
        return r.Widen(Ball.Pow2(-w));
    }

    static readonly Dictionary<string, Ball> s_cache = new();
    public static Ball Cached(string name, int w, Func<int, Ball> make)
    {
        lock (s_cache)
        {
            if (s_cache.TryGetValue(name, out Ball b) && b.Bits >= w + 4) return b;
            b = make(w + w / 4 + 64);
            s_cache[name] = b;
            return b;
        }
    }
    public static void Clear() { lock (s_cache) s_cache.Clear(); }
    public static Ball Pi(int w) => Cached("pi", w, PiChudnovsky);
    public static Ball Ln2(int w) => Cached("ln2", w, Ln2Machin);
}

internal static class Fn
{
    static int Log2(long n) => 63 - System.Numerics.BitOperations.LeadingZeroCount((ulong)n);
    static readonly ApFloat One = (ApFloat)1L;

    public static int TaylorM = 0;   // 0: choose sqrt(N); 1: plain Horner
    public static int HalvingsOverride = -1;
    public static bool Adaptive;
    public static int MaxRounds = 13;

    static int Baby(int n) => TaylorM == 1 ? 2 : Math.Max(2, (int)Math.Sqrt(n));

    /// <summary>expm1 of a small t by rectangular splitting: A_1 in the note. Tail 2 t^(N+1)/(N+1)!.</summary>
    static Ball Expm1Series(Ball t, int w)
    {
        long topT = Ball.Top(t.M) + 1;              // |t| < 2^topT, topT < 0
        int n = 1;
        long bits = 0;
        while (bits < w + 2) { n++; bits += -topT + Log2(n); }
        int m = Baby(n);
        var p = new Ball[m];
        p[0] = Ball.Exact(One);
        p[1] = t;
        for (int i = 2; i < m; i++) p[i] = Ball.Mul(p[i - 1], t, w);
        Ball tm = Ball.Mul(p[m - 1], t, w);
        Ball b = p[n % m];
        long lgFact = 0;
        var weight = new long[n + 2];               // bits the step at index k may drop
        for (int k = 1; k <= n; k++) { lgFact += Log2(k); weight[k] = (k / m) * m * -topT + lgFact - 2 * Log2(k + 1); }
        for (int k = n - 1; k >= 1; k--)
        {
            int wk = Adaptive ? (int)Math.Max(64, w - weight[k]) : w;
            b = Ball.DivSmall(b, k + 1, wk);
            if ((k + 1) % m == 0) b = Adaptive ? Ball.Mul(b.Trim(wk), tm.Trim(wk), wk) : Ball.Mul(b, tm, wk);
            b = Ball.Add(b, Adaptive ? p[k % m].Trim(wk) : p[k % m], wk);
        }
        return b.Widen(Ball.Pow2(-bits + topT + 1));
    }

    public static (long K, Ball R) ReduceExp(ApFloat x, int w)
    {
        long top = Ball.Top(x);
        int qw = (int)Math.Max(32, top + 8);
        Ball ln2 = Constants.Ln2(qw + 8);
        long k = (long)ApFloat.Round(ApFloat.Divide(x, ln2.M, qw));
        if (k == 0) return (0, Ball.Of(x, w));
        int wr = (int)(w + Math.Max(0, top) + 4);
        Ball l = Constants.Ln2(wr);
        Ball lr = new(l.M.WithPrecision(wr + 8), Ball.UpAdd(l.R, Ball.Pow2(-wr - 7)));
        Ball prod = Ball.MulInt(lr, (ApFloat)k, wr);
        return (k, Ball.Sub(Ball.Of(x, wr), prod, wr));
    }

    public static Ball Exp(ApFloat x, int w)
    {
        if (x.IsZero) return Ball.Exact(One);
        int s = HalvingsOverride >= 0 ? HalvingsOverride : Math.Max(2, (int)Math.Round(Math.Cbrt(w) * 0.9));
        int wi = w + 2 * Log2(w) + 8;
        var (k, r) = ReduceExp(x, wi);
        if (r.M.IsZero) return Ball.Scale(Ball.Exact(One), k).Widen(Ball.Pow2(k - wi));
        // A tiny r needs fewer halvings: halve only down to 2^-s.
        long topR = Ball.Top(r.M) + 1;
        long halve = Math.Max(0, s + topR);
        Ball t = Ball.Scale(r, -halve);
        Ball e = Expm1Series(t, wi);
        for (long i = 0; i < halve; i++) e = Ball.Add(Ball.Scale(e, 1), Ball.Mul(e, e, wi), wi);
        return Ball.Scale(Ball.Add(Ball.Exact(One), e, wi), k);
    }

    /// <summary>Plain Horner on exp with plain squaring, for comparison.</summary>
    /// cos t - 1 by rectangular splitting: Sum (-u)^i / (2i)!, i >= 1.
    static Ball CosM1Series(Ball t, int w)
    {
        Ball u = Ball.Mul(t, t, w);
        Ball v = -u;
        long topU = Ball.Top(u.M) + 1;
        int n = 1;
        long bits = 0;
        while (bits < w + 2) { n++; bits += -topU + Log2(2 * n - 1) + Log2(2 * n); }
        int m = Baby(n);
        var p = new Ball[m];
        p[0] = Ball.Exact(One);
        p[1] = v;
        for (int i = 2; i < m; i++) p[i] = Ball.Mul(p[i - 1], v, w);
        Ball vm = Ball.Mul(p[m - 1], v, w);
        Ball b = p[n % m];
        for (int k = n - 1; k >= 1; k--)
        {
            b = Ball.DivSmall(b, (2L * k + 1) * (2L * k + 2), w);
            if ((k + 1) % m == 0) b = Ball.Mul(b, vm, w);
            b = Ball.Add(b, p[k % m], w);
        }
        return Ball.Scale(b, -1).Widen(Ball.Pow2(-bits + topU + 1));
    }

    public static (long Quadrant, Ball R) ReduceTrig(ApFloat x, int w)
    {
        long top = Ball.Top(x);
        if (top < -1) return (0, Ball.Of(x, w));
        int qw = (int)Math.Max(48, top + 16);
        Ball pi = Constants.Pi(qw + 8);
        ApFloat q = ApFloat.Round(ApFloat.Divide(x, Ball.Scaled(pi.M, -1), qw));
        if (q.IsZero) return (0, Ball.Of(x, w));
        int wr = (int)(w + Math.Max(0, top) + 8);
        Ball p = Constants.Pi(wr);
        Ball pr = new(p.M.WithPrecision(wr + 8), Ball.UpAdd(p.R, Ball.Pow2(-wr - 5)));
        Ball prod = Ball.Scale(Ball.MulInt(pr, q, wr), -1);
        long quadrant = (long)(q % (ApFloat)4L);
        if (quadrant < 0) quadrant += 4;
        return (quadrant, Ball.Sub(Ball.Of(x, wr), prod, wr));
    }

    public static (Ball Sin, Ball Cos) SinCosReduced(Ball r, int wi)
    {
        int s = HalvingsOverride >= 0 ? HalvingsOverride : Math.Max(2, (int)Math.Round(Math.Cbrt(wi) * 0.9));
        long topR = Ball.Top(r.M) + 1;
        long halve = Math.Max(0, s + topR);
        Ball t = Ball.Scale(r, -halve);
        Ball c = CosM1Series(t, wi);
        for (long i = 0; i < halve; i++)
            c = Ball.Scale(Ball.Mul(c, Ball.Add(c, Ball.Exact((ApFloat)2L), wi), wi), 1);
        Ball cos = Ball.Add(Ball.Exact(One), c, wi);
        Ball s2 = -Ball.Mul(c, Ball.Add(c, Ball.Exact((ApFloat)2L), wi), wi);
        Ball sin = Ball.Sqrt(s2, wi);
        if (r.M.IsNegative) sin = -sin;
        return (sin, cos);
    }

    public static (Ball Sin, Ball Cos) SinCos(ApFloat x, int w)
    {
        int wi = w + 2 * Log2(w) + 8;
        var (q, r) = ReduceTrig(x, wi);
        var (s, c) = SinCosReduced(r, wi);
        return q switch
        {
            0 => (s, c),
            1 => (c, -s),
            2 => (-s, -c),
            _ => (-c, s),
        };
    }

    public static Ball Sin(ApFloat x, int w) => SinCos(x, w).Sin;
    public static Ball Cos(ApFloat x, int w) => SinCos(x, w).Cos;

    // ln by the AGM. The formula's relative error is taken as 2^(8 + log2 log s) / s^2: see the note.
    public static Ball LnAgm(ApFloat x, int w)
    {
        int wi = w + 2 * Log2(w) + 16;
        long top = Ball.Top(x);
        long m = wi / 2 + 8 - top;                 // s = x 2^m >= 2^(wi/2 + 8)
        Ball s = Ball.Of(Ball.Scaled(x, m), wi);
        Ball a = Ball.Exact(One), b = Ball.Div(Ball.Exact((ApFloat)4L), s, wi);
        while (true)
        {
            Ball an = Ball.Scale(Ball.Add(a, b, wi), -1);
            Ball bn = Ball.Sqrt(Ball.Mul(a, b, wi), wi);
            a = an; b = bn;
            Ball gap = Ball.Sub(a, b, wi);
            if (gap.M.IsZero || Ball.Top(gap.M) < Ball.Top(a.M) - wi / 2 - 2)
            {
                // One more arithmetic mean: quadratic convergence, so the error is about gap^2.
                Ball g = Ball.Scale(Ball.Add(a, b, wi), -1);
                g = g.Widen(ApFloat.Multiply(gap.M.IsZero ? Ball.Pow2(-wi) : ApFloat.Multiply(gap.M, gap.M, Ball.RB, TowardPositive), One, Ball.RB, TowardPositive));
                Ball pi = Constants.Pi(wi + 8);
                Ball l = Ball.Scale(Ball.Div(new Ball(pi.M.WithPrecision(wi + 8), pi.R).Widen(Ball.Pow2(-wi - 6)), g, wi), -1);
                l = l.Widen(Ball.Pow2(Ball.Top(l.M) - wi + 2));     // the formula's error, generously
                if (m == 0) return l;
                Ball ln2 = Constants.Ln2((int)(wi + 8 + Log2(Math.Abs(m) + 1)));
                Ball lm = Ball.MulInt(new Ball(ln2.M.WithPrecision(wi + 40), ln2.R).Widen(Ball.Pow2(-wi - 38)), (ApFloat)m, wi + 16);
                return Ball.Sub(l, lm, wi + 16);
            }
        }
    }

    // ln by Newton on exp, cubic: y0 good to w/3 bits, delta = x exp(-y0) - 1, ln(1+delta) by three terms.
    public static Ball LnNewton(ApFloat x, int w)
    {
        if (w <= 96) return LnSeries(x, w, 0);
        int wi = w + 2 * Log2(w) + 8;
        int w0 = wi / 3 + 16;
        Ball y0 = LnNewton(x, w0);
        ApFloat y = y0.M;
        Ball e = Exp(-y, wi);
        Ball d = Ball.Sub(Ball.Mul(Ball.Of(x, wi), e, wi), Ball.Exact(One), wi);
        Ball d2 = Ball.Mul(d, d, wi);
        Ball d3 = Ball.Mul(d2, d, wi);
        Ball sum = Ball.Add(Ball.Sub(d, Ball.Scale(d2, -1), wi), Ball.DivSmall(d3, 3, wi), wi);
        long td = Ball.Top(d.M) + 2;
        return Ball.Add(Ball.Exact(y), sum.Widen(Ball.Pow2(4 * td)), wi);
    }

    // ln by atanh after r square roots: ln x = E ln 2 + 2^(r+1) atanh z.
    public static Ball LnSeries(ApFloat x, int w, int roots)
    {
        int wi = w + 2 * Log2(w + 8) + 12;
        long e = Ball.Top(x) + 1;                  // m in [1/2, 1)
        Ball f = Ball.Of(Ball.Scaled(x, -e), wi);
        if (f.M < (ApFloat)0.70710678118654752) { f = Ball.Scale(f, 1); e--; }  // m in [0.707, 1.414)
        for (int i = 0; i < roots; i++) f = Ball.Sqrt(f, wi);
        Ball z = Ball.Div(Ball.Sub(f, Ball.Exact(One), wi), Ball.Add(f, Ball.Exact(One), wi), wi);
        Ball u = Ball.Mul(z, z, wi);
        long topU = u.M.IsZero ? -wi : Ball.Top(u.M) + 1;
        int n = (int)(wi / -topU + 2);
        int m = Baby(n);
        var p = new Ball[m];
        p[0] = Ball.Exact(One);
        p[1] = u;
        for (int i = 2; i < m; i++) p[i] = Ball.Mul(p[i - 1], u, wi);
        Ball um = Ball.Mul(p[m - 1], u, wi);
        Ball sum = Ball.Exact(Ball.Z);
        for (int j = (n - 1) / m; j >= 0; j--)
        {
            Ball block = Ball.Exact(Ball.Z);
            for (int i = m - 1; i >= 0; i--)
            {
                int k = j * m + i;
                if (k >= n) continue;
                block = Ball.Add(block, Ball.DivSmall(p[i], 2L * k + 1, wi), wi);
            }
            sum = Ball.Add(Ball.Mul(sum, um, wi), block, wi);
        }
        Ball ath = Ball.Mul(z, sum, wi).Widen(Ball.Pow2((n + 1) * topU + 1));
        Ball l = Ball.Scale(ath, roots + 1);
        if (e == 0) return l;
        Ball ln2 = Constants.Ln2(wi + 8 + Log2(Math.Abs(e) + 1));
        Ball em = Ball.MulInt(new Ball(ln2.M.WithPrecision(wi + 40), ln2.R).Widen(Ball.Pow2(-wi - 38)), (ApFloat)e, wi + 8);
        return Ball.Add(em, l, wi + 8);
    }

    // atan by h halvings, then the alternating series; |x| > 1 goes through pi/2 - atan(1/x).
    public static Ball AtanSeries(ApFloat x, int w, int h)
    {
        int wi = w + 2 * Log2(w + 8) + 12 + h;
        bool invert = ApFloat.Abs(x) > (ApFloat)1L;
        Ball t = invert ? Ball.Div(Ball.Exact((ApFloat)1L), Ball.Of(x, wi), wi) : Ball.Of(x, wi);
        for (int i = 0; i < h; i++)
        {
            Ball root = Ball.Sqrt(Ball.Add(Ball.Exact(One), Ball.Mul(t, t, wi), wi), wi);
            t = Ball.Div(t, Ball.Add(Ball.Exact(One), root, wi), wi);
        }
        Ball u = Ball.Mul(t, t, wi);
        Ball v = -u;
        long topU = Ball.Top(u.M) + 1;
        int n = (int)(wi / -topU + 2);
        int m = Baby(n);
        var p = new Ball[m];
        p[0] = Ball.Exact(One);
        p[1] = v;
        for (int i = 2; i < m; i++) p[i] = Ball.Mul(p[i - 1], v, wi);
        Ball vm = Ball.Mul(p[m - 1], v, wi);
        Ball sum = Ball.Exact(Ball.Z);
        for (int j = (n - 1) / m; j >= 0; j--)
        {
            Ball block = Ball.Exact(Ball.Z);
            for (int i = m - 1; i >= 0; i--)
            {
                int k = j * m + i;
                if (k >= n) continue;
                block = Ball.Add(block, Ball.DivSmall(p[i], 2L * k + 1, wi), wi);
            }
            sum = Ball.Add(Ball.Mul(sum, vm, wi), block, wi);
        }
        Ball a = Ball.Scale(Ball.Mul(t, sum, wi).Widen(Ball.Pow2((n + 1) * topU + Ball.Top(t.M) + 2)), h);
        if (!invert) return a;
        Ball pi = Constants.Pi(wi + 8);
        Ball half = Ball.Scale(new Ball(pi.M.WithPrecision(wi + 8), pi.R).Widen(Ball.Pow2(-wi - 6)), -1);
        return x.IsNegative ? Ball.Sub(-half, a, wi) : Ball.Sub(half, a, wi);
    }

    // atan by Newton on sin and cos: y = y0 + atan(d), d = (x c - s)/(c + x s).
    public static Ball AtanNewton(ApFloat x, int w)
    {
        if (w <= 96) return AtanSeries(x, w, 3);
        int wi = w + 2 * Log2(w) + 8;
        int w0 = wi / 3 + 16;
        Ball y0 = AtanNewton(x, w0);
        ApFloat y = y0.M;
        var (s, c) = SinCos(y, wi);
        Ball xb = Ball.Of(x, wi);
        Ball num = Ball.Sub(Ball.Mul(xb, c, wi), s, wi);
        Ball den = Ball.Add(c, Ball.Mul(xb, s, wi), wi);
        Ball d = Ball.Div(num, den, wi);
        Ball d3 = Ball.Mul(Ball.Mul(d, d, wi), d, wi);
        Ball delta = Ball.Sub(d, Ball.DivSmall(d3, 3, wi), wi);
        long td = Ball.Top(d.M) + 2;
        return Ball.Add(Ball.Exact(y), delta.Widen(Ball.Pow2(5 * td)), wi);
    }

    public static ApFloat Ziv(Func<ApFloat, int, Ball> f, ApFloat x, int p, RoundingMode mode, out int rounds, int g0 = 32)
    {
        rounds = 0;
        for (long g = g0; ; g *= 2)
        {
            rounds++;
            int w = (int)(p + g);
            Ball b = f(x, w);
            int e = w + 8;
            ApFloat lo = ApFloat.Subtract(b.M, b.R, e, TowardNegative);
            ApFloat hi = ApFloat.Add(b.M, b.R, e, TowardPositive);
            ApFloat a = lo.WithPrecision(p, mode), c = hi.WithPrecision(p, mode);
            if (a == c) return a;
            if (rounds > MaxRounds) throw new InvalidOperationException("no convergence");
        }
    }
}
