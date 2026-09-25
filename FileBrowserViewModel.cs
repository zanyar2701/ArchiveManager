using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ArchiveManager.Application.Services;
using ArchiveManager.Infrastructure.FileSystem;

namespace ArchiveManager.UI.ViewModels;

public sealed class FileBrowserViewModel : ViewModelBase
{
    private readonly FileTypeMapperService _fileTypes;
    private string _currentFolderTitle = string.Empty;
    private string _currentFolderPath = string.Empty;
    private string _breadcrumb = string.Empty;
    private string _localFilterText = string.Empty;
    private FileRowViewModel? _selectedFile;
    private string _sortColumn = "FileName";
    private bool _sortAscending = true;

    public ObservableCollection<FileRowViewModel> Files { get; } = new();
    public event Action? RegisterNewDocumentRequested;

    public string CurrentFolderTitle
    {
        get => _currentFolderTitle;
        private set => SetField(ref _currentFolderTitle, value);
    }

    public string CurrentFolderPath
    {
        get => _currentFolderPath;
        private set => SetField(ref _currentFolderPath, value);
    }

    public string Breadcrumb
    {
        get => _breadcrumb;
        private set => SetField(ref _breadcrumb, value);
    }

    public string LocalFilterText
    {
        get => _localFilterText;
        set { if (SetField(ref _localFilterText, value)) ApplyFilterAndSort(); }
    }

    public FileRowViewModel? SelectedFile
    {
        get => _selectedFile;
        set => SetField(ref _selectedFile, value);
    }

    public bool IsEmpty => Files.Count == 0;

    public RelayCommand RegisterNewDocumentCommand { get; }
    public RelayCommand SortByColumnCommand { get; }
    public RelayCommand OpenSelectedFileCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private System.Collections.Generic.List<FileRowViewModel> _allFilesInFolder = new();

    public FileBrowserViewModel(FileTypeMapperService fileTypes)
    {
        _fileTypes = fileTypes;
        RegisterNewDocumentCommand = new RelayCommand(() => RegisterNewDocumentRequested?.Invoke());
        SortByColumnCommand = new RelayCommand(col => SortBy((string)col!));
        OpenSelectedFileCommand = new RelayCommand(OpenSelected, () => SelectedFile is not null);
        RefreshCommand = new RelayCommand(() => LoadFolder(CurrentFolderPath, CurrentFolderTitle, Breadcrumb, RegisteredLookup));
    }

    private Func<string, (int? DocumentId, int? Year, DateTime? RegisteredAt, bool IsMissing)>? RegisteredLookup;

    /// <summary>
    /// Loads a folder's files from disk, augmented with registration metadata
    /// supplied by the caller (MainViewModel queries the DB for the folder's
    /// registered documents and passes a lookup by absolute path — keeps this
    /// ViewModel free of direct DB access, consistent with MVVM separation).
    /// </summary>
    public void LoadFolder(string absolutePath, string title, string breadcrumb,
        Func<string, (int? DocumentId, int? Year, DateTime? RegisteredAt, bool IsMissing)> registeredLookup)
    {
        CurrentFolderPath = absolutePath;
        CurrentFolderTitle = title;
        Breadcrumb = breadcrumb;
        RegisteredLookup = registeredLookup;

        _allFilesInFolder.Clear();

        if (!string.IsNullOrEmpty(absolutePath) && Directory.Exists(absolutePath))
        {
            foreach (var file in Directory.EnumerateFiles(absolutePath))
            {
                var (label, iconKey) = _fileTypes.Resolve(file);
                var (docId, year, registeredAt, isMissing) = registeredLookup(file);
                var info = new FileInfo(file);

                _allFilesInFolder.Add(new FileRowViewModel
                {
                    DocumentId = docId,
                    FileName = Path.GetFileName(file),
                    AbsolutePath = file,
                    TypeLabel = label,
                    IconKey = iconKey,
                    Year = year,
                    SizeBytes = info.Length,
                    RegisteredAt = registeredAt,
                    IsRegistered = docId is not null,
                    IsMissing = isMissing
                });
            }
        }

        ApplyFilterAndSort();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void SortBy(string column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        Files.Clear();
        var query = _allFilesInFolder.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(LocalFilterText))
        {
            var needle = LocalFilterText.Trim();
            query = query.Where(f =>
                f.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                f.TypeLabel.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                f.YearDisplay.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        query = _sortColumn switch
        {
            "FileName" => _sortAscending ? query.OrderBy(f => f.FileName) : query.OrderByDescending(f => f.FileName),
            "TypeLabel" => _sortAscending ? query.OrderBy(f => f.TypeLabel) : query.OrderByDescending(f => f.TypeLabel),
            "Year" => _sortAscending ? query.OrderBy(f => f.Year) : query.OrderByDescending(f => f.Year),
            "SizeBytes" => _sortAscending ? query.OrderBy(f => f.SizeBytes) : query.OrderByDescending(f => f.SizeBytes),
            "RegisteredAt" => _sortAscending ? query.OrderBy(f => f.RegisteredAt) : query.OrderByDescending(f => f.RegisteredAt),
            _ => query
        };

        foreach (var row in query) Files.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OpenSelected(object? _)
    {
        if (SelectedFile is null) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(SelectedFile.AbsolutePath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception)
        {
            // Surfaced by the View via a bound error property in a fuller implementation;
            // kept minimal here per spec §32 (never a raw stack trace to the operator).
        }
    }
}
