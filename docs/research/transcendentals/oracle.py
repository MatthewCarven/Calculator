# An independent oracle for correctly rounded transcendentals, with the standard library only: a
# prototype of tests/reference/transcendental_reference.py. Nothing here comes from Natural.
#   exp, ln, log10: Python's decimal (libmpdec), correctly rounded at D digits, so the true value is
#     within one unit of the D-digit result. Exact input, exact Fractions.
#   sin, cos, atan: fixed-point Taylor on Python ints, with a counted error bound.
#   Each result is an enclosure [lo, hi]; both ends must round to the same p-bit value in the mode,
#   or the working precision grows and it goes again: Ziv's loop, written a second time, independently.
# Reads "fn x p mode result" lines and checks each.
import decimal, math, re, sys, time
from fractions import Fraction
sys.set_int_max_str_digits(0)

MODES = ("ToNearestEven", "TowardZero", "TowardPositive", "TowardNegative")

def parse_hex(s):
    if s == "0":
        return Fraction(0)
    neg = s.startswith("-")
    m, e = s.lstrip("-").split("p")
    v = int(m, 16) * Fraction(2) ** int(e)
    return -v if neg else v

def round_fraction(v, p, mode):
    """The exact Fraction v rounded to p bits in the mode, as a Fraction. Long division, no shortcuts."""
    if v == 0:
        return Fraction(0)
    neg = v < 0
    a = -v if neg else v
    n, d = a.numerator, a.denominator
    k = n.bit_length() - d.bit_length()
    if (k >= 0 and n < (d << k)) or (k < 0 and (n << -k) < d):
        k -= 1
    ulp = k - p + 1
    nn, dd = (n, d << ulp) if ulp >= 0 else (n << -ulp, d)
    q, r = divmod(nn, dd)
    if mode == "ToNearestEven":
        c = (2 * r > dd) - (2 * r < dd)
        up = c > 0 or (c == 0 and q & 1 == 1)
    elif mode == "TowardZero":
        up = False
    elif mode == "TowardPositive":
        up = not neg and r != 0
    else:
        up = neg and r != 0
    if up:
        q += 1
    w = q * Fraction(2) ** ulp
    return -w if neg else w

def to_decimal(x, ctx):
    """x exactly as a Decimal: m 2^e = m 5^k / 10^k for e = -k."""
    n, d = x.numerator, x.denominator
    k = d.bit_length() - 1
    assert d == 1 << k
    return decimal.Decimal(n * 5 ** k).scaleb(-k, ctx)

def decimal_enclosure(name, x, bits):
    digits = bits * 30103 // 100000 + 30
    ctx = decimal.Context(prec=digits, Emax=decimal.MAX_EMAX, Emin=decimal.MIN_EMIN, rounding=decimal.ROUND_HALF_EVEN)
    wide = decimal.Context(prec=decimal.MAX_PREC, Emax=decimal.MAX_EMAX, Emin=decimal.MIN_EMIN)
    d = to_decimal(x, wide)
    r = {"exp": d.exp, "ln": d.ln, "log10": d.log10}[name](ctx)
    sign, ds, ex = r.as_tuple()
    unit = Fraction(10) ** (ex + len(ds) - digits)
    v = Fraction(int(r.scaleb(-ex, wide))) * Fraction(10) ** ex
    return v - unit, v + unit

# ---- fixed point: a value is an int scaled by 2^w; every floor costs less than one unit ----

def pi_fixed(w):
    """Machin in fixed point. Error below 2^-w: series run on 64 extra bits, far more than the terms can cost."""
    g = w + 64
    def arc(n):
        one = (1 << g) // n
        term, s, k, n2 = one, one, 1, n * n
        while term:
            term //= n2
            t = term // (2 * k + 1)
            s = s - t if k & 1 else s + t
            k += 1
        return s
    return (16 * arc(5) - 4 * arc(239)) >> 64

_pi = {}
def pi_cached(w):
    if w not in _pi:
        _pi[w] = pi_fixed(w)
    return _pi[w]

def sin_cos_fixed(x, w):
    """(s, c, err): sin x and cos x scaled by 2^w, with the error bound in units. x a Fraction."""
    top = max(0, math.floor(math.log2(abs(x)))) if x != 0 and abs(x) >= 1 else 0
    wp = w + top + 16
    pi = pi_cached(wp)
    q = (x * 2 * (1 << wp) / pi).__round__()
    # r = x - q pi/2, in fixed point at wp; the error of pi costs |q| units.
    X = (x.numerator << wp) // x.denominator
    r = X - (q * pi >> 1)
    err_r = abs(q) + 2
    h = 8                               # halve so the series is short; each doubling doubles the error of c - 1, roughly
    t = r >> h
    u = t * t >> wp
    # Sum (-1)^i t^(2i) / (2i)! and Sum (-1)^i t^(2i+1) / (2i+1)!, in plain floors.
    c, s = 0, 0
    term = 1 << wp
    i = 0
    while term:
        c += term if i % 2 == 0 else -term
        term = term * t >> wp
        term //= (2 * i + 1)
        s += term if i % 2 == 0 else -term
        term = term * t >> wp
        term //= (2 * i + 2)
        i += 1
    err = 4 * i + 8 + (err_r >> h) + 2
    for _ in range(h):
        s, c = (s * c >> (wp - 1)), ((c * c - s * s) >> wp)
        err = 4 * err + 4
    err += err_r                        # |sin'| <= 1: the reduction's error goes straight through
    # undo the quadrant
    q &= 3
    if q == 1: s, c = c, -s
    elif q == 2: s, c = -s, -c
    elif q == 3: s, c = -c, s
    return s, c, err, wp

def trig_enclosure(name, x, bits):
    s, c, err, wp = sin_cos_fixed(x, bits + 40)
    v = s if name == "sin" else c
    unit = Fraction(1, 1 << wp)
    return v * unit - err * unit, v * unit + err * unit

def atan_enclosure(x, bits):
    """atan by a Newton step on the trig above: y a good guess from floats and repeated correction."""
    w = bits + 48
    y = Fraction(math.atan(float(x))).limit_denominator(1 << 60) if abs(x) < 1e300 else Fraction(0)
    prec = 40
    while True:
        s, c, err, wp = sin_cos_fixed(y, min(2 * prec + 32, w))
        S, C = Fraction(s, 1 << wp), Fraction(c, 1 << wp)
        d = (x * C - S) / (C + x * S)           # atan x = y + atan d
        y = y + d - d ** 3 / 3
        y = Fraction(round(y * (1 << (w + 16))), 1 << (w + 16))
        if prec >= w:
            break
        prec *= 2
    s, c, err, wp = sin_cos_fixed(y, w)
    S, C = Fraction(s, 1 << wp), Fraction(c, 1 << wp)
    d = (x * C - S) / (C + x * S)
    v = y + d - d ** 3 / 3
    e = Fraction(8 * err + 8, 1 << wp) + abs(d) ** 5
    return v - e, v + e

def enclosure(name, x, bits):
    if name in ("exp", "ln", "log10"):
        return decimal_enclosure(name, x, bits)
    if name in ("sin", "cos"):
        return trig_enclosure(name, x, bits)
    return atan_enclosure(x, bits)

def correctly_rounded(name, x, p, mode):
    bits = p + 64
    while True:
        lo, hi = enclosure(name, x, bits)
        a, b = round_fraction(lo, p, mode), round_fraction(hi, p, mode)
        if a == b:
            return a
        bits *= 2
        if bits > 200000:
            raise RuntimeError("no convergence")

def main():
    t0 = time.time()
    good = bad = 0
    seen = {}
    for line in open(sys.argv[1], encoding="utf-8"):
        name, xs, ps, mode, rs = line.split()
        x, p, r = parse_hex(xs), int(ps), parse_hex(rs)
        want = correctly_rounded(name, x, p, mode)
        if want == r:
            good += 1
        else:
            bad += 1
            if bad < 12:
                print("DIFFER", name, xs, p, mode, rs, "want", float(want))
        k = (name, p)
        seen.setdefault(k, [0, 0])
        seen[k][0 if want == r else 1] += 1
    print(f"{good} agree, {bad} differ, {time.time() - t0:.1f} s")
    for k in sorted(seen):
        print(k, seen[k])
    # Sanity of the oracle itself, against values known to their last digit.
    print("pi", float(Fraction(pi_fixed(200), 1 << 200)))
    lo, hi = trig_enclosure("sin", Fraction(10) ** 22, 200)
    print("sin(1e22) width bits", float(hi - lo), float(lo))
    x = Fraction(6381956970095103) * Fraction(2) ** 797
    lo, hi = trig_enclosure("cos", x, 300)
    print("cos(hard)", float(lo), float(hi))

main()
