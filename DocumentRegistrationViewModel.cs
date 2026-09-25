using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;
using Microsoft.Win32;

namespace ArchiveManager.UI.ViewModels;

/// <summary>
/// Right-hand "ثبت و نام‌گذاری سند" panel — the app's centerpiece (spec §6/§7/§9).
/// </summary>
public sealed class DocumentRegistrationViewModel : ViewModelBase
{
    private readonly DocumentRegistrationService _registrationService;
    private readonly AppServices _services;

    private string? _sourceFilePath;
    private string? _destinationFolderAbsolute;
    private string? _destinationFolderRelative;
    private int? _companyId;

    private string _documentType = string.Empty;
    private string _subject = string.Empty;
    private int _year = PersianYearNow();
    private string _versionText = string.Empty;
    private string _companyName = string.Empty;
    private string _fileFormat = string.Empty;
    private string _filenamePreview = string.Empty;
    private string _validationMessage = string.Empty;
    private bool _isEditingExistingDocument;
    private int? _editingDocumentId;

    public ObservableCollection<string> DocumentTypes { get; } = new();
    public ObservableCollection<string> Subjects { get; } = new();
    public ObservableCollection<string> Formats { get; } = new()
        { "PDF", "Word", "Excel", "PowerPoint", "Access", "ZIP", "Image", "Video", "Text", "سایر" };

    public string DocumentType { get => _documentType; set { if (SetField(ref _documentType, value)) RefreshPreview(); } }
    public string Subject { get => _subject; set { if (SetField(ref _subject, value)) RefreshPreview(); } }
    public int Year { get => _year; set { if (SetField(ref _year, value)) RefreshPreview(); } }
    public string VersionText { get => _versionText; set { if (SetField(ref _versionText, value)) RefreshPreview(); } }
    public string CompanyName { get => _companyName; set => SetField(ref _companyName, value); }
    public string FileFormat { get => _fileFormat; set { if (SetField(ref _fileFormat, value)) RefreshPreview(); } }
    public string FilenamePreview { get => _filenamePreview; private set => SetField(ref _filenamePreview, value); }
    public string ValidationMessage { get => _validationMessage; private set => SetField(ref _validationMessage, value); }
    public bool HasSourceFile => !string.IsNullOrEmpty(_sourceFilePath);
    public string SourceFileDisplay => _sourceFilePath is null ? "فایلی انتخاب نشده" : Path.GetFileName(_sourceFilePath);

    public RelayCommand PickFileCommand { get; }
    public RelayCommand CopyNameCommand { get; }
    public RelayCommand RenameFileCommand { get; }
    public RelayCommand RegisterCommand { get; }

    public event Action<string>? DuplicateDetected;   // filename that collided
    public event Action<string>? OperationSucceeded;  // success message
    public event Action<string>? OperationFailed;      // error message

    public DocumentRegistrationViewModel(AppServices services)
    {
        _services = services;
        _registrationService = services.DocumentRegistration;

        PickFileCommand = new RelayCommand(PickFile);
        CopyNameCommand = new RelayCommand(CopyName, () => !string.IsNullOrEmpty(FilenamePreview));
        RenameFileCommand = new RelayCommand(() => Commit(isRename: true), CanCommit);
        RegisterCommand = new RelayCommand(() => Commit(isRename: false), CanCommit);

        LoadLookupLists();
    }

    private void LoadLookupLists()
    {
        using var connection = _services.ConnectionFactory.CreateOpenConnection();

        void FillFrom(string sql, ObservableCollection<string> target)
        {
            target.Clear();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) target.Add(reader.GetString(0));
        }

        FillFrom("SELECT Name FROM DocumentTypes WHERE IsActive = 1 ORDER BY Name;", DocumentTypes);
        FillFrom("SELECT Name FROM DocumentSubjects WHERE IsActive = 1 ORDER BY Name;", Subjects);
    }

    /// <summary>Called by MainViewModel when the operator selects a company/folder
    /// in the tree, or clicks "＋ ثبت سند جدید" — starts a fresh, blank form.</summary>
    public void StartNewRegistration(int companyId, string companyDisplayName, string destinationFolderAbsolute, string destinationFolderRelative)
    {
        _isEditingExistingDocument = false;
        _editingDocumentId = null;
        _companyId = companyId;
        _destinationFolderAbsolute = destinationFolderAbsolute;
        _destinationFolderRelative = destinationFolderRelative;
        _sourceFilePath = null;

        CompanyName = companyDisplayName;
        DocumentType = string.Empty;
        Subject = string.Empty;
        Year = PersianYearNow();
        VersionText = string.Empty;
        FileFormat = string.Empty;

        OnPropertyChanged(nameof(HasSourceFile));
        OnPropertyChanged(nameof(SourceFileDisplay));
        RefreshPreview();
    }

    /// <summary>Called from the file table's "ثبت/به‌روزرسانی اطلاعات" context menu
    /// action — pre-fills every field from the existing registration record.</summary>
    public void StartEditExisting(FileRowViewModel file, Document document, int companyId, string companyDisplayName,
        string destinationFolderAbsolute, string destinationFolderRelative)
    {
        _isEditingExistingDocument = true;
        _editingDocumentId = document.DocumentId;
        _companyId = companyId;
        _destinationFolderAbsolute = destinationFolderAbsolute;
        _destinationFolderRelative = destinationFolderRelative;
        _sourceFilePath = file.AbsolutePath;

        CompanyName = companyDisplayName;
        DocumentType = document.DocumentTypeName ?? string.Empty;
        Subject = document.SubjectName ?? string.Empty;
        Year = document.Year;
        VersionText = document.Version?.ToString("D2") ?? string.Empty;
        FileFormat = document.FormatLabel ?? string.Empty;

        OnPropertyChanged(nameof(HasSourceFile));
        OnPropertyChanged(nameof(SourceFileDisplay));
        RefreshPreview();
    }

    private void PickFile(object? _)
    {
        var dialog = new OpenFileDialog { Title = "انتخاب فایل" };
        if (dialog.ShowDialog() == true)
        {
            _sourceFilePath = dialog.FileName;
            var ext = Path.GetExtension(dialog.FileName).TrimStart('.');
            var (label, _) = _services.FileTypes.Resolve(dialog.FileName);
            FileFormat = label == "سایر" ? "سایر" : label;

            OnPropertyChanged(nameof(HasSourceFile));
            OnPropertyChanged(nameof(SourceFileDisplay));
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        if (string.IsNullOrWhiteSpace(DocumentType) || string.IsNullOrWhiteSpace(FileFormat) || Year <= 0)
        {
            FilenamePreview = string.Empty;
            return;
        }

        int? version = null;
        if (!string.IsNullOrWhiteSpace(VersionText) && int.TryParse(VersionText, out var v) && v > 0)
        {
            version = v;
        }

        var extension = ExtensionForFormat(FileFormat);
        var parts = new FilenameParts(DocumentType, Subject, Year, CompanyName, version, extension);
        FilenamePreview = _services.FilenameGenerator.Generate(parts);
        ValidationMessage = string.Empty;
        RegisterCommand.RaiseCanExecuteChanged();
        RenameFileCommand.RaiseCanExecuteChanged();
    }

    private bool CanCommit(object? _) =>
        HasSourceFile && !string.IsNullOrWhiteSpace(DocumentType) && !string.IsNullOrWhiteSpace(FileFormat)
        && Year > 0 && _destinationFolderAbsolute is not null && _companyId is not null;

    private void Commit(bool isRename)
    {
        if (!CanCommit(null))
        {
            ValidationMessage = "لطفاً فیلدهای الزامی (نوع سند، سال، فرمت فایل) و فایل را تکمیل کنید.";
            return;
        }

        int? version = null;
        if (!string.IsNullOrWhiteSpace(VersionText) && int.TryParse(VersionText, out var v) && v > 0) version = v;

        var request = new RegistrationRequest(
            SourceFilePath: _sourceFilePath!,
            DestinationFolderAbsolutePath: _destinationFolderAbsolute!,
            DestinationFolderRelativePath: _destinationFolderRelative!,
            CompanyId: _companyId!.Value,
            DocumentTypeName: DocumentType,
            SubjectName: string.IsNullOrWhiteSpace(Subject) ? null : Subject,
            Year: Year,
            Version: version,
            Extension: ExtensionForFormat(FileFormat),
            CompanyDisplayName: CompanyName,
            OperatorId: null);

        var result = (isRename && _isEditingExistingDocument && _editingDocumentId is not null)
            ? _registrationService.Rename(_editingDocumentId.Value, _sourceFilePath!, request)
            : _registrationService.Register(request);

        if (result.RequiresDuplicateResolution)
        {
            DuplicateDetected?.Invoke(result.GeneratedFileName ?? FilenamePreview);
            return;
        }

        if (result.Succeeded)
        {
            OperationSucceeded?.Invoke(isRename ? "نام فایل با موفقیت تغییر کرد." : "سند با موفقیت ثبت شد.");
        }
        else
        {
            OperationFailed?.Invoke(result.ErrorMessage ?? "عملیات ناموفق بود.");
        }
    }

    /// <summary>Called by the duplicate-resolution dialog when the operator
    /// picks "ایجاد نسخه جدید" — auto-computes and applies the next free version.</summary>
    public void ResolveWithNewVersion()
    {
        if (_destinationFolderAbsolute is null) return;
        var extension = ExtensionForFormat(FileFormat);
        var baseParts = new FilenameParts(DocumentType, Subject, Year, CompanyName, null, extension);
        var baseName = _services.FilenameGenerator.GenerateBaseName(baseParts);
        var nextVersion = _services.DuplicateDetection.NextFreeVersion(_destinationFolderAbsolute, baseName, extension);
        VersionText = nextVersion.ToString("D2");
        Commit(isRename: false);
    }

    private void CopyName(object? _)
    {
        if (!string.IsNullOrEmpty(FilenamePreview))
        {
            System.Windows.Clipboard.SetText(FilenamePreview);
        }
    }

    private static string ExtensionForFormat(string formatLabel) => formatLabel switch
    {
        "PDF" => "pdf",
        "Word" => "docx",
        "Excel" => "xlsx",
        "PowerPoint" => "pptx",
        "Access" => "accdb",
        "ZIP" => "zip",
        "Image" => "jpg",
        "Video" => "mp4",
        "Text" => "txt",
        _ => "dat"
    };

    /// <summary>ASSUMPTION: current Persian (Jalali) year is approximated as
    /// Gregorian year − 621 for the default-value convenience only; the operator
    /// can always override it. A production build should use a proper Persian
    /// calendar conversion (System.Globalization.PersianCalendar) — noted in README.</summary>
    private static int PersianYearNow() => new System.Globalization.PersianCalendar().GetYear(DateTime.Now);
}
