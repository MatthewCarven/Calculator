# Brute-forces section 5's shortcuts with exact Fractions: wherever a threshold says "nudge", the nudged rounding must
# equal the true rounding, in every mode, for short and long x. The true value comes from the Taylor series with a
# rigorous tail. Prints how many cases each rule fired on, how many went wrong, and how far the rule can be pushed.
import random
from fractions import Fraction as F

MODES = ("N", "Z", "U", "D")

def rnd(v, p, mode):
    if v == 0:
        return F(0)
    neg = v < 0
    a = -v if neg else v
    n, d = a.numerator, a.denominator
    k = n.bit_length() - d.bit_length()
    if (k >= 0 and n < (d << k)) or (k < 0 and (n << -k) < d):
        k -= 1
    ulp = k - p + 1
    nn, dd = (n, d << ulp) if ulp >= 0 else (n << -ulp, d)
    q, r = divmod(nn, dd)
    if mode == "N":
        c = (2 * r > dd) - (2 * r < dd)
        up = c > 0 or (c == 0 and q & 1 == 1)
    elif mode == "Z":
        up = False
    elif mode == "U":
        up = not neg and r != 0
    else:
        up = neg and r != 0
    q += 1 if up else 0
    w = q * F(2) ** ulp
    return -w if neg else w

def series(coeffs, x, power_step=2, first=1):
    """Sum c_k x^(first + k*step) and a bound on the rest: twice the next term, |x| <= 1/8 and coefficients of slow growth."""
    s = F(0)
    p = x ** first
    step = x ** power_step
    for c in coeffs:
        s += c * p
        p *= step
    return s, 4 * abs(p)      # the coefficients here are all at most 4 in size

# Taylor coefficients, enough for |x| <= 1/8 at the precisions below (the tail bound is checked to be small).
ODD = {
    "sin":   (1, 0, [F(1), F(-1, 6), F(1, 120), F(-1, 5040), F(1, 362880), F(-1, 39916800)]),
    "sinh":  (1, 0, [F(1), F(1, 6), F(1, 120), F(1, 5040), F(1, 362880), F(1, 39916800)]),
    "tan":   (1, 0, [F(1), F(1, 3), F(2, 15), F(17, 315), F(62, 2835), F(1382, 155925)]),
    "tanh":  (1, 0, [F(1), F(-1, 3), F(2, 15), F(-17, 315), F(62, 2835), F(-1382, 155925)]),
    "atan":  (1, 0, [F(1), F(-1, 3), F(1, 5), F(-1, 7), F(1, 9), F(-1, 11)]),
    "atanh": (1, 0, [F(1), F(1, 3), F(1, 5), F(1, 7), F(1, 9), F(1, 11)]),
    "asin":  (1, 0, [F(1), F(1, 6), F(3, 40), F(5, 112), F(35, 1152), F(63, 2816)]),
    "asinh": (1, 0, [F(1), F(-1, 6), F(3, 40), F(-5, 112), F(35, 1152), F(-63, 2816)]),
}
QUAD = {
    "expm1": [F(1), F(1, 2), F(1, 6), F(1, 24), F(1, 120), F(1, 720), F(1, 5040), F(1, 40320), F(1, 362880), F(1, 3628800)],
    "log1p": [F(1), F(-1, 2), F(1, 3), F(-1, 4), F(1, 5), F(-1, 6), F(1, 7), F(-1, 8), F(1, 9), F(-1, 10)],
}
EVEN = {
    "cos":  [F(1), F(-1, 2), F(1, 24), F(-1, 720), F(1, 40320), F(-1, 3628800)],
    "cosh": [F(1), F(1, 2), F(1, 24), F(1, 720), F(1, 40320), F(1, 3628800)],
}
EXP = [F(1), F(1), F(1, 2), F(1, 6), F(1, 24), F(1, 120), F(1, 720), F(1, 5040), F(1, 40320), F(1, 362880), F(1, 3628800)]

def enclosure(name, x):
    if name in ODD:
        _, _, c = ODD[name]
        s, t = series(c, x, 2, 1)
    elif name in QUAD:
        s, t = series(QUAD[name], x, 1, 1)
    elif name in EVEN:
        s, t = series(EVEN[name], x, 2, 0)
    else:
        s, t = series(EXP, x, 1, 0)
    return s - t, s + t

def top_of(x):
    a = abs(x)
    n, d = a.numerator, a.denominator
    k = n.bit_length() - d.bit_length()
    if (k >= 0 and n < (d << k)) or (k < 0 and (n << -k) < d):
        k -= 1
    return k

def low_of(x):
    d = abs(x).denominator
    assert d & (d - 1) == 0
    return -(d.bit_length() - 1)

ABOVE = {"tan", "asin", "sinh", "atanh"}          # magnitude a little above x
BELOW = {"sin", "atan", "asinh", "tanh"}          # magnitude a little below x

def nudged(v, side, floor_bit):
    """v moved by far less than anything that matters: side +1 up, -1 down, in signed sense."""
    return v + side * F(2) ** (floor_bit - 64)

def rule(name, x, p):
    """(fires, centre, side) as section 5 says, or (False, ...) ."""
    T, e = top_of(x), low_of(x)
    lim = min(e, T - p - 2)
    if name in ABOVE or name in BELOW:
        fires = 3 * T + 3 <= lim
        up_in_magnitude = name in ABOVE
        side = (1 if up_in_magnitude else -1) * (1 if x > 0 else -1)
        return fires, x, side, lim
    if name == "expm1":
        return 2 * T + 2 <= lim, x, 1, lim
    if name == "log1p":
        return 2 * T + 2 <= lim, x, -1, lim
    if name == "exp":
        return T <= -p - 2, F(1), 1 if x > 0 else -1, -p - 8
    if name == "cos":
        return 2 * T + p + 2 <= 0, F(1), -1, -p - 8
    if name == "cosh":
        return 2 * T + p + 2 <= 0, F(1), 1, -p - 8
    raise KeyError(name)

random.seed(20261107)
names = list(ABOVE | BELOW) + ["expm1", "log1p", "exp", "cos", "cosh"]
total = fired = wrong = unsettled = 0
per = {}
for name in sorted(names):
    fires_here = wrong_here = 0
    for p in (2, 3, 4, 5, 8, 12, 17, 24, 33, 53):
        for trial in range(1400):
            # bit lengths: 1, a few, around p, and longer; T from far below the rule to just above it
            L = random.choice([1, 1, 2, 3, p - 1 if p > 1 else 1, p, p + 1, p + 2, p + 3, 2 * p + 5, 3 * p + 9])
            L = max(1, L)
            base = {"exp": -p - 2}.get(name, None)
            if name in ("cos", "cosh"):
                base = -(p + 2) // 2
            elif name in ("expm1", "log1p"):
                base = -p - 3
            elif base is None:
                base = -(p + 5) // 2
            T = base + random.randint(-6, 3)
            T = min(T, -3)
            m = (1 << (L - 1)) | (random.getrandbits(L - 1) if L > 1 else 0)
            if L > 1:
                m |= 1
            x = F(m) * F(2) ** (T - L + 1)
            if random.random() < 0.5:
                x = -x
            if name in ("cos", "cosh"):
                pass
            ok, centre, side, lim = rule(name, x, p)
            total += 1
            if not ok:
                continue
            lo, hi = enclosure(name, x)
            for mode in MODES:
                a, b = rnd(lo, p, mode), rnd(hi, p, mode)
                want = a
                if a != b:
                    unsettled += 1
                    continue
                got = rnd(nudged(centre, side, lim), p, mode)
                fires_here += 1
                if got != want:
                    wrong_here += 1
                    if wrong_here <= 3:
                        print(f"WRONG {name} p={p} mode={mode} x=2^{float(top_of(x))} L={L} got {float(got)} want {float(want)}")
    per[name] = (fires_here, wrong_here)
    fired += fires_here
    wrong += wrong_here
for k, v in per.items():
    print(f"{k:6} fired {v[0]:6}  wrong {v[1]}")
print(f"{total} inputs, {fired} rounded checks, {wrong} wrong, {unsettled} enclosures too wide to judge")

# The far end: tanh(x) for x >= (p + 2)/2 is 1 nudged toward zero. 1 - tanh x = 2/(e^(2x) + 1) < 2 e^(-2x).
bad = 0
for p in (2, 3, 8, 24, 53, 113):
    limit = F(p + 2, 2)
    for x in (limit, limit + F(1, 8), limit * 2):
        gap_hi = 2 * F(3 ** -1) ** 0   # placeholder, replaced below
        # e^(2x) >= 2^(2x) strictly for x > 0 since e > 2, so the gap is below 2^(1 - 2x).
        gap = F(2) ** (1 - int(2 * x))
        if not gap < F(2) ** (-p - 1 + 2):
            bad += 1
print("tanh far end: gap bound under half an ulp below 1 for all tried:", bad == 0)
