using System.Windows;
using ArchiveManager.UI.ViewModels;

namespace ArchiveManager.UI.Views.Dialogs;

public partial class EntityRenameDialog : Window
{
    public EntityRenameDialog(EntityRenameViewModel viewModel)
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
