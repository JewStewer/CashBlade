using System.Windows;
using System.Globalization;
using System.IO;
using System.Windows.Markup;
using System.Windows.Threading;
using Finora.Api;
using Finora.Data;
using Finora.Services;

namespace Finora;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LogStartup("Evergrove startup began.");

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        var culture = CultureInfo.GetCultureInfo("en-AU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        try
        {
            var shouldRunDatabaseInitializer = ShouldRunDatabaseInitializer();
            if (shouldRunDatabaseInitializer)
            {
                LogStartup("Database setup started.");
                DatabaseInitializer.Initialize();
                LogStartup("Database setup finished.");
            }
            else
            {
                LogStartup("Fast schema repair started.");
                DatabaseInitializer.RepairSchema();
                LogStartup("Fast schema repair finished.");
                LogStartup("Existing database found; skipped heavy startup database repair.");
            }
        }
        catch (Exception ex)
        {
            LogStartup($"Database setup failed: {ex}");
            MessageBox.Show($"Evergrove could not finish database setup, but it will still try to open.{Environment.NewLine}{ex.Message}");
        }

        // Start local Wi-Fi sync server
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var webAppRoot = Path.Combine(exeDir, "WebApp", "wwwroot");
            SyncServer.Start(Directory.Exists(webAppRoot) ? webAppRoot : null);
            LogStartup("Sync server started on port 5050.");
        }
        catch (Exception ex)
        {
            LogStartup($"Sync server failed to start: {ex.Message}");
        }

        // Start Supabase cloud sync (reads supabase.json next to exe)
        try
        {
            SupabaseSyncService.Start();
        }
        catch (Exception ex)
        {
            LogStartup($"Supabase sync failed to start: {ex.Message}");
        }

        LogStartup("Main window creation started.");
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        LogStartup("Main window shown.");

        _ = Task.Run(async () =>
        {
            var updater = new AppUpdateService();
            if (await updater.TryStartUpdateAsync())
            {
                await Dispatcher.InvokeAsync(Shutdown);
            }
        });
    }

    private static bool ShouldRunDatabaseInitializer()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var currentDatabase = Path.Combine(localAppData, "Cashglade", "cashglade.db");
        var legacyDatabase = Path.Combine(localAppData, "Finora", "finora.db");

        return !File.Exists(currentDatabase) && !File.Exists(legacyDatabase);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogStartup($"UI error: {e.Exception}");
        MessageBox.Show($"Evergrove hit a startup/display problem and logged it for repair.{Environment.NewLine}{e.Exception.Message}");
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogStartup($"Fatal error: {e.ExceptionObject}");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogStartup($"Background error: {e.Exception}");
        e.SetObserved();
    }

    private static void LogStartup(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cashglade");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Startup logging must never become the thing that stops startup.
        }
    }
}
