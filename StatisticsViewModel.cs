using System.Collections.Generic;
using ArchiveManager.Application.Services;

namespace ArchiveManager.UI.ViewModels;

/// <summary>Backs the "گزارش‌ها" (Reports) screen (spec §11). Strictly read-only.</summary>
public sealed class StatisticsViewModel : ViewModelBase
{
    private readonly StatisticsService _statisticsService;
    private ArchiveStatistics _stats = new();
    private List<(string FileName, int Count)> _duplicates = new();
    private List<EntityListRow> _entities = new();

    public ArchiveStatistics Stats { get => _stats; private set => SetField(ref _stats, value); }
    public List<(string FileName, int Count)> DuplicateFileNames { get => _duplicates; private set => SetField(ref _duplicates, value); }
    /// <summary>The "COMPANY LIST" table — entity-identity addendum.</summary>
    public List<EntityListRow> Entities { get => _entities; private set => SetField(ref _entities, value); }

    public RelayCommand RefreshCommand { get; }

    public StatisticsViewModel(StatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public void Refresh(object? _ = null)
    {
        Stats = _statisticsService.ComputeStatistics();
        DuplicateFileNames = _statisticsService.FindDuplicateFileNames();
        Entities = _statisticsService.ListEntities();
    }
}
