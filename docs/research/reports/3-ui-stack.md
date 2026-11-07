**Recommendation.** WPF on .NET 10, no NuGet packages, Fluent dictionaries, with all arithmetic in a child worker process. Stop sets a flag, then kills the worker after 150 ms. The WPF app has no reference to Natural.

End-to-end spike (real Fluent window, worker inside a 2,000,000-bit multiply, 3 runs): 583–585 frames in 4 s, median 7 ms, worst 13–21 ms, none over 34 ms. Stop to process exit 2–3 ms; next answer on the UI thread 92–104 ms.

**UI stacks.** Text cases use 1,000,000 random digits. Nothing was downloaded: Avalonia is ShareX's 12.0.5, WinUI is WSL Settings' App SDK 1.8, both code-only, no XAML compiler.

| | WinForms | WPF + Fluent | Avalonia 12 | WinUI 3 |
|---|---|---|---|---|
| Packages to restore | 0 | 0 | Yes | Yes |
| First render, ms (medians, alternating runs) | 269–322 | 572–699 | 841–1045; AOT 319–385 | 462 |
| TextBox wrapped: UI blocked | 23–24 s | 0.7–1.8 s | Over 120 s, killed | 6.2 s |
| TextBox, newline every 100: blocked / per key | 16 ms / 3–4 ms | 0.2 s / 11–20 ms | 1.3 s / 540–640 ms | 1.1 s / 28–49 ms |
| 1,000-row virtual list, ms: fill / jump median / worst | 17–45 / 14 / 71 | 59–110 / 11 / 42 | 122 / 17 / 37 | 77 / 21 / 30 |
| Custom digit view, random jump median | 7 ms | 2 ms | 16 ms (its frame clock) | Not built; needs Win2D |
| Framework-dependent publish | 0.2 MB | 0.2 MB | 22 MB | 105 MB |
| Smallest self-contained | 49 MB | 62 MB | 31 MB (AOT) | 231 MB (WSL Settings) |
| Trim and AOT | Refused, NETSDK1175 | Refused, NETSDK1168 | Both ran | Not tried |
| Dark theme | Builds clean; progress bar stays light | Dark and light rendered | Not inspected | Not inspected |
| Agent's look | PrintWindow, shown window | PNG with no window or message loop; UI Automation pressed 3 keys, read the box | RenderTargetBitmap, shown window | Not tried |

WPF is the only option with zero packages, no text case over 2 s, a window-free PNG, and prior art here (Studio). Avalonia ships best, but a 100,000-character run cost 5.4 s per keystroke. WinUI needs the App SDK, plus Win2D for custom drawing. WinForms is lightest, but freezes 24 s, and its dark mode is partial.

- No stock box shows a long number honestly. WPF "NoWrap" splits every 9,600 characters, WinForms multiline every 1,024, and WinForms single-line drew nothing (its 1.7 ms was a blank box). So list rows hold a preview and the value; the open number draws through a custom `DigitView` that shapes only visible lines.
- Previews stay cheap at 3.32 Mbit: `E20` 21–41 ms, hex float 5 ms.
- `ThemeMode` is build error WPF0001 ("for evaluation purposes only"). Merging `Fluent.Dark.xaml` by hand rendered the same page without it (dark tested, light not).

Reuse: `FloatReport` moves to the engine for the IEEE inspector. `FloatPanel` is the trap: it runs per keystroke on the UI thread, capped at 65,536 bits, where a multiply takes 1 s. From Studio take `PlotCanvas`, `--smoke`, `--screenshot`. `origin/Gemini` is 24 commits behind and recalculates on the UI thread; a scratch export builds, window not run. Take only its limb grid and field split from `ToIeeeBytes`.

**Threading.**

| | (a) Thread, checks between ops | (b) Child process | (c) Step points in Natural |
|---|---|---|---|
| What Stop does | Sets a flag; the op runs on | Flag, then kill; host respawns and re-seeds | Flag read each row; op throws |
| Stop latency | 1 s at 65,536 bits, 17.5 s at 262,144; projected 5 min at 1 Mbit, 47 min at a million digits | 2–47 ms to exit, tested at 2 Mbit; about 100 ms to next answer | 0.1–8.9 ms up to 1 Mbit |
| Cost | None | 18.5 MB idle; 0.19 against 0.14 ms a call; cold start 62–119 ms | Ratios 0.95–1.10, inside noise; 256-bit job +10–13% |
| Weak point | Cannot be forced: `Thread.Abort` throws, `Interrupt` ignored | Worker memory lost, so the host keeps values | Natural changes; one unguarded loop means no Stop |

Recommend (b): no Natural change, and Stop works whatever Natural is doing, including future code. Its stdio loop doubles as the agent's harness, and a stack overflow kills the worker, not the history. Keep (a)'s check inside the worker for soft stops. Keep preflight as a warning and memory refusal: about 15 ns a limb step, time ±3× here. Add (c) with order item 7, which rewrites those loops; it brings true progress inside one op. Until then one op's bar is elapsed over estimate.

Jobs are no Stop either: the `E` format of 30,000-digit 1/3 overran a 50 ms slice to 1.0 s, and at a million digits the first slice was still running at 5 minutes, when I killed it. TODO 5c's "cheap" needs revisiting. Integer `F0` kept to 50 ms.

Rejected: `ControlledExecution.Run` (16–55 ms, but SYSLIB0046 "may corrupt the process", and Natural locks its fives cache). GC doesn't favour (b): 2–6 ms pause over 6 s, no gen2.

Worker: reader thread, BelowNormal compute thread, text lines on stdio. 830,000 hex characters there and back: 14–25 ms. Stdin EOF ends it: gone within 110 ms of the parent's death, mid-multiply.

**Solution.**

```
Calc/  Calc.slnx, Directory.Build.props (NaturalRoot, default ..\Natural\)
  src/Calc.Protocol    net10.0  wire records; no Natural, no UI
  src/Calc.Engine      net10.0  parser, evaluator, cost model; Natural + Protocol
  src/Calc.Worker      net10.0 exe, calc-worker; stdio loop, agent CLI
  src/Calc.ViewModels  net10.0  UI state, commands; Protocol only
  src/Calc.App         net10.0-windows WPF; views, DigitView, bit grid, plot
  tests/  Engine (BigInteger oracle), Worker (real exe: kill, respawn, orphan), ViewModels (fake worker)
```

A scratch skeleton built: the worker lands beside the app, and `typeof(Natural.ApFloat)` in the app fails CS0246. Publishing the app alone drops the worker's DLLs; publishing both to one folder ran: 0.56 MB framework-dependent, 140 MB self-contained.

Reference Natural as a sibling ProjectReference through `NaturalRoot`: the only route that sees 9a/9b, since GitHub lacks 3 commits and 9b is uncommitted. Clones need both repos side by side. Move to a submodule once Natural is pushed. No package while Natural keeps changing.

**Risks.**
- A million digits can be held, not computed: multiply projects to 47 minutes, a non-integer's full decimal to 20+.
- Calc isn't a repo yet: Matthew's `git init`.
- Host and worker state can drift; each request should carry what it needs.

**Not verified.** Real high DPI (one 96-DPI screen; WPF read back PerMonitorV2 with a manifest, 192-DPI PNG only). The NuGet route for Avalonia and WinUI, their XAML compilers, `Avalonia.Headless`, MSIX. Avalonia and WinUI long text by eye. Avalonia publish figures exclude 5 MB of ANGLE. Multiply and format times at 1 Mbit and up are quadratic projections. Single-file with two exes, JSON framing, the slow speed phase, load beside Chess Bruteforcer, Narrator.

Natural and Calc untouched. Probes and PNGs: C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\ui\