using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;
using ArchiveManager.UI.Views.Dialogs;

namespace ArchiveManager.UI.ViewModels;

public enum MainTab { Archive, Reports, Settings, Guide }

/// <summary>
/// Top-level composition: owns the tree/browser/registration child VMs,
/// wires their events together, and tracks which top-nav tab is active.
/// This is the "AppShell" ViewModel referenced in the spec's component list.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private MainTab _activeTab = MainTab.Archive;
    private string _documentCountDisplay = "تعداد اسناد: 0";
    private string _statusBreadcrumb = "مسیر: بایگانی شرکت‌ها";
    private bool _isSearchOverlayOpen;
    private string _pendingDuplicateFileName = string.Empty;
    private bool _isDuplicateDialogOpen;

    public ArchiveTreeViewModel Tree { get; }
    public FileBrowserViewModel Browser { get; }
    public DocumentRegistrationViewModel Registration { get; }
    public SearchViewModel Search { get; }
    public StatisticsViewModel Statistics { get; }
    public SettingsViewModel Settings { get; }
    public ArchiveGuideViewModel Guide { get; }

    public MainTab ActiveTab { get => _activeTab; set => SetField(ref _activeTab, value); }
    public string DocumentCountDisplay { get => _documentCountDisplay; private set => SetField(ref _documentCountDisplay, value); }
    public string StatusBreadcrumb { get => _statusBreadcrumb; private set => SetField(ref _statusBreadcrumb, value); }
    public bool IsSearchOverlayOpen { get => _isSearchOverlayOpen; set => SetField(ref _isSearchOverlayOpen, value); }
    public bool IsDuplicateDialogOpen { get => _isDuplicateDialogOpen; private set => SetField(ref _isDuplicateDialogOpen, value); }
    public string PendingDuplicateFileName { get => _pendingDuplicateFileName; private set => SetField(ref _pendingDuplicateFileName, value); }

    public RelayCommand GoToArchiveCommand { get; }
    public RelayCommand GoToReportsCommand { get; }
    public RelayCommand GoToSettingsCommand { get; }
    public RelayCommand GoToGuideCommand { get; }
    public RelayCommand GoHomeCommand { get; }
    public RelayCommand ToggleSearchOverlayCommand { get; }
    public RelayCommand ResolveDuplicateWithVersionCommand { get; }
    public RelayCommand CancelDuplicateCommand { get; }

    // --- Entity-identity commands (addendum): available from the tree's
    // context menu on a company/person node — see "USER INTERFACE" section. ---
    public RelayCommand TransferEntityCommand { get; }
    public RelayCommand RenameEntityCommand { get; }
    public RelayCommand ShowEntityHistoryCommand { get; }
    public RelayCommand ShowEntityInfoCommand { get; }
    public RelayCommand OpenEntityFolderCommand { get; }
    public RelayCommand RegisterDocumentForEntityCommand { get; }

    public MainViewModel(AppServices services)
    {
        _services = services;

        Tree = new ArchiveTreeViewModel(services.ArchiveTree);
        Browser = new FileBrowserViewModel(services.FileTypes);
        Registration = new DocumentRegistrationViewModel(services);
        Search = new SearchViewModel(services.Search);
        Statistics = new StatisticsViewModel(services.Statistics);
        Settings = new SettingsViewModel(services.Settings, services.Backup, services.Localization);
        Guide = new ArchiveGuideViewModel();

        Tree.NodeActivated += OnTreeNodeActivated;
        Browser.RegisterNewDocumentRequested += OnRegisterNewDocumentRequested;
        Registration.DuplicateDetected += OnDuplicateDetected;
        Registration.OperationSucceeded += OnRegistrationSucceeded;
        Registration.OperationFailed += OnRegistrationFailed;
        Search.ResultActivated += OnSearchResultActivated;

        GoToArchiveCommand = new RelayCommand(() => ActiveTab = MainTab.Archive);
        GoToReportsCommand = new RelayCommand(() => { Statistics.Refresh(); ActiveTab = MainTab.Reports; });
        GoToSettingsCommand = new RelayCommand(() => ActiveTab = MainTab.Settings);
        GoToGuideCommand = new RelayCommand(() => ActiveTab = MainTab.Guide);
        GoHomeCommand = new RelayCommand(() => { ActiveTab = MainTab.Archive; Tree.Reload(); UpdateDocumentCount(); });
        ToggleSearchOverlayCommand = new RelayCommand(() => IsSearchOverlayOpen = !IsSearchOverlayOpen);
        ResolveDuplicateWithVersionCommand = new RelayCommand(() => { IsDuplicateDialogOpen = false; Registration.ResolveWithNewVersion(); });
        CancelDuplicateCommand = new RelayCommand(() => IsDuplicateDialogOpen = false);

        TransferEntityCommand = new RelayCommand(node => OpenTransferDialog((TreeNodeViewModel)node!), node => node is TreeNodeViewModel { IsCompany: true });
        RenameEntityCommand = new RelayCommand(node => OpenRenameDialog((TreeNodeViewModel)node!), node => node is TreeNodeViewModel { IsCompany: true });
        ShowEntityHistoryCommand = new RelayCommand(node => OpenHistoryDialog((TreeNodeViewModel)node!), node => node is TreeNodeViewModel { IsCompany: true });
        ShowEntityInfoCommand = new RelayCommand(node => ShowEntityInfo((TreeNodeViewModel)node!), node => node is TreeNodeViewModel { IsCompany: true });
        OpenEntityFolderCommand = new RelayCommand(node => OpenEntityFolder((TreeNodeViewModel)node!), node => node is TreeNodeViewModel { IsCompany: true });
        RegisterDocumentForEntityCommand = new RelayCommand(node => RegisterDocumentForEntity((TreeNodeViewModel)node!), node => node is TreeNodeViewModel { IsCompany: true });

        if (!services.Settings.IsArchiveRootConfigured())
        {
            // First-run setup (spec §19/§30): prompt immediately rather than
            // showing a silently-empty tree.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    "مسیر ریشه بایگانی هنوز تنظیم نشده است. لطفاً از بخش «تنظیمات» آن را انتخاب کنید.",
                    "راه‌اندازی اولیه", MessageBoxButton.OK, MessageBoxImage.Information);
                ActiveTab = MainTab.Settings;
            });
        }
        else
        {
            UpdateDocumentCount();
        }
    }

    private void OnTreeNodeActivated(TreeNodeViewModel node)
    {
        if (!node.IsCompany)
        {
            // An archive-level folder — browse it, but registration stays disabled
            // until a company is selected (a document always belongs to a company).
            Browser.LoadFolder(node.AbsolutePath, node.DisplayName, $"مسیر: بایگانی > {node.DisplayName}", LookupRegistration);
            StatusBreadcrumb = $"مسیر: بایگانی > {node.DisplayName}";
            return;
        }

        Browser.LoadFolder(node.AbsolutePath, node.DisplayName, BuildBreadcrumb(node), LookupRegistration);
        StatusBreadcrumb = BuildBreadcrumb(node);

        var companyId = FindCompanyId(node.DisplayName, node.RelativePath);
        if (companyId is not null)
        {
            // Use the entity's actual logical Name (never the raw folder name,
            // which may carry a numeric ordering prefix like "001-") so the
            // registration panel and generated filenames stay correct.
            var entityName = LoadCompanyDomainModel(companyId.Value)?.Name ?? node.DisplayName;
            Registration.StartNewRegistration(companyId.Value, entityName, node.AbsolutePath, node.RelativePath);
        }
    }

    private void OnRegisterNewDocumentRequested()
    {
        // Re-arms the registration form for the currently open company folder.
        if (Tree.SelectedNode is { IsCompany: true } node)
        {
            var companyId = FindCompanyId(node.DisplayName, node.RelativePath);
            if (companyId is not null)
            {
                var entityName = LoadCompanyDomainModel(companyId.Value)?.Name ?? node.DisplayName;
                Registration.StartNewRegistration(companyId.Value, entityName, node.AbsolutePath, node.RelativePath);
            }
        }
    }

    private void OnDuplicateDetected(string fileName)
    {
        PendingDuplicateFileName = fileName;
        IsDuplicateDialogOpen = true;
    }

    private void OnRegistrationSucceeded(string message)
    {
        MessageBox.Show(message, "موفق", MessageBoxButton.OK, MessageBoxImage.Information);
        if (Tree.SelectedNode is not null) OnTreeNodeActivated(Tree.SelectedNode);
        UpdateDocumentCount();
    }

    private void OnRegistrationFailed(string message)
    {
        MessageBox.Show(message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnSearchResultActivated(Document document)
    {
        IsSearchOverlayOpen = false;
        // Locate and select the corresponding tree node, then load its folder.
        foreach (var level in Tree.RootNodes)
        {
            foreach (var company in level.Children)
            {
                if (string.Equals(company.DisplayName, document.CompanyName, StringComparison.Ordinal))
                {
                    level.IsExpanded = true;
                    Tree.SelectedNode = company;
                    return;
                }
            }
        }
    }

    private (int? DocumentId, int? Year, DateTime? RegisteredAt, bool IsMissing) LookupRegistration(string absolutePath)
    {
        using var connection = _services.ConnectionFactory.CreateOpenConnection();
        var archiveRoot = _services.Settings.Current.ArchiveRootPath;
        var relativePath = Path.GetRelativePath(archiveRoot, absolutePath);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DocumentId, Year, RegisteredAt, IsMissing FROM Documents WHERE RelativePath = $rel;";
        cmd.Parameters.AddWithValue("$rel", relativePath);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetInt32(0), reader.GetInt32(1), DateTime.Parse(reader.GetString(2)), reader.GetInt32(3) == 1);
        }
        return (null, null, null, false);
    }

    // --------------------- Entity-identity dialog wiring (addendum) ---------------------

    /// <summary>"ثبت سند" from the tree's context menu — selects that entity (which
    /// loads its folder into the center panel via the normal tree-selection flow)
    /// then arms a blank registration form for it, exactly as if the operator had
    /// clicked the node themselves and pressed "＋ ثبت سند جدید".</summary>
    private void RegisterDocumentForEntity(TreeNodeViewModel node)
    {
        Tree.SelectedNode = node; // triggers OnTreeNodeActivated -> loads folder + registration
        ActiveTab = MainTab.Archive;
    }

    private void OpenTransferDialog(TreeNodeViewModel node)
    {
        if (node.CompanyId is not int companyId) return;
        var entity = LoadCompanyDomainModel(companyId);
        if (entity is null) return;

        var dialogVm = new EntityTransferViewModel(_services.EntityTransition, _services.CompanyProfiles, entity, LoadAllArchiveLevels());
        var dialog = new EntityTransferDialog(dialogVm) { Owner = System.Windows.Application.Current.MainWindow };
        dialog.ShowDialog();

        if (dialogVm.DialogResult == true)
        {
            MessageBox.Show("پرونده با موفقیت منتقل شد.", "موفق", MessageBoxButton.OK, MessageBoxImage.Information);
            Tree.Reload();
            UpdateDocumentCount();
        }
    }

    private void OpenRenameDialog(TreeNodeViewModel node)
    {
        if (node.CompanyId is not int companyId) return;
        var entity = LoadCompanyDomainModel(companyId);
        if (entity is null) return;

        var dialogVm = new EntityRenameViewModel(_services.EntityTransition, entity);
        var dialog = new EntityRenameDialog(dialogVm) { Owner = System.Windows.Application.Current.MainWindow };
        dialog.ShowDialog();

        if (dialogVm.DialogResult == true)
        {
            MessageBox.Show("نام پرونده با موفقیت تغییر کرد.", "موفق", MessageBoxButton.OK, MessageBoxImage.Information);
            Tree.Reload();
        }
    }

    private void OpenHistoryDialog(TreeNodeViewModel node)
    {
        if (node.CompanyId is not int companyId) return;
        var entity = LoadCompanyDomainModel(companyId);
        if (entity is null) return;

        var dialogVm = new EntityHistoryViewModel(_services.EntityTransition, entity);
        var dialog = new EntityHistoryDialog(dialogVm) { Owner = System.Windows.Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    private void ShowEntityInfo(TreeNodeViewModel node)
    {
        if (node.CompanyId is not int companyId) return;
        var entity = LoadCompanyDomainModel(companyId);
        if (entity is null) return;

        var typeText = entity.EntityType == EntityType.Person ? "شخص" : "شرکت";
        var stageName = LoadAllArchiveLevels().Find(l => l.ArchiveLevelId == entity.ArchiveLevelId)?.FolderName ?? "?";
        var previousNames = LoadPreviousNames(companyId);
        var prevText = previousNames.Count > 0 ? string.Join("، ", previousNames) : "—";

        MessageBox.Show(
            $"کد پرونده: {entity.ArchiveCode}\nنام فعلی: {entity.Name}\nنوع: {typeText}\nمرحله: {stageName}\nنام‌های قبلی: {prevText}",
            "اطلاعات پرونده", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void OpenEntityFolder(TreeNodeViewModel node)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(node.AbsolutePath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"امکان باز کردن پوشه وجود ندارد.\n\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Company? LoadCompanyDomainModel(int companyId)
    {
        using var connection = _services.ConnectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT CompanyId, Name, ArchiveLevelId, FolderPath, PreviousLevelId, CreatedAt, EntityType, ArchiveCode, UpdatedAt
                             FROM Companies WHERE CompanyId = $id;";
        cmd.Parameters.AddWithValue("$id", companyId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new Company
        {
            CompanyId = reader.GetInt32(0),
            Name = reader.GetString(1),
            ArchiveLevelId = reader.GetInt32(2),
            FolderPath = reader.GetString(3),
            PreviousLevelId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            EntityType = !reader.IsDBNull(6) && reader.GetString(6) == "Person" ? EntityType.Person : EntityType.Company,
            ArchiveCode = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            UpdatedAt = reader.IsDBNull(8) ? reader.GetDateTime(5) : DateTime.Parse(reader.GetString(8))
        };
    }

    private List<ArchiveLevel> LoadAllArchiveLevels()
    {
        var levels = new List<ArchiveLevel>();
        using var connection = _services.ConnectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveLevelId, Code, Name, SortOrder FROM ArchiveLevels ORDER BY SortOrder;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            levels.Add(new ArchiveLevel { ArchiveLevelId = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), SortOrder = reader.GetInt32(3) });
        }
        return levels;
    }

    private List<string> LoadPreviousNames(int companyId)
    {
        var names = new List<string>();
        using var connection = _services.ConnectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name FROM EntityNameHistory WHERE EntityId = $id AND IsCurrent = 0 ORDER BY FromDate;";
        cmd.Parameters.AddWithValue("$id", companyId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private int? FindCompanyId(string companyName, string relativePath)
    {
        using var connection = _services.ConnectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CompanyId FROM Companies WHERE FolderPath = $path;";
        cmd.Parameters.AddWithValue("$path", relativePath);
        var result = cmd.ExecuteScalar();
        return result is null ? null : Convert.ToInt32(result);
    }

    private static string BuildBreadcrumb(TreeNodeViewModel companyNode)
    {
        var segments = companyNode.RelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "مسیر: بایگانی > " + string.Join(" > ", segments);
    }

    private void UpdateDocumentCount()
    {
        using var connection = _services.ConnectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Documents;";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        DocumentCountDisplay = $"تعداد اسناد: {count}";
    }
}
