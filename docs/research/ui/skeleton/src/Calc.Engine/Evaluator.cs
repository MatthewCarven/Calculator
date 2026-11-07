using Natural;
namespace Calc.Engine;
public static class Evaluator { public static string Sum(string a, string b) => (ApFloat.Parse(a) + ApFloat.Parse(b)).ToString("R"); }
