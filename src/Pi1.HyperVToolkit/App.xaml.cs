using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.Infrastructure.Capabilities;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Diagnostics;
using Pi1.HyperVToolkit.Infrastructure.Cluster;
using Pi1.HyperVToolkit.Dialogs;
using Pi1.HyperVToolkit.Infrastructure.Export;
using Pi1.HyperVToolkit.Infrastructure.HyperV;
using Pi1.HyperVToolkit.Infrastructure.Storage;
using Pi1.HyperVToolkit.Infrastructure.Paths;
using Pi1.HyperVToolkit.Infrastructure.Scope;
using Pi1.HyperVToolkit.Infrastructure.Security;
using Pi1.HyperVToolkit.Infrastructure.SystemInfo;
using Pi1.HyperVToolkit.ViewModels;
using Serilog;

namespace Pi1.HyperVToolkit;

/// <summary>
/// Application bootstrap: global exception handling, DI, file logging,
/// startup logging and shell creation. No business logic.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _services;
    private CancellationTokenSource? _startupCts;
    private string _logFilePath = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        base.OnStartup(e);

        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();

            _logFilePath = Path.Combine(paths.LogsDirectory, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    _logFilePath,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    encoding: Encoding.UTF8)
                .CreateLogger();

            var services = new ServiceCollection();
            ConfigureServices(services, paths);
            _services = services.BuildServiceProvider();

            LogStartup(
                _services.GetRequiredService<IAppInfoProvider>(),
                _services.GetRequiredService<IPrivilegeService>());

            var mainWindow = _services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();

            _startupCts = new CancellationTokenSource();
            var mainViewModel = (MainViewModel)mainWindow.DataContext;
            await mainViewModel.InitializeAsync(_startupCts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatálne zlyhanie pri štarte aplikácie.");
            Log.CloseAndFlush();
            MessageBox.Show(
                $"Aplikáciu sa nepodarilo spustiť.\n\n{ex.Message}\n\nPodrobnosti: {_logFilePath}",
                "π1 Hyper-V Toolkit",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _startupCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown racing an in-flight start; nothing to cancel.
        }

        _startupCts?.Dispose();
        (_services as IDisposable)?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services, IAppPaths paths)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddSerilog(dispose: true);
        });

        services.AddSingleton(paths);
        services.AddSingleton<IAppInfoProvider, AppInfoProvider>();
        services.AddSingleton<IPrivilegeService, PrivilegeService>();
        services.AddSingleton<IScopeService>(sp =>
            new ScopeService(sp.GetRequiredService<IAppInfoProvider>().MachineName));
        services.AddSingleton<ICapabilitySnapshot, CapabilitySnapshot>();
        services.AddSingleton<IScopePolicy>(sp =>
            new ScopePolicy(sp.GetRequiredService<IAppInfoProvider>().MachineName));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<ILocalizationService>(LocalizationService.Instance);
        services.AddSingleton<ISessionCache, SessionCache>();
        services.AddSingleton<ICapabilityProbeService, CapabilityProbeService>();
        services.AddSingleton<ICimQuerier, MmiCimQuerier>();
        services.AddSingleton<IVhdInspector, VhdInspector>();
        services.AddSingleton<IHyperVService, HyperVService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<IClusterInfoService, ClusterInfoService>();
        services.AddSingleton<ClusterEventReader>();
        services.AddSingleton<IClusterService, ClusterService>();
        services.AddSingleton<ISystemInformationService, SystemInformationService>();
        services.AddSingleton<ITargetNodeResolver, TargetNodeResolver>();
        services.AddSingleton<IExportDialogService, WindowsSaveFileDialogService>();
        services.AddSingleton<IExportFileWriter, DiskFileWriter>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<VmsViewModel>();
        services.AddSingleton<NodesViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<NetworkingViewModel>();
        services.AddSingleton<ClusterViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    private static void LogStartup(IAppInfoProvider info, IPrivilegeService privilege)
    {
        Log.Information("===== π1 Hyper-V Toolkit startup =====");
        Log.Information("Verzia: {Version}", info.ApplicationVersion);
        Log.Information("Počítač: {Machine}", info.MachineName);
        Log.Information("Používateľ: {User}", info.UserName);
        Log.Information("OS: {Os}", info.OsDescription);
        Log.Information("Architektúra procesu: {Arch}", info.ProcessArchitecture);
        Log.Information("Runtime: {Runtime}", info.RuntimeVersion);
        Log.Information("Oprávnenia: {Elevation}", privilege.ElevationLabel);
        Log.Information("Štart aplikácie…");
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nezachytaná výnimka vo vlákne používateľského rozhrania.");
        MessageBox.Show(
            $"Nastala neočakávaná chyba:\n\n{e.Exception.Message}\n\nPodrobnosti: {_logFilePath}",
            "π1 Hyper-V Toolkit",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nezachytaná výnimka v úlohe na pozadí.");
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "Nezachytaná výnimka domény aplikácie (ukončenie: {Terminating}).", e.IsTerminating);
        MessageBox.Show(
            $"Nastala kritická chyba:\n\n{ex?.Message}\n\nPodrobnosti: {_logFilePath}",
            "π1 Hyper-V Toolkit",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
