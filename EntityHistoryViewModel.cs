using System.Collections.ObjectModel;
using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;

namespace ArchiveManager.UI.ViewModels;

/// <summary>Backs the "تاریخچه پرونده" view — read-only chronological timeline.</summary>
public sealed class EntityHistoryViewModel : ViewModelBase
{
    public string EntityName { get; }
    public string ArchiveCode { get; }
    public ObservableCollection<EntityHistoryEvent> Events { get; } = new();

    public EntityHistoryViewModel(EntityTransitionService transitionService, Company entity)
    {
        EntityName = entity.Name;
        ArchiveCode = entity.ArchiveCode;
        foreach (var evt in transitionService.GetHistory(entity.CompanyId))
        {
            Events.Add(evt);
        }
    }
}
