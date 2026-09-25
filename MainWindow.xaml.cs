using System.Windows;
using System.Windows.Controls;
using ArchiveManager.UI.ViewModels;

namespace ArchiveManager.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void QuickSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsSearchOverlayOpen = true;
        }
    }

    /// <summary>
    /// WPF's TreeView.SelectedItem is read-only and cannot be bound directly in
    /// XAML, so the click-to-browse interaction (the tree's primary purpose) is
    /// wired here instead: forward the newly selected node to the ViewModel,
    /// which raises ArchiveTreeViewModel.NodeActivated and loads it into the
    /// center panel + registration form exactly as selecting it programmatically
    /// (e.g. from a search result or a context-menu action) already does.
    /// </summary>
    private void ArchiveTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is TreeNodeViewModel node)
        {
            vm.Tree.SelectedNode = node;
        }
    }
}
