using System;
using System.IO;
using System.Windows;
using ArchiveManager.Application.Services;
using ArchiveManager.Infrastructure.Data;
using ArchiveManager.UI.ViewModels;

namespace ArchiveManager;

/// <summary>
/// Application entry point. Wires up the (small, hand-rolled) composition root —
/// this app is intentionally too small to need a full DI container.
/// </summary>
public partial class App : System.Windows.Application
{
    public static string AppRootDirectory { get; private set; } = AppContext.BaseDirectory;
    public static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure the portable folder structure exists next to the exe:
        //   Data/, Config/, Backup/, Logs/
        Directory.CreateDirectory(Path.Combine(AppRootDirectory, "Data"));
        Directory.CreateDirectory(Path.Combine(AppRootDirectory, "Config"));
        Directory.CreateDirectory(Path.Combine(AppRootDirectory, "Backup"));
        Directory.CreateDirectory(Path.Combine(AppRootDirectory, "Logs"));

        try
        {
            Services = AppServices.Create(AppRootDirectory);
        }
        catch (Exception ex)
        {
            // Database unavailable at launch — edge case #16 in the spec.
            System.Windows.MessageBox.Show(
                $"امکان اتصال به پایگاه داده وجود ندارد.\n\nجزئیات فنی: {ex.Message}",
                "خطای راه‌اندازی",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        var mainWindow = new UI.Views.MainWindow
        {
            DataContext = new MainViewModel(Services)
        };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>
/// Composition root: creates and owns every long-lived service.
/// Intentionally a plain class instead of a DI framework — this app has
/// ~10 services, a framework would add indirection without real benefit.
/// </summary>
public sealed class AppServices : IDisposable
{
    public SettingsService Settings { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }
    public DbInitializer DbInitializer { get; }
    public ActivityLogService ActivityLog { get; }
    public ArchiveTreeService ArchiveTree { get; }
    public FileTypeMapperService FileTypes { get; }
    public PersianTextNormalizer TextNormalizer { get; }
    public FilenameGenerationService FilenameGenerator { get; }
    public DuplicateDetectionService DuplicateDetection { get; }
    public DocumentRegistrationService DocumentRegistration { get; }
    public SearchService Search { get; }
    public StatisticsService Statistics { get; }
    public BackupService Backup { get; }
    public IntegrityCheckService Integrity { get; }
    public LocalizationService Localization { get; }
    public EntityTransitionService EntityTransition { get; }
    public CompanyProfileService CompanyProfiles { get; }

    private AppServices(
        SettingsService settings,
        SqliteConnectionFactory connectionFactory,
        DbInitializer dbInitializer,
        ActivityLogService activityLog,
        ArchiveTreeService archiveTree,
        FileTypeMapperService fileTypes,
        PersianTextNormalizer textNormalizer,
        FilenameGenerationService filenameGenerator,
        DuplicateDetectionService duplicateDetection,
        DocumentRegistrationService documentRegistration,
        SearchService search,
        StatisticsService statistics,
        BackupService backup,
        IntegrityCheckService integrity,
        LocalizationService localization,
        EntityTransitionService entityTransition,
        CompanyProfileService companyProfiles)
    {
        Settings = settings;
        ConnectionFactory = connectionFactory;
        DbInitializer = dbInitializer;
        ActivityLog = activityLog;
        ArchiveTree = archiveTree;
        FileTypes = fileTypes;
        TextNormalizer = textNormalizer;
        FilenameGenerator = filenameGenerator;
        DuplicateDetection = duplicateDetection;
        DocumentRegistration = documentRegistration;
        Search = search;
        Statistics = statistics;
        Backup = backup;
        Integrity = integrity;
        Localization = localization;
        EntityTransition = entityTransition;
        CompanyProfiles = companyProfiles;
    }

    public static AppServices Create(string appRoot)
    {
        var configPath = Path.Combine(appRoot, "Config", "settings.json");
        var defaultConfigPath = Path.Combine(appRoot, "Config", "settings.default.json");
        var settings = SettingsService.LoadOrCreate(configPath, defaultConfigPath);

        var dbPath = Path.Combine(appRoot, "Data", "archive.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var dbInitializer = new DbInitializer(connectionFactory);
        dbInitializer.InitializeAndSeed();

        var activityLog = new ActivityLogService(connectionFactory);
        var fileTypes = new FileTypeMapperService();
        var textNormalizer = new PersianTextNormalizer();
        var archiveTree = new ArchiveTreeService(connectionFactory, settings, fileTypes);
        var filenameGenerator = new FilenameGenerationService(textNormalizer);
        var duplicateDetection = new DuplicateDetectionService();
        var documentRegistration = new DocumentRegistrationService(
            connectionFactory, settings, filenameGenerator, duplicateDetection, activityLog, fileTypes);
        var search = new SearchService(connectionFactory);
        var statistics = new StatisticsService(connectionFactory);
        var backup = new BackupService(appRoot, connectionFactory, settings, activityLog);
        var integrity = new IntegrityCheckService(connectionFactory, settings, activityLog);
        var localization = new LocalizationService(appRoot, settings.Current.Language);
        var entityTransition = new EntityTransitionService(connectionFactory, settings, activityLog);
        var companyProfiles = new CompanyProfileService(connectionFactory);

        return new AppServices(
            settings, connectionFactory, dbInitializer, activityLog, archiveTree, fileTypes,
            textNormalizer, filenameGenerator, duplicateDetection, documentRegistration,
            search, statistics, backup, integrity, localization, entityTransition, companyProfiles);
    }

    public void Dispose()
    {
        ConnectionFactory.Dispose();
    }
}
