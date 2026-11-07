# Makes natural-poll: a throwaway copy of the copied Natural source with step points added to
# Multiply, DivRem and Sqrt, so the overhead of TODO 5c's idea can be measured. Nothing in the
# real Natural folder is touched.
import shutil
from pathlib import Path

root = Path(__file__).parent
src = root / "natural-copy"
dst = root / "natural-poll"
if dst.exists():
    shutil.rmtree(dst)
shutil.copytree(src, dst, ignore=shutil.ignore_patterns("bin", "obj"))


def patch(name, pairs):
    p = dst / name
    t = p.read_bytes().decode("utf-8-sig").replace("\r\n", "\n")
    for old, new in pairs:
        assert t.count(old) == 1, (name, old, t.count(old))
        t = t.replace(old, new)
    p.write_text(t, encoding="utf-8", newline="\n")


patch("Magnitude.cs", [
    # Multiply: one poll per multiplier limb.
    ("""        for (int j = 0; j < multiplier.Length; j++)
        {
            uint word = multiplier[j];
            for (int k = 0; k < 32; k++)
            {
                if (((word >> k) & 1) == 0) continue;
                uint[] addend = shiftedBy[k] ??= ShiftLimbsLeft(multiplicand, k);
                AddInto(product, addend, j);
            }
        }
        return Trim(product);
    }
""", """        for (int j = 0; j < multiplier.Length; j++)
        {
            if (StopFlag.Requested) throw new OperationCanceledException();
            uint word = multiplier[j];
            for (int k = 0; k < 32; k++)
            {
                if (((word >> k) & 1) == 0) continue;
                uint[] addend = shiftedBy[k] ??= ShiftLimbsLeft(multiplicand, k);
                AddInto(product, addend, j);
            }
        }
        return Trim(product);
    }

    /// <summary>Multiply as an iterator (TODO 5c): yields between multiplier limbs, the units just done.</summary>
    internal static IEnumerable<long> MultiplySteps(uint[] a, uint[] b, uint[][] result)
    {
        if (a.Length == 0 || b.Length == 0) { result[0] = Empty; yield break; }
        if (a.Length < b.Length) (a, b) = (b, a);
        uint[] multiplicand = a, multiplier = b;
        var product = new uint[multiplicand.Length + multiplier.Length];
        var shiftedBy = new uint[32][];
        for (int j = 0; j < multiplier.Length; j++)
        {
            long units = MultiplyRow(product, multiplicand, shiftedBy, multiplier[j], j);
            yield return units;
        }
        result[0] = Trim(product);
    }

    /// <summary>One multiplier limb's additions, in a plain method: hot loops stay out of iterators.</summary>
    private static long MultiplyRow(uint[] product, uint[] multiplicand, uint[][] shiftedBy, uint word, int j)
    {
        long units = 0;
        for (int k = 0; k < 32; k++)
        {
            if (((word >> k) & 1) == 0) continue;
            uint[] addend = shiftedBy[k] ??= ShiftLimbsLeft(multiplicand, k);
            AddInto(product, addend, j);
            units += addend.Length;
        }
        return units;
    }
"""),
    # DivRem: one poll per quotient bit.
    ("""            uint bit = (dividend[i >> 5] >> (int)(i & 31)) & 1;
            ShiftLeftOneInPlace(rem, bit);
""", """            if (StopFlag.Requested) throw new OperationCanceledException();
            uint bit = (dividend[i >> 5] >> (int)(i & 31)) & 1;
            ShiftLeftOneInPlace(rem, bit);
"""),
    # Sqrt: one poll per root bit.
    ("""            long at = i << 1;                                  // even, so a pair never straddles two limbs
""", """            if (StopFlag.Requested) throw new OperationCanceledException();
            long at = i << 1;                                  // even, so a pair never straddles two limbs
"""),
    ("""internal static class Magnitude
""", """/// <summary>The step point's flag: a single volatile read per row.</summary>
public static class StopFlag
{
    public static volatile bool Requested;
}

internal static class Magnitude
"""),
])

patch("ApInt.cs", [
    ("""    private uint[] Mag => _mag ?? Magnitude.Empty;
""", """    private uint[] Mag => _mag ?? Magnitude.Empty;

    /// <summary>a * b as a job (TODO 5c's shape), stepped a multiplier limb at a time.</summary>
    public static Natural.Jobs.Job<ApInt> StartMultiply(ApInt a, ApInt b) =>
        Natural.Jobs.Job.Start<ApInt>(context => MultiplySteps(context, a, b));

    private static IEnumerable<long> MultiplySteps(Natural.Jobs.JobContext<ApInt> context, ApInt a, ApInt b)
    {
        var holder = new uint[1][];
        context.ExpectedWork = (long)a.Mag.Length * b.Mag.Length * 16;
        foreach (long units in Magnitude.MultiplySteps(a.Mag, b.Mag, holder)) yield return units;
        context.Result = new ApInt(holder[0], a._negative != b._negative);
    }
"""),
])

csproj = dst / "Natural.csproj"
t = csproj.read_bytes().decode("utf-8-sig")
t = t.replace("<Nullable>enable</Nullable>", "<Nullable>enable</Nullable>\n    <AssemblyName>NaturalPoll</AssemblyName>")
csproj.write_text(t, encoding="utf-8")
print("natural-poll written")
