"""Old against new tables from the E-*.csv logs, and crossing points by log-log interpolation."""
import csv
import glob
import math
import os
import statistics
import sys

B = r"C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\bench"
ARMS = ["old", "ks", "kar", "sd"]


def rows(path):
    with open(path, newline="") as f:
        for r in csv.reader(f):
            if r and r[0] == "T" and len(r) >= 10:
                yield r


def load(pattern):
    out = {}
    for path in glob.glob(os.path.join(B, pattern)):
        for r in rows(path):
            key = (r[1], int(r[4]), r[5])
            out.setdefault(key, []).append(float(r[6]))
    return out


def med(v):
    return statistics.median(v)


def show(s):
    if s < 1e-3:
        return f"{s * 1e6:.0f} us"
    if s < 1:
        return f"{s * 1e3:.1f} ms"
    return f"{s:.2f} s"


def table(title, section, ops, sizes, data):
    print(f"\n{title}")
    print("op, size, " + ", ".join(ARMS) + ", gain old/kar, gain old/sd, runs")
    for op in ops:
        for size in sizes:
            cells, nums = [], {}
            n = []
            for arm in ARMS:
                v = data[arm].get((section, size, op))
                if v:
                    nums[arm] = med(v)
                    cells.append(show(nums[arm]))
                    n.append(str(len(v)))
                else:
                    cells.append("-")
                    n.append("0")
            gk = f"{nums['old'] / nums['kar']:.1f}x" if "old" in nums and "kar" in nums else "-"
            gs = f"{nums['old'] / nums['sd']:.1f}x" if "old" in nums and "sd" in nums else "-"
            print(f"{op}, {size}, " + ", ".join(cells) + f", {gk}, {gs}, {'/'.join(n)}")


def main():
    data = {a: {} for a in ARMS}
    for a in ARMS:
        data[a].update(load(f"E-{a}-mul.csv"))
        data[a].update(load(f"E-{a}-ops.csv"))
        data[a].update(load(f"E-{a}-d.csv"))
    table("Multiply", "B", ["Mul"], [1000, 10000, 100000, 1000000], data)
    table("Basic ops", "B", ["Div", "Sqrt", "DivSmall", "TextEfull", "TextE50", "Parse"], [16384, 65536, 262144], data)
    table("Prototypes", "D",
          ["pi_shrink", "pi_chudnovsky", "e_binary_split", "sinx_smith_shrink", "expx_smith_r8", "lnx_halley", "powxy"],
          [1024, 4096, 16384, 65536], data)
    cal = []
    for path in glob.glob(os.path.join(B, "E-*-*.csv")):
        if "-kt" in path:
            continue
        cal += [float(r[9]) for r in rows(path)]
    if cal:
        print(f"\ncalibration ms: min {min(cal):.1f} median {med(cal):.1f} max {max(cal):.1f} n={len(cal)}")


def crossings(pattern, targets):
    """For each op in a log: the size where the median passes the target, interpolated log-log."""
    data = load(pattern)
    ops = sorted({k[2] for k in data})
    print(f"\ncrossings in {pattern}")
    for op in ops:
        pts = sorted((k[1], med(v)) for k, v in data.items() if k[2] == op)
        for target in targets:
            found = None
            for (a, ta), (b, tb) in zip(pts, pts[1:]):
                if ta <= target < tb:
                    t = (math.log(target) - math.log(ta)) / (math.log(tb) - math.log(ta))
                    found = a * (b / a) ** t
                    break
            if found:
                print(f"{op}: {show(target)} at {found:,.0f} bits ({found * math.log10(2):,.0f} digits)")
    print("  " + "; ".join(f"{k[2]}@{k[1]}={show(med(v))}" for k, v in sorted(data.items(), key=lambda kv: (kv[0][2], kv[0][1]))))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "cross":
        crossings(sys.argv[2], [float(x) for x in sys.argv[3:]])
    else:
        main()
