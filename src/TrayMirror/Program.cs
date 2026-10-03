using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TrayMirror.Core.Configuration;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Scheduling;
using TrayMirror.Diagnostics;
using TrayMirror.Interop;
using TrayMirror.Mirroring;
using TrayMirror.Shell;
using TrayMirror.Startup;
using TrayMirror.Tray;

namespace TrayMirror;

/// <summary>
/// The application entry point and the object graph that holds everything together.
/// </summary>
/// <remarks>
/// There is no App.xaml. Every window is borderless with no WPF content, so a XAML application
/// definition would add a generated entry point and a build step for nothing. The
/// <see cref="Application"/> instance still exists, because the dispatcher, the message pump and
/// <c>HwndSource</c> all depend on it.
/// </remarks>
internal static class Program
{
    private const string InstanceName = @"Local\traymirror-single-instance";

    /// <summary>Runs traymirror.</summary>
    /// <param name="args">
    /// Command-line arguments. <c>--probe</c> prints the tray topology of this machine and exits,
    /// which is what a bug report should include.
    /// </param>
    /// <returns>Zero on a clean exit.</returns>
    [STAThread]
    internal static int Main(string[] args)
    {
        int exitCode = 0;
        var thread = new Thread(() =>
        {
            exitCode = RealMain(args);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private static int RealMain(string[] args)
    {
        NativeMethods.EnsureDefaultDesktop();

        if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
        {
            return RunProbe();
        }

        // A second instance would add a second notification icon and register a second set of DWM
        // thumbnails against the same taskbar, which looks like a rendering bug rather than like
        // two copies running.
        using var single = new Mutex(initiallyOwned: true, InstanceName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            return 0;
        }

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        application.DispatcherUnhandledException += (s, e) =>
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "traymirror_crash.txt"), "Dispatcher exception: " + e.Exception.ToString() + Environment.NewLine);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "traymirror_crash.txt"), "AppDomain exception: " + e.ExceptionObject?.ToString() + Environment.NewLine);
        };
        application.Exit += (s, e) =>
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "traymirror_crash.txt"), $"Application.Exit: code {e.ApplicationExitCode}" + Environment.NewLine);
        };

        using var host = new ApplicationHost(application.Dispatcher);
        host.Start();

        int exitCode = application.Run();
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "traymirror_crash.txt"), $"Run returned: code {exitCode}" + Environment.NewLine);
        return exitCode;
    }

    private static int RunProbe()
    {
        NativeMethods.EnsureDefaultDesktop();
        string report = TrayProbeReport.Build();
        string path = Path.Combine(Path.GetTempPath(), "traymirror-probe.txt");

        try
        {
            File.WriteAllText(path, report);
        }
        catch (IOException)
        {
            // An unwritable temp directory is a machine problem, and the report is still printed
            // to the console below, so there is nothing here worth failing the command over.
        }

        // traymirror is a WinExe, so it has no console of its own. Attaching to the one that
        // launched it is what lets the report be piped or redirected like any other command
        // output. When there is no parent console, which is what a double-click looks like, the
        // attach fails harmlessly and the file on disk is the only copy.
        if (NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
        {
            Console.Out.WriteLine(report);
            Console.Out.WriteLine($"Written to {path}");
            Console.Out.Flush();
        }

        return 0;
    }
}

/// <summary>
/// Owns every long-lived object in the running application.
/// </summary>
/// <remarks>
/// Construction order matters and is enforced by the field order below: the message window has to
/// exist before anything that hooks its messages, and the controller has to be disposed before the
/// message window is destroyed so that thumbnails are unregistered while their destination windows
/// still exist.
/// </remarks>
internal sealed class ApplicationHost : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IDiagnosticsSink _log;
    private readonly string _configPath;
    private readonly MessageWindow _messageWindow;
    private readonly ShellWatcher _shellWatcher;
    private readonly MirrorController _controller;
    private readonly TrayIconHost _trayIcon;
    private readonly FileSystemWatcher? _configWatcher;
    private readonly DispatcherTimer _configReloadTimer;

    private TrayMirrorConfig _config;
    private bool _disposed;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApplicationHost"/> class.
    /// </summary>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    internal ApplicationHost(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        _dispatcher = dispatcher;

        _configPath = ConfigLoader.DefaultPath();
        _ = ConfigLoader.EnsureExists(_configPath);

        ConfigLoadResult loaded = ConfigLoader.Load(_configPath);
        _config = loaded.Config;

        _log = _config.DiagnosticsLog
            ? new FileDiagnosticsSink(FileDiagnosticsSink.DefaultPath())
            : NullDiagnosticsSink.Instance;

        foreach (string warning in loaded.Warnings)
        {
            _log.Write("config", warning);
        }

        _messageWindow = new MessageWindow("traymirror.messages");
        _shellWatcher = new ShellWatcher(_messageWindow, _log);
        _controller = new MirrorController(_dispatcher, _log)
        {
            Config = _config,
        };

        _trayIcon = new TrayIconHost(_messageWindow, _log)
        {
            StartWithWindowsChecked = AutostartRegistry.IsEnabled(),
        };

        _trayIcon.CommandInvoked += OnTrayCommand;
        _shellWatcher.Invalidated += OnInvalidated;

        _configReloadTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };

        _configReloadTimer.Tick += OnConfigReloadTick;
        _configWatcher = TryWatchConfig();

        ApplyAutostart(_config.StartWithWindows);
    }

    /// <summary>Builds the first set of mirrors and starts reacting to shell events.</summary>
    internal void Start()
    {
        _controller.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _configWatcher?.Dispose();
        _configReloadTimer.Stop();
        _configReloadTimer.Tick -= OnConfigReloadTick;

        _trayIcon.CommandInvoked -= OnTrayCommand;
        _shellWatcher.Invalidated -= OnInvalidated;

        _trayIcon.Dispose();
        _shellWatcher.Dispose();
        _controller.Dispose();
        _messageWindow.Dispose();
    }

    private static void Open(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnInvalidated(object? sender, RebuildReasons reason)
    {
        if (reason == RebuildReasons.TaskbarCreated)
        {
            // Explorer forgot every notification icon when it restarted, traymirror's included.
            _trayIcon.Readd();
        }

        _controller.RequestRebuild(reason);
    }

    private void OnTrayCommand(object? sender, TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.ReloadConfig:
                ReloadConfig();
                break;

            case TrayCommand.ToggleStartWithWindows:
                ApplyAutostart(!_trayIcon.StartWithWindowsChecked);
                break;

            case TrayCommand.OpenConfigFile:
                _ = ConfigLoader.EnsureExists(_configPath);
                Open(_configPath);
                break;

            case TrayCommand.OpenDiagnosticsLog:
                Open(FileDiagnosticsSink.DefaultPath());
                break;

            case TrayCommand.Exit:
                Dispose();
                Application.Current?.Shutdown();
                break;

            case TrayCommand.None:
            default:
                break;
        }
    }

    private void ApplyAutostart(bool enabled)
    {
        string? executable = Environment.ProcessPath;
        if (executable is null)
        {
            return;
        }

        if (AutostartRegistry.Apply(enabled, executable, _log))
        {
            _trayIcon.StartWithWindowsChecked = enabled;
        }
    }

    private void ReloadConfig()
    {
        ConfigLoadResult loaded = ConfigLoader.Load(_configPath);

        foreach (string warning in loaded.Warnings)
        {
            _log.Write("config", warning);
        }

        _config = loaded.Config;
        _controller.Config = _config;

        if (_config.DiagnosticsLog != (_log is FileDiagnosticsSink))
        {
            // The sink is chosen once at startup, so a change here needs a restart to take effect.
            // Saying so in the log is more use than silently ignoring it.
            _log.Write("config", "The diagnosticsLog setting changed. Restart traymirror for it to take effect.");
        }
    }

    private FileSystemWatcher? TryWatchConfig()
    {
        string? directory = Path.GetDirectoryName(_configPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var watcher = new FileSystemWatcher(directory, ConfigLoader.FileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };

        watcher.Changed += OnConfigFileChanged;
        watcher.Created += OnConfigFileChanged;
        watcher.Renamed += OnConfigFileChanged;
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Arrives on a file-system watcher thread, and a single save raises several events. The
        // timer coalesces them and moves the reload onto the UI thread, where the controller lives.
        _ = _dispatcher.InvokeAsync(
            () =>
            {
                _configReloadTimer.Stop();
                _configReloadTimer.Start();
            },
            DispatcherPriority.Background);
    }

    private void OnConfigReloadTick(object? sender, EventArgs e)
    {
        _configReloadTimer.Stop();
        ReloadConfig();
    }
}
