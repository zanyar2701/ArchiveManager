using System;
using System.Collections.ObjectModel;
using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;

namespace ArchiveManager.UI.ViewModels;

public sealed class StageOption
{
    public int ArchiveLevelId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// Backs the "انتقال پرونده" ("تغییر مرحله / انتقال پرونده") dialog — the
/// entity-identity addendum's central new workflow. Fields mirror the
/// addendum's dialog spec exactly: نام فعلی, نوع هویت فعلی, مرحله فعلی,
/// مرحله مقصد, نوع هویت جدید, نام جدید, تاریخ انتقال, علت/توضیحات.
/// </summary>
public sealed class EntityTransferViewModel : ViewModelBase
{
    private readonly EntityTransitionService _transitionService;
    private readonly CompanyProfileService _companyProfileService;
    private readonly int _entityId;

    private string _newName;
    private EntityType _newEntityType;
    private StageOption? _selectedTargetStage;
    private string _reason = string.Empty;
    private string _previewText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public string CurrentName { get; }
    public string CurrentEntityTypeDisplay { get; }
    public string CurrentStageName { get; private set; } = "?";
    public DateTime TransferDate { get; } = DateTime.Now;

    public ObservableCollection<StageOption> TargetStages { get; } = new();
    public ObservableCollection<EntityType> EntityTypes { get; } = new() { EntityType.Person, EntityType.Company };

    public string NewName { get => _newName; set { if (SetField(ref _newName, value)) UpdatePreview(); } }
    public EntityType NewEntityType
    {
        get => _newEntityType;
        set { if (SetField(ref _newEntityType, value)) { UpdatePreview(); OnPropertyChanged(nameof(ShowCompanyProfileFields)); } }
    }
    public StageOption? SelectedTargetStage { get => _selectedTargetStage; set { if (SetField(ref _selectedTargetStage, value)) UpdatePreview(); } }
    public string Reason { get => _reason; set => SetField(ref _reason, value); }

    // --- Optional company registration info (addendum "COMPANY REGISTRATION
    // INFORMATION") — only shown/used when NewEntityType == Company; never
    // required for a Person in pre-growth. ---
    public bool ShowCompanyProfileFields => NewEntityType == EntityType.Company;
    public string NationalId { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string CompanyType { get; set; } = string.Empty;
    public string Ceo { get; set; } = string.Empty;
    public string ContactInformation { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string PreviewText { get => _previewText; private set => SetField(ref _previewText, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public EntityTransferViewModel(EntityTransitionService transitionService, CompanyProfileService companyProfileService,
        Company currentEntity, System.Collections.Generic.IEnumerable<ArchiveLevel> allStages)
    {
        _transitionService = transitionService;
        _companyProfileService = companyProfileService;
        _entityId = currentEntity.CompanyId;

        CurrentName = currentEntity.Name;
        CurrentEntityTypeDisplay = currentEntity.EntityType == EntityType.Person ? "شخص" : "شرکت";
        _newName = currentEntity.Name;
        _newEntityType = currentEntity.EntityType;

        foreach (var stage in allStages)
        {
            TargetStages.Add(new StageOption { ArchiveLevelId = stage.ArchiveLevelId, DisplayName = stage.FolderName });
            if (stage.ArchiveLevelId == currentEntity.ArchiveLevelId)
            {
                CurrentStageName = stage.FolderName;
            }
        }
        _selectedTargetStage = null;

        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(() => { DialogResult = false; RequestClose?.Invoke(); });

        UpdatePreview();
    }

    private bool CanConfirm(object? _) =>
        !IsBusy && SelectedTargetStage is not null && !string.IsNullOrWhiteSpace(NewName);

    private void UpdatePreview()
    {
        if (SelectedTargetStage is null)
        {
            PreviewText = string.Empty;
            return;
        }
        var typeText = NewEntityType == EntityType.Person ? "شخص" : "شرکت";
        PreviewText =
            $"پرونده «{CurrentName}» از {CurrentStageName} به {SelectedTargetStage.DisplayName} منتقل خواهد شد " +
            $"و نام پرونده به «{NewName}» ({typeText}) تغییر می‌کند.";
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private void Confirm(object? _)
    {
        if (SelectedTargetStage is null) return;
        IsBusy = true;
        ErrorMessage = string.Empty;

        var request = new StageTransferRequest(
            EntityId: _entityId,
            ToStageId: SelectedTargetStage.ArchiveLevelId,
            NewEntityType: NewEntityType,
            NewName: NewName.Trim(),
            Reason: string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
            OperatorId: null);

        var result = _transitionService.TransferStage(request);
        IsBusy = false;

        if (result.Succeeded)
        {
            // Optional company registration info is saved only when the entity
            // is (now) a Company and the operator actually filled something in —
            // never required, per addendum.
            if (NewEntityType == EntityType.Company && HasAnyCompanyProfileInput())
            {
                _companyProfileService.Save(new Domain.Models.CompanyProfile
                {
                    EntityId = _entityId,
                    NationalId = NullIfEmpty(NationalId),
                    RegistrationNumber = NullIfEmpty(RegistrationNumber),
                    CompanyType = NullIfEmpty(CompanyType),
                    Ceo = NullIfEmpty(Ceo),
                    ContactInformation = NullIfEmpty(ContactInformation),
                    Website = NullIfEmpty(Website)
                });
            }

            DialogResult = true;
            RequestClose?.Invoke();
        }
        else
        {
            ErrorMessage = result.ErrorMessage ?? "انتقال پرونده ناموفق بود.";
        }
    }

    private bool HasAnyCompanyProfileInput() =>
        !string.IsNullOrWhiteSpace(NationalId) || !string.IsNullOrWhiteSpace(RegistrationNumber) ||
        !string.IsNullOrWhiteSpace(CompanyType) || !string.IsNullOrWhiteSpace(Ceo) ||
        !string.IsNullOrWhiteSpace(ContactInformation) || !string.IsNullOrWhiteSpace(Website);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
