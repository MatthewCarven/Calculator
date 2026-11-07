using System.Windows;
public static class Program
{
    [STAThread] public static void Main()
    {
        System.Console.WriteLine(new Calc.ViewModels.Shell().Version);
        // The next line must not compile: the app has no reference to Natural.
        // _ = typeof(Natural.ApFloat);
    }
}
