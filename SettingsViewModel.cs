using System.Windows;
using ArchiveManager.Application.Services;
using Microsoft.Win32;

namespace ArchiveManager.UI.ViewModels;

/// <summary>Backs the "تنظیمات" screen (spec §23/§19).</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly BackupService _backupService;
    private readonly LocalizationService _localization;

    private string _archiveRootPath;
    private string _language;
    private bool _usePersianDigits;

    public string ArchiveRootPath { get => _archiveRootPath; set => SetField(ref _archiveRootPath, value); }
    public string Language { get => _language; set => SetField(ref _language, value); }
    public bool UsePersianDigits { get => _usePersianDigits; set => SetField(ref _usePersianDigits, value); }

    public RelayCommand BrowseArchiveRootCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CreateBackupCommand { get; }
    public RelayCommand RestoreBackupCommand { get; }

    public event System.Action? SettingsSaved;
    public event System.Action<string>? BackupCreated;

    public SettingsViewModel(SettingsService settings, BackupService backupService, LocalizationService localization)
    {
        _settings = settings;
        _backupService = backupService;
        _localization = localization;

        _archiveRootPath = settings.Current.ArchiveRootPath;
        _language = settings.Current.Language;
        _usePersianDigits = settings.Current.UsePersianDigits;

        BrowseArchiveRootCommand = new RelayCommand(BrowseArchiveRoot);
        SaveCommand = new RelayCommand(Save);
        CreateBackupCommand = new RelayCommand(CreateBackup);
        RestoreBackupCommand = new RelayCommand(RestoreBackup);
    }

    private void BrowseArchiveRoot(object? _)
    {
        var dialog = new OpenFolderDialog { Title = "انتخاب پوشه ریشه بایگانی" };
        if (dialog.ShowDialog() == true)
        {
            ArchiveRootPath = dialog.FolderName;
        }
    }

    private void Save(object? _)
    {
        _settings.Current.ArchiveRootPath = ArchiveRootPath;
        _settings.Current.Language = Language;
        _settings.Current.UsePersianDigits = UsePersianDigits;
        _settings.Save();
        _localization.SwitchLanguage(Language);
        SettingsSaved?.Invoke();
    }

    private void CreateBackup(object? _)
    {
        var path = _backupService.CreateBackup();
        BackupCreated?.Invoke(path);
    }

    private void RestoreBackup(object? _)
    {
        var dialog = new OpenFileDialog { Title = "انتخاب فایل پشتیبان", Filter = "SQLite Database (*.db)|*.db" };
        if (dialog.ShowDialog() == true)
        {
            var confirm = MessageBox.Show(
                "بازیابی این پشتیبان، اطلاعات فعلی را جایگزین می‌کند. آیا ادامه می‌دهید؟",
                "تایید بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                _backupService.RestoreBackup(dialog.FileName);
                MessageBox.Show("لطفاً برنامه را مجدداً اجرا کنید تا داده‌های بازیابی‌شده بارگذاری شوند.",
                    "بازیابی انجام شد", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
