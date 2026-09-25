using System;
using System.Collections.ObjectModel;
using ArchiveManager.Application.Services;

namespace ArchiveManager.UI.ViewModels;

public sealed class ArchiveTreeViewModel : ViewModelBase
{
    private readonly ArchiveTreeService _treeService;
    private TreeNodeViewModel? _selectedNode;

    public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = new();
    public event Action<TreeNodeViewModel>? NodeActivated; // fires on selection -> loads center panel

    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            var previous = _selectedNode;
            if (SetField(ref _selectedNode, value))
            {
                // Keep the visual TreeViewItem.IsSelected state (bound TwoWay in
                // MainWindow.xaml) in sync for programmatic selection too — a real
                // user click already sets this itself before SelectedItemChanged
                // fires, but a search-result jump or a context-menu action assigns
                // this property directly and needs the same visual feedback.
                if (previous is not null) previous.IsSelected = false;
                if (value is not null)
                {
                    value.IsSelected = true;
                    NodeActivated?.Invoke(value);
                }
            }
        }
    }

    public ArchiveTreeViewModel(ArchiveTreeService treeService)
    {
        _treeService = treeService;
        Reload();
    }

    public void Reload()
    {
        RootNodes.Clear();
        _treeService.SynchronizeWithDatabase();
        var root = _treeService.BuildTreeFromDisk();

        // The visual root "بایگانی شرکت‌ها" is shown expanded with its four
        // level children — matches the reference screenshot exactly.
        foreach (var level in root.Children)
        {
            RootNodes.Add(new TreeNodeViewModel(level, startExpanded: false));
        }
    }
}
