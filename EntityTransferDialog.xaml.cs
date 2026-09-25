using System.Windows;
using ArchiveManager.UI.ViewModels;

namespace ArchiveManager.UI.Views.Dialogs;

public partial class EntityTransferDialog : Window
{
    public EntityTransferDialog(EntityTransferViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () =>
        {
            DialogResult = viewModel.DialogResult;
            Close();
        };
    }
}
