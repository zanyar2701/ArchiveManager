using System.Collections.ObjectModel;
using ArchiveManager.Application.Services;

namespace ArchiveManager.UI.ViewModels;

/// <summary>Backs the top-bar quick search overlay (spec §10).</summary>
public sealed class SearchViewModel : ViewModelBase
{
    private readonly SearchService _searchService;
    private string _queryText = string.Empty;
    private bool _hasResults;

    public ObservableCollection<Domain.Models.Document> Results { get; } = new();

    public string QueryText
    {
        get => _queryText;
        set { if (SetField(ref _queryText, value)) RunSearch(); }
    }

    public bool HasResults { get => _hasResults; private set => SetField(ref _hasResults, value); }

    public event System.Action<Domain.Models.Document>? ResultActivated;
    public RelayCommand ActivateResultCommand { get; }

    public SearchViewModel(SearchService searchService)
    {
        _searchService = searchService;
        ActivateResultCommand = new RelayCommand(doc => ResultActivated?.Invoke((Domain.Models.Document)doc!));
    }

    private void RunSearch()
    {
        Results.Clear();
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            HasResults = false;
            return;
        }

        var filters = new SearchFilters(FreeText: QueryText);
        foreach (var doc in _searchService.Search(filters, maxResults: 30))
        {
            Results.Add(doc);
        }
        HasResults = Results.Count > 0;
    }
}
