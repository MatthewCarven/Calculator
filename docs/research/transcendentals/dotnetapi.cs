// .NET 10's interfaces and special cases, as they are: what order item 9 must implement, and double's answers.
using System.Numerics;
using System.Reflection;

Type[] open = [typeof(IFloatingPointConstants<>), typeof(IFloatingPoint<>), typeof(IRootFunctions<>), typeof(IPowerFunctions<>),
    typeof(IExponentialFunctions<>), typeof(ILogarithmicFunctions<>), typeof(ITrigonometricFunctions<>), typeof(IHyperbolicFunctions<>),
    typeof(IFloatingPointIeee754<>)];
foreach (Type t in open)
{
    var names = t.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m is MethodInfo { IsSpecialName: false } or PropertyInfo)
        .Select(m => m is MethodInfo mi ? $"{m.Name}({mi.GetParameters().Length}{(mi.IsAbstract ? "" : ",default")})" : $"{m.Name}[prop]")
        .Distinct().OrderBy(s => s);
    var ifaces = t.GetInterfaces().Where(i => i.Name != "IEquatable`1" && !i.Name.StartsWith("IFormattable")).Select(i => i.Name.Split('`')[0]).Where(n => n.StartsWith("IFloating") || n.Contains("Functions"));
    Console.WriteLine($"{t.Name.Split('`')[0]} : {string.Join(",", ifaces)}\n   {string.Join(" ", names)}");
}
Console.WriteLine();
double inf = double.PositiveInfinity, nan = double.NaN, nz = -0.0;
string S(double d) => double.IsNaN(d) ? "NaN" : d == 0 ? (double.IsNegative(d) ? "-0" : "+0") : d.ToString("R");
void Row(string label, double v) => Console.WriteLine($"{label,-26} {S(v)}");
Row("Pow(NaN,0)", double.Pow(nan, 0)); Row("Pow(1,NaN)", double.Pow(1, nan)); Row("Pow(-1,inf)", double.Pow(-1, inf)); Row("Pow(0,-1)", double.Pow(0, -1));
Row("Pow(-0,-1)", double.Pow(nz, -1)); Row("Pow(-0,-2)", double.Pow(nz, -2)); Row("Pow(-8,1/3.)", double.Pow(-8, 1.0 / 3)); Row("Pow(-2,3)", double.Pow(-2, 3)); Row("Pow(0,0)", double.Pow(0, 0));
Row("Pow(-inf,3)", double.Pow(double.NegativeInfinity, 3)); Row("Pow(-inf,-3)", double.Pow(double.NegativeInfinity, -3)); Row("Pow(2,-inf)", double.Pow(2, double.NegativeInfinity)); Row("Pow(0.5,-inf)", double.Pow(0.5, double.NegativeInfinity));
Row("RootN(-8,3)", double.RootN(-8, 3)); Row("RootN(-8,2)", double.RootN(-8, 2)); Row("RootN(8,0)", double.RootN(8, 0)); Row("RootN(0,-2)", double.RootN(0, -2)); Row("RootN(-0,3)", double.RootN(nz, 3)); Row("RootN(-0,-3)", double.RootN(nz, -3)); Row("RootN(-0,2)", double.RootN(nz, 2)); Row("RootN(inf,-2)", double.RootN(inf, -2)); Row("RootN(-inf,3)", double.RootN(double.NegativeInfinity, 3)); Row("RootN(27,-3)", double.RootN(27, -3));
Row("Cbrt(-0)", double.Cbrt(nz)); Row("Cbrt(-27)", double.Cbrt(-27)); Row("Hypot(inf,NaN)", double.Hypot(inf, nan)); Row("Hypot(NaN,-inf)", double.Hypot(nan, double.NegativeInfinity)); Row("Hypot(-3,4)", double.Hypot(-3, 4)); Row("Hypot(-0,-0)", double.Hypot(nz, nz));
Row("SinPi(1)", double.SinPi(1)); Row("SinPi(-1)", double.SinPi(-1)); Row("SinPi(2)", double.SinPi(2)); Row("SinPi(-0)", double.SinPi(nz)); Row("SinPi(0.5)", double.SinPi(0.5)); Row("SinPi(1.5)", double.SinPi(1.5)); Row("SinPi(inf)", double.SinPi(inf));
Row("CosPi(0.5)", double.CosPi(0.5)); Row("CosPi(1.5)", double.CosPi(1.5)); Row("CosPi(1)", double.CosPi(1)); Row("CosPi(-0.5)", double.CosPi(-0.5));
Row("TanPi(0.5)", double.TanPi(0.5)); Row("TanPi(1.5)", double.TanPi(1.5)); Row("TanPi(-0.5)", double.TanPi(-0.5)); Row("TanPi(1)", double.TanPi(1)); Row("TanPi(2)", double.TanPi(2)); Row("TanPi(-1)", double.TanPi(-1)); Row("TanPi(-2)", double.TanPi(-2)); Row("TanPi(0.25)", double.TanPi(0.25)); Row("TanPi(0.75)", double.TanPi(0.75));
Row("AsinPi(1)", double.AsinPi(1)); Row("AcosPi(-1)", double.AcosPi(-1)); Row("AtanPi(1)", double.AtanPi(1)); Row("AtanPi(inf)", double.AtanPi(inf)); Row("Atan2Pi(1,1)", double.Atan2Pi(1, 1)); Row("Atan2Pi(-0,-1)", double.Atan2Pi(nz, -1));
Row("Atan2(+0,-0)", double.Atan2(0, nz)); Row("Atan2(-0,-0)", double.Atan2(nz, nz)); Row("Atan2(+0,+0)", double.Atan2(0, 0)); Row("Atan2(-0,+0)", double.Atan2(nz, 0)); Row("Atan2(inf,inf)", double.Atan2(inf, inf)); Row("Atan2(inf,-inf)", double.Atan2(inf, double.NegativeInfinity));
Row("Log(0)", double.Log(0)); Row("Log(-0)", double.Log(nz)); Row("Log(-1)", double.Log(-1)); Row("Log(1)", double.Log(1)); Row("Log2(8)", double.Log2(8)); Row("Log10(1000)", double.Log10(1000));
Row("Log(8,2)", double.Log(8, 2)); Row("Log(5,1)", double.Log(5, 1)); Row("Log(1,0)", double.Log(1, 0)); Row("Log(5,0)", double.Log(5, 0)); Row("Log(1,inf)", double.Log(1, inf)); Row("Log(5,inf)", double.Log(5, inf)); Row("Log(NaN,1)", double.Log(nan, 1)); Row("Log(0,2)", double.Log(0, 2)); Row("Log(5,-2)", double.Log(5, -2));
Row("LogP1(-1)", double.LogP1(-1)); Row("LogP1(-0)", double.LogP1(nz)); Row("ExpM1(-0)", double.ExpM1(nz)); Row("ExpM1(-inf)", double.ExpM1(double.NegativeInfinity)); Row("Exp2(10)", double.Exp2(10)); Row("Exp10(-2)", double.Exp10(-2)); Row("Exp10M1(2)", double.Exp10M1(2)); Row("Exp2M1(-0)", double.Exp2M1(nz));
Row("Log2P1(1)", double.Log2P1(1)); Row("Log10P1(9)", double.Log10P1(9));
Row("Atanh(1)", double.Atanh(1)); Row("Atanh(-1)", double.Atanh(-1)); Row("Atanh(2)", double.Atanh(2)); Row("Acosh(1)", double.Acosh(1)); Row("Acosh(0.5)", double.Acosh(0.5)); Row("Asinh(-0)", double.Asinh(nz)); Row("Tanh(inf)", double.Tanh(inf));
Row("Sin(inf)", double.Sin(inf)); Row("Tan(-0)", double.Tan(nz)); Row("Asin(2)", double.Asin(2)); Row("Acos(1)", double.Acos(1));
Row("DegToRad(180)", double.DegreesToRadians(180)); Row("RadToDeg(pi)", double.RadiansToDegrees(Math.PI));
Console.WriteLine($"ILogB(0) {double.ILogB(0)}, ILogB(NaN) {double.ILogB(nan)}, ILogB(inf) {double.ILogB(inf)}, Epsilon {double.Epsilon}, ScaleB(1,-1100) {double.ScaleB(1, -1100)}, ReciprocalEstimate(3) {double.ReciprocalEstimate(3)}, Lerp(1,3,0.5) {double.Lerp(1, 3, 0.5)}");
Console.WriteLine($"Sin(1e22) {Math.Sin(1e22):R}, Cos(6381956970095103*2^797) {Math.Cos(6381956970095103.0 * Math.ScaleB(1, 797)):R}");
