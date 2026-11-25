// The console calculator arrives in B11. For now this checks that the worker landed beside it.

using Calc.Protocol;

var worker = Path.Combine(AppContext.BaseDirectory, Wire.WorkerFileName);
Console.WriteLine($"calc, wire version {Wire.Version}. The console calculator arrives in B11.");
if (!File.Exists(worker))
{
    Console.Error.WriteLine($"The worker isn't beside this program, at {worker}.");
    return 1;
}
Console.WriteLine("The worker is beside it.");
return 0;
