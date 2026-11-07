using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Throwaway WinForms probe. One scenario a process.
//   wfprobe <scenario> [chars] [timeoutSeconds] [pngPath]
// Scenarios: hello tb-single tb-nowrap tb-wrap tb-lines rtb list view facts
static class P
{
    static readonly List<string> Out = [];
    static string _stage = "start";
    static ProbeForm _form = null!;

    sealed class ProbeForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    sealed class DigitView : Control
    {
        private readonly string _text;
        private readonly int _perLine;
        private readonly Font _font = new("Consolas", 10F);
        public long FirstLine;
        public long LineCount => (_text.Length + _perLine - 1) / _perLine;

        public DigitView(string text, int perLine)
        {
            _text = text; _perLine = perLine;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            int y = 0, h = _font.Height;
            for (long l = FirstLine; l < LineCount && y < Height; l++, y += h)
            {
                int at = (int)(l * _perLine);
                TextRenderer.DrawText(e.Graphics, _text.AsSpan(at, Math.Min(_perLine, _text.Length - at)), _font, new Point(4, y), Color.Black,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    static void Png(Control c, string path)
    {
        using var bmp = new Bitmap(c.Width, c.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(c.Handle, hdc, 2);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(path);
    }

    static string Digits(int n, bool lines = false)
    {
        var r = new Random(42);
        var sb = new StringBuilder(n + n / 50);
        for (int i = 0; i < n; i++)
        {
            sb.Append((char)('0' + r.Next(10)));
            if (lines && i % 100 == 99) sb.Append("\r\n");
        }
        return sb.ToString();
    }

    static void Stage(string name, Action work)
    {
        _stage = name;
        var sw = Stopwatch.StartNew();
        work();
        Out.Add($"{name}={sw.Elapsed.TotalMilliseconds:F1}");
    }

    static void TextBoxScenario(int n, bool multi, bool wrap, bool lines, string? png)
    {
        string text = Digits(n, lines);
        var box = new TextBox { Font = new Font("Consolas", 10F), Dock = DockStyle.Fill, Multiline = multi, WordWrap = wrap };
        if (multi) box.ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both;
        box.MaxLength = 0;
        _form.Controls.Add(box);
        _form.Update();
        Stage("set", () => box.Text = text);
        Stage("paint", () => box.Refresh());
        if (png != null) Png(_form, png + "-start.png");
        Stage("end", () => { box.SelectionStart = box.TextLength; box.ScrollToCaret(); box.Refresh(); });
        if (png != null) Png(_form, png + "-end.png");
        Stage("key1", () => { box.SelectedText = "7"; box.Refresh(); });
        Stage("key2", () => { box.SelectedText = "8"; box.Refresh(); });
        Stage("home", () => { box.SelectionStart = 0; box.ScrollToCaret(); box.Refresh(); });
        Out.Add($"len={box.TextLength} line_of_end={box.GetLineFromCharIndex(box.TextLength)} line1_starts={box.GetFirstCharIndexFromLine(1)}");
    }

    static void RichScenario(int n)
    {
        string text = Digits(n);
        var box = new RichTextBox { Font = new Font("Consolas", 10F), Dock = DockStyle.Fill, WordWrap = true, MaxLength = int.MaxValue };
        _form.Controls.Add(box);
        _form.Update();
        Stage("set", () => box.Text = text);
        Stage("paint", () => box.Refresh());
        Stage("end", () => { box.SelectionStart = box.TextLength; box.ScrollToCaret(); box.Refresh(); });
        Stage("key1", () => { box.SelectedText = "7"; box.Refresh(); });
        Stage("key2", () => { box.SelectedText = "8"; box.Refresh(); });
        Out.Add($"len={box.TextLength}");
    }

    static void Mock(string png)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var page = new TabPage("Standard");
        tabs.TabPages.Add(page); tabs.TabPages.Add("Scientific"); tabs.TabPages.Add("Programmer"); tabs.TabPages.Add("IEEE");
        var expr = new TextBox { Text = "sqrt(2) * 3", Font = new Font("Consolas", 14F), Dock = DockStyle.Top };
        var keys = new TableLayoutPanel { ColumnCount = 4, RowCount = 4, Dock = DockStyle.Top, Height = 170 };
        for (int i = 0; i < 4; i++) { keys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); keys.RowStyles.Add(new RowStyle(SizeType.Percent, 25)); }
        foreach (string k in "7 8 9 / 4 5 6 * 1 2 3 - 0 . = +".Split(' '))
            keys.Controls.Add(new Button { Text = k, Dock = DockStyle.Fill });
        var bar = new ProgressBar { Value = 40, Dock = DockStyle.Top, Height = 10 };
        var stop = new Button { Text = "Stop", Dock = DockStyle.Top };
        var list = new ListBox { Dock = DockStyle.Fill };
        for (int i = 0; i < 6; i++) list.Items.Add($"#{i}  1.41421356237309504880168872420969807856967187537694…  ({(i + 1) * 1000} digits)");
        page.Controls.Add(list); page.Controls.Add(stop); page.Controls.Add(bar); page.Controls.Add(keys); page.Controls.Add(expr);
        _form.Controls.Add(tabs);
        _form.Refresh();
        Application.DoEvents();
        Png(_form, png);
    }

    static void ListScenario(int n, int rows)
    {
        string text = Digits(n);
        var list = new ListView { View = View.Details, VirtualMode = true, FullRowSelect = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 10F) };
        list.Columns.Add("result", 760);
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem($"#{e.ItemIndex}  {text.Substring(e.ItemIndex, 60)}…  ({n:N0} digits)");
        _form.Controls.Add(list);
        Stage("fill", () => { list.VirtualListSize = rows; list.Refresh(); });
        var r = new Random(7);
        var steps = new List<double>();
        _stage = "scroll";
        for (int i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            list.EnsureVisible(r.Next(rows));
            list.Refresh();
            steps.Add(sw.Elapsed.TotalMilliseconds);
        }
        steps.Sort();
        Out.Add($"jump_median={steps[steps.Count / 2]:F1}");
        Out.Add($"jump_max={steps[^1]:F1}");
    }

    static void ViewScenario(int n)
    {
        string text = Digits(n);
        var view = new DigitView(text, 100) { Dock = DockStyle.Fill };
        var bar = new VScrollBar { Dock = DockStyle.Right, Maximum = (int)Math.Min(view.LineCount, int.MaxValue) };
        Stage("show", () => { _form.Controls.Add(view); _form.Controls.Add(bar); _form.Refresh(); });
        var r = new Random(7);
        var steps = new List<double>();
        _stage = "scroll";
        for (int i = 0; i < 200; i++)
        {
            var sw = Stopwatch.StartNew();
            view.FirstLine = r.NextInt64(view.LineCount);
            view.Refresh();
            steps.Add(sw.Elapsed.TotalMilliseconds);
        }
        steps.Sort();
        Out.Add($"jump_median={steps[steps.Count / 2]:F1}");
        Out.Add($"jump_p99={steps[(int)(steps.Count * .99)]:F1}");
        Out.Add($"jump_max={steps[^1]:F1}");
    }

    [STAThread]
    static int Main(string[] args)
    {
        var started = Process.GetCurrentProcess().StartTime;
        string scenario = args.Length > 0 ? args[0] : "hello";
        int chars = args.Length > 1 ? int.Parse(args[1]) : 1_000_000;
        string? png = args.Length > 3 ? args[3] : null;
        ApplicationConfiguration.Initialize();
        if (scenario.EndsWith("-dark")) Application.SetColorMode(SystemColorMode.Dark);
        _form = new ProbeForm
        {
            Text = "calc ui probe", ClientSize = new Size(800, 600), ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(60, 60),
        };

        var watchdog = new Thread(() =>
        {
            Thread.Sleep(args.Length > 2 ? int.Parse(args[2]) * 1000 : 150_000);
            Console.WriteLine($"{scenario} chars={chars} TIMEOUT at stage {_stage} | {string.Join(' ', Out)}");
            Environment.Exit(3);
        }) { IsBackground = true };
        watchdog.Start();

        _form.Shown += (_, _) =>
        {
            _form.Refresh();
            Out.Add($"first_render={(DateTime.Now - started).TotalMilliseconds:F0}");
            try
            {
                switch (scenario)
                {
                    case "hello": case "hello-dark": break;
                    case "mock-dark": Mock(png!); break;
                    case "facts": Out.Add($"dpi={_form.DeviceDpi} mode={Application.HighDpiMode}"); break;
                    case "tb-single": TextBoxScenario(chars, false, false, false, png); break;
                    case "tb-nowrap": TextBoxScenario(chars, true, false, false, png); break;
                    case "tb-wrap": TextBoxScenario(chars, true, true, false, png); break;
                    case "tb-lines": TextBoxScenario(chars, true, false, true, png); break;
                    case "rtb": RichScenario(chars); break;
                    case "list": ListScenario(chars, 1000); break;
                    case "view": ViewScenario(chars); break;
                }
                using var me = Process.GetCurrentProcess();
                Out.Add($"private_mb={me.PrivateMemorySize64 / 1048576.0:F0}");
                Console.WriteLine($"{scenario} chars={chars} | {string.Join(' ', Out)}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"{scenario} chars={chars} FAILED at {_stage}: {e.GetType().Name}: {e.Message} | {string.Join(' ', Out)}");
            }
            Application.Exit();
        };
        Application.Run(_form);
        return 0;
    }
}
