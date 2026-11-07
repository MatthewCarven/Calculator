using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Throwaway WPF probe. One scenario a process, so nothing warms the next one.
//   wpfprobe <scenario> [chars]
// Scenarios: hello hello-fluent facts tb-nowrap tb-wrap tb-lines tblock-nowrap tblock-wrap rtb
//            list list-full list-novirt view view-fluent shot
public sealed class Row
{
    public int Index { get; init; }
    public string Preview { get; init; } = "";
    public string Full { get; init; } = "";
}

/// <summary>The lazily drawn number: only the lines in view are ever shaped.</summary>
public sealed class DigitView : FrameworkElement
{
    private readonly string _text;
    private readonly int _perLine;
    private readonly Typeface _face = new("Consolas");
    public long FirstLine;
    public long LineCount => (_text.Length + _perLine - 1) / _perLine;

    public DigitView(string text, int perLine) { _text = text; _perLine = perLine; }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip, y = 0, h = 17;
        for (long l = FirstLine; l < LineCount && y < ActualHeight; l++, y += h)
        {
            int at = (int)(l * _perLine);
            string s = _text.Substring(at, Math.Min(_perLine, _text.Length - at));
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, 13, Brushes.Black, ppd);
            dc.DrawText(ft, new Point(4, y));
        }
    }
}

static class P
{
    static readonly List<string> Out = [];
    static string _stage = "start";
    static Window _win = null!;
    static string? _png;
    static string? _spikeWorker;

    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);

    static string Digits(int n, bool lines = false)
    {
        var r = new Random(42);
        var sb = new StringBuilder(n + n / 50);
        for (int i = 0; i < n; i++)
        {
            sb.Append((char)('0' + r.Next(10)));
            if (lines && i % 100 == 99) sb.Append('\n');
        }
        return sb.ToString();
    }

    static double Lap(Stopwatch sw) { double ms = sw.Elapsed.TotalMilliseconds; sw.Restart(); return ms; }

    /// <summary>Waits for layout, then for a frame to have gone through the render thread.</summary>
    static async Task Settle()
    {
        _win.UpdateLayout();
        for (int i = 0; i < 2; i++)
        {
            var tcs = new TaskCompletionSource();
            EventHandler? h = null;
            h = (_, _) => { CompositionTarget.Rendering -= h; tcs.TrySetResult(); };
            CompositionTarget.Rendering += h;
            await tcs.Task;
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    static async Task<double> Stage(string name, Func<Task> work)
    {
        _stage = name;
        var sw = Stopwatch.StartNew();
        await work();
        double ms = sw.Elapsed.TotalMilliseconds;
        Out.Add($"{name}={ms:F1}");
        return ms;
    }

    static TextBox Box(string _) => new()
    {
        FontFamily = new FontFamily("Consolas"), FontSize = 13,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    static async Task TextBoxScenario(int n, TextWrapping wrap, bool lines)
    {
        string text = Digits(n, lines);
        var box = Box("");
        box.TextWrapping = wrap;
        box.AcceptsReturn = lines;
        _win.Content = box;
        await Settle();
        await Stage("set", () => { box.Text = text; return Task.CompletedTask; });
        await Stage("layout", () => { _win.UpdateLayout(); return Task.CompletedTask; });
        await Stage("frame", Settle);
        if (_png != null) Shot(_png + "-start.png");
        await Stage("end", async () => { box.CaretIndex = box.Text.Length; box.ScrollToEnd(); await Settle(); });
        if (_png != null) Shot(_png + "-end.png");
        await Stage("key1", async () => { box.SelectedText = "7"; box.CaretIndex = box.Text.Length; await Settle(); });
        await Stage("key2", async () => { box.SelectedText = "8"; box.CaretIndex = box.Text.Length; await Settle(); });
        Out.Add($"len={box.Text.Length} lines={box.LineCount} line0={box.GetLineLength(0)}");
    }

    static async Task TextBlockScenario(int n, TextWrapping wrap)
    {
        string text = Digits(n);
        var block = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = wrap };
        var sv = new ScrollViewer { Content = block, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        _win.Content = sv;
        await Settle();
        await Stage("set", () => { block.Text = text; return Task.CompletedTask; });
        await Stage("layout", () => { _win.UpdateLayout(); return Task.CompletedTask; });
        await Stage("frame", Settle);
        if (_png != null) Shot(_png + "-start.png");
        await Stage("end", async () => { sv.ScrollToEnd(); await Settle(); });
        if (_png != null) Shot(_png + "-end.png");
    }

    static async Task RichScenario(int n)
    {
        string text = Digits(n);
        var box = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _win.Content = box;
        await Settle();
        await Stage("set", () => { box.Document = new FlowDocument(new Paragraph(new Run(text))); return Task.CompletedTask; });
        await Stage("layout", () => { _win.UpdateLayout(); return Task.CompletedTask; });
        await Stage("frame", Settle);
        await Stage("end", async () => { box.ScrollToEnd(); await Settle(); });
    }

    static async Task ListScenario(int n, int rows, bool virtualise, bool bindFull)
    {
        string text = Digits(n);
        var data = new List<Row>(rows);
        for (int i = 0; i < rows; i++)
            data.Add(new Row { Index = i, Full = text, Preview = $"#{i}  {text.Substring(i, 60)}…  ({n:N0} digits)" });

        var xaml = new FrameworkElementFactory(typeof(TextBlock));
        xaml.SetBinding(TextBlock.TextProperty, new Binding(bindFull ? nameof(Row.Full) : nameof(Row.Preview)));
        xaml.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        xaml.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        xaml.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
        var list = new ListBox { ItemTemplate = new DataTemplate { VisualTree = xaml } };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list, virtualise);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Item);
        _win.Content = list;
        await Settle();
        await Stage("fill", async () => { list.ItemsSource = data; await Settle(); });
        var r = new Random(7);
        var steps = new List<double>();
        _stage = "scroll";
        int jumps = bindFull ? 10 : 100;
        for (int i = 0; i < jumps; i++)
        {
            var sw = Stopwatch.StartNew();
            list.ScrollIntoView(data[r.Next(rows)]);
            await Settle();
            steps.Add(sw.Elapsed.TotalMilliseconds);
        }
        steps.Sort();
        Out.Add($"jump_median={steps[steps.Count / 2]:F1}");
        Out.Add($"jump_max={steps[^1]:F1}");
    }

    static async Task ViewScenario(int n)
    {
        string text = Digits(n);
        var view = new DigitView(text, 100);
        var bar = new ScrollBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = view.LineCount, ViewportSize = 30 };
        var grid = new DockPanel();
        DockPanel.SetDock(bar, Dock.Right);
        grid.Children.Add(bar);
        grid.Children.Add(view);
        await Stage("show", async () => { _win.Content = grid; await Settle(); });
        var r = new Random(7);
        var steps = new List<double>();
        _stage = "scroll";
        for (int i = 0; i < 200; i++)
        {
            var sw = Stopwatch.StartNew();
            view.FirstLine = r.NextInt64(view.LineCount);
            view.InvalidateVisual();
            await Settle();
            steps.Add(sw.Elapsed.TotalMilliseconds);
        }
        steps.Sort();
        Out.Add($"jump_median={steps[steps.Count / 2]:F1}");
        Out.Add($"jump_p99={steps[(int)(steps.Count * .99)]:F1}");
        Out.Add($"jump_max={steps[^1]:F1}");
        // render one frame's OnRender alone, without the wait for vsync
        var one = new List<double>();
        var rtb = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32);
        for (int i = 0; i < 100; i++)
        {
            view.FirstLine = r.NextInt64(view.LineCount);
            view.InvalidateVisual();
            var sw = Stopwatch.StartNew();
            view.UpdateLayout();
            rtb.Render(view);
            one.Add(sw.Elapsed.TotalMilliseconds);
        }
        one.Sort();
        Out.Add($"offscreen_frame_median={one[one.Count / 2]:F2}");
    }

    static void Shot(string path)
    {
        var fe = (FrameworkElement)_win.Content;
        var rtb = new RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(fe);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    static FrameworkElement Mock()
    {
        var expr = new TextBox { Text = "sqrt(2) * 3", FontFamily = new FontFamily("Consolas"), FontSize = 18, Margin = new Thickness(8) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(expr, "expr");
        var keys = new System.Windows.Controls.Primitives.UniformGrid { Rows = 4, Columns = 4, Margin = new Thickness(8) };
        foreach (string k in "7 8 9 / 4 5 6 * 1 2 3 - 0 . = +".Split(' '))
        {
            var b = new Button { Content = k, Margin = new Thickness(2), MinHeight = 36 };
            System.Windows.Automation.AutomationProperties.SetAutomationId(b, "key" + k);
            string key = k;
            b.Click += (_, _) => expr.Text += key;
            keys.Children.Add(b);
        }
        var bits = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 16, Margin = new Thickness(8) };
        for (int i = 0; i < 32; i++) bits.Children.Add(new System.Windows.Controls.Primitives.ToggleButton { Content = i % 3 == 0 ? "1" : "0", IsChecked = i % 3 == 0, Margin = new Thickness(1), MinWidth = 24 });
        var list = new ListBox { Margin = new Thickness(8), Height = 120 };
        for (int i = 0; i < 6; i++) list.Items.Add($"#{i}  1.41421356237309504880168872420969807856967187537694…  ({(i + 1) * 1000} digits)");
        var bar = new ProgressBar { Height = 8, Value = 40, Margin = new Thickness(8) };
        var stop = new Button { Content = "Stop", Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 4, 16, 4) };
        var page = new StackPanel();
        page.Children.Add(expr); page.Children.Add(keys); page.Children.Add(bar); page.Children.Add(stop); page.Children.Add(list);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Standard", Content = page });
        tabs.Items.Add(new TabItem { Header = "Scientific" });
        tabs.Items.Add(new TabItem { Header = "Programmer", Content = bits });
        tabs.Items.Add(new TabItem { Header = "IEEE" });
        return tabs;
    }

    static void Png(FrameworkElement fe, int w, int h, string path, double dpi = 96)
    {
        var rtb = new RenderTargetBitmap((int)(w * dpi / 96), (int)(h * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(fe);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    /// <summary>The agent's look at the window: no window, no message loop, no desktop needed.</summary>
    static int Offscreen(Application app, ThemeMode theme, string path)
    {
        var sw = Stopwatch.StartNew();
        app.ThemeMode = theme;
        var root = new Border { Child = Mock(), Width = 800, Height = 600 };
        root.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        root.Measure(new Size(800, 600));
        root.Arrange(new Rect(0, 0, 800, 600));
        root.UpdateLayout();
        Png(root, 800, 600, path);
        Png(root, 800, 600, path.Replace(".png", "-192dpi.png"), 192);
        Console.WriteLine($"offscreen {theme.Value}: {sw.Elapsed.TotalMilliseconds:F0} ms, wrote {path}");
        return 0;
    }

    /// <summary>A second process drives the real window through UI Automation.</summary>
    static int UiaDriver()
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, "mock") { UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var sw = Stopwatch.StartNew();
        System.Windows.Automation.AutomationElement? win = null;
        while (win == null && sw.ElapsedMilliseconds < 15000)
        {
            Thread.Sleep(100);
            win = System.Windows.Automation.AutomationElement.RootElement.FindFirst(System.Windows.Automation.TreeScope.Children,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ProcessIdProperty, p.Id));
        }
        if (win == null) { Console.WriteLine("uia: window not found"); p.Kill(); return 1; }
        double found = sw.Elapsed.TotalMilliseconds;
        var by = (string id) => win.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, id));
        var t = Stopwatch.StartNew();
        foreach (string k in new[] { "key7", "key8", "key+" })
            ((System.Windows.Automation.InvokePattern)by(k).GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
        double pressed = t.Elapsed.TotalMilliseconds;
        Thread.Sleep(200);
        string value = ((System.Windows.Automation.ValuePattern)by("expr").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).Current.Value;
        Console.WriteLine($"uia: window found after {found:F0} ms; three keys pressed in {pressed:F0} ms; expr reads \"{value}\"");
        p.Kill();
        return 0;
    }

    // The chosen shape end to end: a real window, a real worker process mid-multiply, frames counted,
    // then Stop: kill, respawn, and the next answer back on the UI thread.
    static async Task Spike(string workerExe)
    {
        var bar = new ProgressBar { IsIndeterminate = true, Height = 8 };
        var box = new TextBox { Text = "1+1", FontFamily = new FontFamily("Consolas") };
        var panel = new StackPanel();
        panel.Children.Add(box); panel.Children.Add(bar);
        _win.Content = panel;
        await Settle();

        Process Spawn()
        {
            var psi = new ProcessStartInfo(workerExe, "worker") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            var w = Process.Start(psi)!;
            w.StandardInput.AutoFlush = true; w.StandardInput.NewLine = "\n";
            return w;
        }
        async Task<double> Answer(Process w)
        {
            var sw = Stopwatch.StartNew();
            await w.StandardInput.WriteLineAsync("add 0.1 0.2");
            string? a = await w.StandardOutput.ReadLineAsync();
            Out.Add($"answer={a}");
            return sw.Elapsed.TotalMilliseconds;
        }

        var worker = Spawn();
        await Answer(worker);

        var frames = new List<double>();
        long prev = Stopwatch.GetTimestamp();
        EventHandler tick = (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            frames.Add((now - prev) * 1000.0 / Stopwatch.Frequency);
            prev = now;
            box.Text = frames.Count.ToString();      // the UI thread doing a little work every frame
        };
        CompositionTarget.Rendering += tick;
        await worker.StandardInput.WriteLineAsync("mul 2000000");
        await worker.StandardOutput.ReadLineAsync();     // "started"
        await Task.Delay(4000);

        var stop = Stopwatch.StartNew();
        worker.Kill();
        await worker.WaitForExitAsync();
        double killed = stop.Elapsed.TotalMilliseconds;
        worker = Spawn();
        await Answer(worker);
        double ready = stop.Elapsed.TotalMilliseconds;
        CompositionTarget.Rendering -= tick;
        worker.Kill();
        frames.RemoveAt(0);
        frames.Sort();
        Out.Add($"frames={frames.Count} frame_median={frames[frames.Count / 2]:F1} frame_p99={frames[(int)(frames.Count * .99)]:F1} frame_max={frames[^1]:F1} over34ms={frames.Count(f => f > 34)}");
        Out.Add($"stop_to_exit={killed:F1} stop_to_next_answer={ready:F1}");
    }

    static async Task Facts()
    {
        int aware = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
        var dpi = VisualTreeHelper.GetDpi(_win);
        Out.Add($"thread_dpi_awareness={aware}(0=unaware,1=system,2=permonitor)");
        Out.Add($"dpi_scale={dpi.DpiScaleX}");
        Out.Add($"software_render_tier={RenderCapability.Tier >> 16}");
        await Task.CompletedTask;
    }

    [STAThread]
    static int Main(string[] args)
    {
        var started = Process.GetCurrentProcess().StartTime;
        string scenario = args.Length > 0 ? args[0] : "hello";
        int chars = args.Length > 1 ? int.Parse(args[1]) : 1_000_000;
        _png = args.Length > 3 && scenario != "shot" && !scenario.StartsWith("spike") ? args[3] : null;
        _spikeWorker = scenario.StartsWith("spike") ? args[3] : null;
        if (scenario == "uia-driver") return UiaDriver();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (scenario == "offscreen-merged")
        {
            // Fluent without the WPF0001 API: the theme's own dictionary, merged by hand.
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml") });
            var root = new Border { Child = Mock(), Width = 800, Height = 600 };
            root.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
            root.Measure(new Size(800, 600));
            root.Arrange(new Rect(0, 0, 800, 600));
            root.UpdateLayout();
            Png(root, 800, 600, args[3]);
            Console.WriteLine("merged Fluent.Dark.xaml rendered");
            return 0;
        }
        if (scenario == "offscreen-dark") return Offscreen(app, ThemeMode.Dark, args[3]);
        if (scenario == "offscreen-light") return Offscreen(app, ThemeMode.Light, args[3]);
        if (scenario == "offscreen-none") return Offscreen(app, ThemeMode.None, args[3]);
        if (scenario.EndsWith("-fluent")) app.ThemeMode = ThemeMode.System;
        if (scenario == "mock") app.ThemeMode = ThemeMode.System;
        _win = new Window
        {
            Title = "calc ui probe", Width = 800, Height = 600, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 60, Top = 60, Content = new TextBlock { Text = "hello" },
        };

        var watchdog = new Thread(() =>
        {
            Thread.Sleep(args.Length > 2 ? int.Parse(args[2]) * 1000 : 150_000);
            Console.WriteLine($"{scenario} chars={chars} TIMEOUT at stage {_stage} | {string.Join(' ', Out)}");
            Environment.Exit(3);
        }) { IsBackground = true };
        watchdog.Start();

        bool once = false;
        _win.ContentRendered += async (_, _) =>
        {
            if (once) return; once = true;
            double fromStart = (DateTime.Now - started).TotalMilliseconds;
            try
            {
                Out.Add($"first_render={fromStart:F0}");
                switch (scenario)
                {
                    case "hello": case "hello-fluent": break;
                    case "spike": case "spike-fluent": await Spike(_spikeWorker!); break;
                    case "mock": _win.Content = Mock(); await Task.Delay(8000); break;
                    case "facts": case "facts-fluent": await Facts(); break;
                    case "tb-nowrap": await TextBoxScenario(chars, TextWrapping.NoWrap, false); break;
                    case "tb-wrap": await TextBoxScenario(chars, TextWrapping.Wrap, false); break;
                    case "tb-lines": await TextBoxScenario(chars, TextWrapping.NoWrap, true); break;
                    case "tblock-nowrap": await TextBlockScenario(chars, TextWrapping.NoWrap); break;
                    case "tblock-wrap": await TextBlockScenario(chars, TextWrapping.Wrap); break;
                    case "rtb": await RichScenario(chars); break;
                    case "list": await ListScenario(chars, 1000, true, false); break;
                    case "list-fluent": await ListScenario(chars, 1000, true, false); break;
                    case "list-full": await ListScenario(chars, 1000, true, true); break;
                    case "list-novirt": await ListScenario(chars, 1000, false, false); break;
                    case "view": case "view-fluent": await ViewScenario(chars); break;
                    case "shot":
                        _win.Content = Box(""); ((TextBox)_win.Content).Text = "12345";
                        await Settle();
                        Shot(args[3]);
                        break;
                }
                using var me = Process.GetCurrentProcess();
                Out.Add($"private_mb={me.PrivateMemorySize64 / 1048576.0:F0}");
                Console.WriteLine($"{scenario} chars={chars} | {string.Join(' ', Out)}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"{scenario} chars={chars} FAILED at {_stage}: {e.GetType().Name}: {e.Message} | {string.Join(' ', Out)}");
            }
            app.Shutdown();
        };
        _win.Show();
        app.Run();
        return 0;
    }
}
