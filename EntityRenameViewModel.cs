using System;
using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;

namespace ArchiveManager.UI.ViewModels;

/// <summary>
/// Backs the standalone "تغییر نام" dialog — renames an entity WITHOUT
/// changing its stage (addendum: explicitly distinct from "انتقال پرونده").
/// </summary>
public sealed class EntityRenameViewModel : ViewModelBase
{
    private readonly EntityTransitionService _transitionService;
    private readonly int _entityId;

    private string _newName;
    private string _reason = string.Empty;
    private bool _renamePhysicalFolder = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public string CurrentName { get; }

    public string NewName { get => _newName; set => SetField(ref _newName, value); }
    public string Reason { get => _reason; set => SetField(ref _reason, value); }
    public bool RenamePhysicalFolder { get => _renamePhysicalFolder; set => SetField(ref _renamePhysicalFolder, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public EntityRenameViewModel(EntityTransitionService transitionService, Company currentEntity)
    {
        _transitionService = transitionService;
        _entityId = currentEntity.CompanyId;
        CurrentName = currentEntity.Name;
        _newName = currentEntity.Name;

        ConfirmCommand = new RelayCommand(Confirm, () => !IsBusy && !string.IsNullOrWhiteSpace(NewName) && NewName.Trim() != CurrentName);
        CancelCommand = new RelayCommand(() => { DialogResult = false; RequestClose?.Invoke(); });
    }

    private void Confirm(object? _)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        var request = new RenameEntityRequest(
            EntityId: _entityId,
            NewName: NewName.Trim(),
            Reason: string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
            OperatorId: null,
            RenamePhysicalFolder: RenamePhysicalFolder);

        var result = _transitionService.RenameEntity(request);
        IsBusy = false;

        if (result.Succeeded)
        {
            DialogResult = true;
            RequestClose?.Invoke();
        }
        else
        {
            ErrorMessage = result.ErrorMessage ?? "تغییر نام ناموفق بود.";
        }
    }
}
