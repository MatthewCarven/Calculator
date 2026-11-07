"""Checks OnTop.cs's results exactly, with Python's standard library only: big integers, Fraction
and decimal. Each result must be the correctly rounded value at its precision (to nearest, ties to
even), or, for ROOT, in the rounding mode named on the line."""
import sys
from decimal import Decimal, getcontext
from fractions import Fraction

path = sys.argv[1]
WORK = 12000  # bits of the reference values


def read_hex(s):
    """C's %a text, as written by ApFloat "A": an exact Fraction."""
    neg = s.startswith("-")
    if neg:
        s = s[1:]
    s = s.lower()
    assert s.startswith("0x"), s
    mant, _, exp = s[2:].partition("p")
    whole, _, frac = mant.partition(".")
    v = Fraction(int((whole + frac) or "0", 16), 16 ** len(frac)) * Fraction(2) ** int(exp or "0")
    return -v if neg else v


def round_to(q, p, mode="ToNearestEven"):
    """q (a Fraction) rounded to p significant bits."""
    if q == 0:
        return Fraction(0)
    sign = -1 if q < 0 else 1
    a = abs(q)
    e = a.numerator.bit_length() - a.denominator.bit_length()
    if Fraction(2) ** e > a:
        e -= 1
    if Fraction(2) ** (e + 1) <= a:
        e += 1
    shift = p - 1 - e
    scaled = a * Fraction(2) ** shift
    n = scaled.numerator // scaled.denominator
    rem = scaled - n
    if mode == "ToNearestEven":
        up = rem > Fraction(1, 2) or (rem == Fraction(1, 2) and n & 1)
    elif mode == "TowardZero":
        up = False
    elif mode == "TowardPositive":
        up = sign > 0 and rem != 0
    elif mode == "TowardNegative":
        up = sign < 0 and rem != 0
    else:
        raise ValueError(mode)
    return sign * Fraction(n + (1 if up else 0)) * Fraction(2) ** (-shift)


def from_decimal(d):
    return Fraction(d)


getcontext().prec = int(WORK * 0.30103) + 20
getcontext().Emin = -10**15
getcontext().Emax = 10**15


def fixed(x):
    return Fraction(x, 1 << WORK)


def atan_inv(n, w):
    one = 1 << w
    term = one // n
    total = term
    k = 1
    sign = -1
    n2 = n * n
    while term:
        term //= n2
        k += 2
        total += sign * (term // k)
        sign = -sign
    return total


def pi_fixed():
    w = WORK + 32
    return Fraction(4 * (4 * atan_inv(5, w) - atan_inv(239, w)), 1 << w)


def taylor_sin_cos(x):
    """x a Fraction; fixed point at WORK + 64 bits."""
    w = WORK + 64
    one = 1 << w
    X = (x.numerator << w) // x.denominator
    # reduce, to keep the series short
    s, c = 0, 0
    # sin
    term = X
    k = 1
    total = 0
    while term:
        total += term
        term = -((((term * X) >> w) * X) >> w) // ((k + 1) * (k + 2))
        k += 2
    s = total
    term = one
    k = 0
    total = 0
    while term:
        total += term
        term = -((((term * X) >> w) * X) >> w) // ((k + 1) * (k + 2))
        k += 2
    c = total
    return Fraction(s, one), Fraction(c, one)


def atan_series(x):
    """atan of a Fraction via Newton on the sine and cosine above, from a float guess."""
    import math
    y = Fraction(math.atan(float(x)))
    for _ in range(14):
        s, c = taylor_sin_cos(y)
        y = y + (x * c - s) / (c + x * s)
        # keep the fraction's size bounded
        y = Fraction((y.numerator << (WORK + 64)) // y.denominator, 1 << (WORK + 64))
    return y


cache = {}


def memo(key, f):
    if key not in cache:
        cache[key] = f()
    return cache[key]


def reference(kind, arg):
    a = None
    if arg != "-" and "," not in arg:
        a = Fraction(Decimal(arg))
    if kind == "PI":
        return memo("pi", pi_fixed)
    if kind == "TAU":
        return 2 * memo("pi", pi_fixed)
    if kind == "E":
        return memo("e", lambda: from_decimal(Decimal(1).exp()))
    if kind == "LN2":
        return memo("ln2", lambda: from_decimal(Decimal(2).ln()))
    if kind == "PHI":
        return memo("phi", lambda: from_decimal((1 + Decimal(5).sqrt()) / 2))
    if kind == "EXP":
        return memo(("exp", arg), lambda: from_decimal(Decimal(arg).exp()))
    if kind == "LN":
        return memo(("ln", arg), lambda: from_decimal(Decimal(arg).ln()))
    if kind == "LOG10":
        return memo(("log10", arg), lambda: from_decimal(Decimal(arg).log10()))
    if kind == "LOG2":
        return memo(("log2", arg), lambda: from_decimal(Decimal(arg).ln() / Decimal(2).ln()))
    if kind == "SIN":
        return memo(("sin", arg), lambda: taylor_sin_cos(a)[0])
    if kind == "COS":
        return memo(("cos", arg), lambda: taylor_sin_cos(a)[1])
    if kind == "TAN":
        return memo(("tan", arg), lambda: taylor_sin_cos(a)[0] / taylor_sin_cos(a)[1])
    if kind == "ATAN":
        return memo(("atan", arg), lambda: atan_series(a))
    if kind == "SINH":
        return memo(("sinh", arg), lambda: from_decimal((Decimal(arg).exp() - (-Decimal(arg)).exp()) / 2))
    if kind == "POWR":
        x, y = arg.split(",")
        return memo(("pow", arg), lambda: from_decimal((Decimal(y) * Decimal(x).ln()).exp()))
    raise ValueError(kind)


good = bad = 0
by_kind = {}
for line in open(path, encoding="utf-8"):
    parts = line.split()
    if not parts:
        continue
    kind = parts[0]
    if kind == "ROOT":
        # exact check, no reference value: r is the rounding of x^(1/k) iff x sits between the
        # k-th powers of the rounding boundaries.
        x, k, p, mode, r = read_hex(parts[1]), int(parts[2]), int(parts[3]), parts[4], read_hex(parts[5])
        # the two neighbours of r at p bits
        e = r.numerator.bit_length() - r.denominator.bit_length()
        if Fraction(2) ** e > r:
            e -= 1
        ulp = Fraction(2) ** (e - p + 1)
        lo, hi = r - ulp, r + ulp
        if mode == "ToNearestEven":
            ok = ((r + lo) / 2) ** k <= x <= ((r + hi) / 2) ** k
        elif mode in ("TowardZero", "TowardNegative"):
            ok = r ** k <= x < hi ** k
        else:
            ok = lo ** k < x <= r ** k
    elif kind == "POW":
        x, n, p, r = read_hex(parts[1]), int(parts[2]), int(parts[3]), read_hex(parts[4])
        ok = round_to(x ** n, p) == r
    else:
        arg, p, r = parts[1], int(parts[2]), read_hex(parts[3])
        ok = round_to(reference(kind, arg), p) == r
        if not ok:
            ref = reference(kind, arg)
            print(f"  WRONG {kind} {arg} at {p}: off by {float((r - ref) / abs(ref))} relative; "
                  f"rounded ref differs by {float(abs(r - round_to(ref, p)) / abs(ref))}")
    by_kind.setdefault(kind, [0, 0])
    by_kind[kind][0 if ok else 1] += 1
    good += ok
    bad += not ok

for k, (g, b) in sorted(by_kind.items()):
    print(f"{k:6} {g:4} correctly rounded, {b} not")
print(f"total {good} right, {bad} wrong")
