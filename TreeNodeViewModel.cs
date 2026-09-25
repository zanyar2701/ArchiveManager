using System.Collections.ObjectModel;
using ArchiveManager.Application.Services;

namespace ArchiveManager.UI.ViewModels;

public sealed class TreeNodeViewModel : ViewModelBase
{
    private bool _isExpanded;
    private bool _isSelected;

    public string DisplayName { get; }
    public string AbsolutePath { get; }
    public string RelativePath { get; }
    public bool IsCompany { get; }
    public int? CompanyId { get; }
    public Domain.Models.EntityType EntityType { get; }
    public string ArchiveCode { get; }
    public string EntityTypeDisplay => EntityType == Domain.Models.EntityType.Person ? "شخص" : "شرکت";
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public TreeNodeViewModel(ArchiveTreeNode node, bool startExpanded = false)
    {
        DisplayName = node.DisplayName;
        AbsolutePath = node.AbsolutePath;
        RelativePath = node.RelativePath;
        IsCompany = node.IsCompany;
        CompanyId = node.CompanyId;
        EntityType = node.EntityType;
        ArchiveCode = node.ArchiveCode;
        _isExpanded = startExpanded;

        foreach (var child in node.Children)
        {
            Children.Add(new TreeNodeViewModel(child, startExpanded: false));
        }
    }
}
