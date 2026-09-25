using System.Windows;
using ArchiveManager.UI.ViewModels;

namespace ArchiveManager.UI.Views.Dialogs;

public partial class EntityHistoryDialog : Window
{
    public EntityHistoryDialog(EntityHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
