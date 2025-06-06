using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Reflection;
using UniInject;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class SongSearchControl : INeedInjection, IInjectionFinishedListener
{
    [Inject]
    private Settings settings;

    [Inject]
    private NonPersistentSettings nonPersistentSettings;

    [Inject(UxmlName = R.UxmlNames.searchTextField)]
    private TextField searchTextField;

    [Inject(UxmlName = R.UxmlNames.searchTextFieldHint)]
    private Label searchTextFieldHint;

    [Inject(UxmlName = R.UxmlNames.searchPropertyButton)]
    private Button searchPropertyButton;

    [Inject(UxmlName = R.UxmlNames.filterActiveIcon)]
    private VisualElement filterActiveIcon;

    [Inject(UxmlName = R.UxmlNames.resetActiveFiltersButton)]
    private Button resetActiveFiltersButton;

    [Inject(UxmlName = R.UxmlNames.filterInactiveIcon)]
    private VisualElement filterInactiveIcon;

    [Inject(UxmlName = R.UxmlNames.searchPropertyDropdownOverlay)]
    private VisualElement searchPropertyDropdownOverlay;

    [Inject(UxmlName = R.UxmlNames.playlistDropdownField)]
    private DropdownField playlistDropdownField;

    [Inject(UxmlName = R.UxmlNames.artistPropertyToggle)]
    private Toggle artistPropertyToggle;

    [Inject(UxmlName = R.UxmlNames.titlePropertyToggle)]
    private Toggle titlePropertyToggle;

    [Inject(UxmlName = R.UxmlNames.genrePropertyToggle)]
    private Toggle genrePropertyToggle;

    [Inject(UxmlName = R.UxmlNames.tagPropertyToggle)]
    private Toggle tagPropertyToggle;

    [Inject(UxmlName = R.UxmlNames.yearPropertyToggle)]
    private Toggle yearPropertyToggle;

    [Inject(UxmlName = R.UxmlNames.editionPropertyToggle)]
    private Toggle editionPropertyToggle;

    [Inject(UxmlName = R.UxmlNames.languagePropertyToggle)]
    private Toggle languagePropertyToggle;

    [Inject(UxmlName = R.UxmlNames.lyricsPropertyToggle)]
    private Toggle lyricsPropertyToggle;

    [Inject(UxmlName = R.UxmlNames.searchExpressionIcon)]
    private VisualElement searchExpressionIcon;

    [Inject(UxmlName = R.UxmlNames.searchPropertyDropdownContainer)]
    private VisualElement searchPropertyDropdownContainer;

    [Inject]
    private SongSelectionPlaylistChooserControl playlistChooserControl;

    [Inject]
    private Injector injector;

    [Inject]
    private SongRouletteControl songRouletteControl;

    [Inject]
    private SongSelectSceneControl songSelectSceneControl;

    [Inject]
    private SongSelectFilterControl songSelectFilterControl;

    [Inject]
    private PlaylistManager playlistManager;

    [Inject]
    private SongSelectSceneInputControl songSelectSceneInputControl;

    private TooltipControl searchExpressionIconTooltipControl;

    public bool IsSearchPropertyDropdownVisible => searchPropertyDropdownOverlay.IsVisibleByDisplay();
    public bool IsSearching => !searchTextField.value.IsNullOrEmpty();

    private HashSet<ESearchProperty> searchProperties = new();

    private readonly Subject<SearchChangedEvent> searchChangedEventStream = new();
    public IObservable<SearchChangedEvent> SearchChangedEventStream => searchChangedEventStream;

    private readonly Subject<VoidEvent> submitEventStream = new();
    public IObservable<VoidEvent> SubmitEventStream => submitEventStream;

    public void OnInjectionFinished()
    {
        using IDisposable d = ProfileMarkerUtils.Auto("SongSearchControl.OnInjectionFinished");

        searchProperties = new HashSet<ESearchProperty>(settings.SearchProperties);
        searchTextField.RegisterValueChangedCallback(evt =>
        {
            searchChangedEventStream.OnNext(new SearchTextChangedEvent());
        });
        searchTextField.DisableParseEscapeSequences();
        searchTextField.RegisterCallback<NavigationSubmitEvent>(_ => SubmitSearch());
        new TextFieldHintControl(searchTextFieldHint);

        songSelectSceneInputControl.FuzzySearchText.Subscribe(newValue => searchTextFieldHint.SetVisibleByVisibility(newValue.IsNullOrEmpty()));

        searchExpressionIcon.HideByDisplay();
        nonPersistentSettings.IsSearchExpressionsEnabled.Subscribe(newValue => searchExpressionIcon.SetVisibleByDisplay(newValue));
        searchExpressionIconTooltipControl = new(searchExpressionIcon);

        // Apply last search expression if any
        if (!nonPersistentSettings.LastValidSearchExpression.Value.IsNullOrEmpty())
        {
            searchTextField.value = nonPersistentSettings.LastValidSearchExpression.Value;
        }

        HideSearchPropertyDropdownOverlay();
        searchPropertyButton.RegisterCallbackButtonTriggered(_ =>
        {
            if (IsSearchPropertyDropdownVisible)
            {
                HideSearchPropertyDropdownOverlay();
            }
            else
            {
                ShowSearchPropertyDropdownOverlay();
            }
        });
        VisualElementUtils.RegisterCallbackToHideByDisplayOnDirectClick(searchPropertyDropdownOverlay);

        filterActiveIcon.HideByDisplay();
        nonPersistentSettings.PlaylistName
            .Subscribe(_ => UpdateAnyFiltersActive());
        playlistManager.PlaylistChangedEventStream
            .Subscribe(_ => UpdateAnyFiltersActive());
        songSelectFilterControl.FiltersChangedEventStream
            .Subscribe(_ => UpdateAnyFiltersActive());
        resetActiveFiltersButton.RegisterCallbackButtonTriggered(_ => ResetActiveFilters());

        if (!nonPersistentSettings.ActiveSearchPropertyFilters.IsNullOrEmpty())
        {
            songSelectFilterControl.InitFilters();
        }

        RegisterToggleSearchPropertyCallback(artistPropertyToggle, ESearchProperty.Artist);
        RegisterToggleSearchPropertyCallback(titlePropertyToggle, ESearchProperty.Title);
        RegisterToggleSearchPropertyCallback(languagePropertyToggle, ESearchProperty.Language);
        RegisterToggleSearchPropertyCallback(genrePropertyToggle, ESearchProperty.Genre);
        RegisterToggleSearchPropertyCallback(tagPropertyToggle, ESearchProperty.Tags);
        RegisterToggleSearchPropertyCallback(editionPropertyToggle, ESearchProperty.Edition);
        RegisterToggleSearchPropertyCallback(yearPropertyToggle, ESearchProperty.Year);
        RegisterToggleSearchPropertyCallback(lyricsPropertyToggle, ESearchProperty.Lyrics);

        new AnchoredPopupControl(searchPropertyDropdownContainer, searchPropertyButton, Corner2D.BottomRight);
        new UseAvailableScreenHeightControl(searchPropertyDropdownContainer);

        songRouletteControl.EntryListChangedEventStream
            .Subscribe(_ => UpdateSearchTextFieldStyle());
        songSelectSceneControl.RunningSongRepositorySearches
            .Subscribe(_ => UpdateSearchTextFieldStyle());
    }

    private void UpdateSearchTextFieldStyle()
    {
        if (songRouletteControl.Entries.IsNullOrEmpty()
            && !GetRawSearchText().IsNullOrEmpty()
            && songSelectSceneControl.RunningSongRepositorySearches.Value <= 0)
        {
            // No more running searches and no results found, so highlight this in the UI.
            searchTextField.AddToClassList("noSearchResults");
        }
        else
        {
            searchTextField.RemoveFromClassList("noSearchResults");
        }
    }

    private void ResetActiveFilters()
    {
        playlistChooserControl.Reset();
        songSelectFilterControl.Reset();
        ResetSearchText();
    }

    private void UpdateAnyFiltersActive()
    {
        IPlaylist activePlaylist = playlistManager.GetPlaylistByName(nonPersistentSettings.PlaylistName.Value);
        bool isAnyFilterOrPlaylistActive = songSelectFilterControl.IsAnyFilterActive
                                           || (activePlaylist != null &&
                                               activePlaylist is not UltraStarAllSongsPlaylist);

        filterActiveIcon.SetVisibleByDisplay(isAnyFilterOrPlaylistActive);
        filterInactiveIcon.SetVisibleByDisplay(!isAnyFilterOrPlaylistActive);
    }

    public void SubmitSearch()
    {
        submitEventStream.OnNext(VoidEvent.instance);
    }

    public void ShowSearchPropertyDropdownOverlay()
    {
        searchPropertyDropdownOverlay.ShowByDisplay();
        playlistDropdownField.Focus();
    }

    public void HideSearchPropertyDropdownOverlay()
    {
        searchPropertyDropdownOverlay.HideByDisplay();
        searchPropertyButton.Focus();
    }

    public IReadOnlyCollection<SongMeta> GetFilteredSongMetas(IReadOnlyCollection<SongMeta> songMetas)
    {
        string searchExp = searchTextField.value;
        searchExpressionIcon.RemoveFromClassList("errorFontColor");
        searchExpressionIconTooltipControl.TooltipText = Translation.Get(R.Messages.songSelectScene_searchExpressionEnabled,
            "properties", GetAvailableSearchExpressionPropertiesCsv());
        if (nonPersistentSettings.IsSearchExpressionsEnabled.Value
            && !searchExp.IsNullOrEmpty())
        {
            try
            {
                List<SongMeta> searchExpSongMetas = songMetas.AsQueryable()
                    .Where(searchExp)
                    .ToList();
                nonPersistentSettings.LastValidSearchExpression.Value = searchExp;
                return searchExpSongMetas;
            }
            catch (Exception e)
            {
                Debug.Log($"Invalid search expression '{searchExp}': {e.Message}. Stack trace:\n{e.StackTrace}");
                searchExpressionIcon.AddToClassList("errorFontColor");
                searchExpressionIconTooltipControl.TooltipText = Translation.Get(R.Messages.songSelectScene_searchExpressionError,
                    "errorDetails", e.Message);
                return new List<SongMeta>();
            }
        }
        else
        {
            nonPersistentSettings.LastValidSearchExpression.Value = "";
        }

        // Ignore prefix for special search syntax
        string searchText = GetRawSearchText() != "#"
            ? GetSearchText()
            : "";
        if (searchText.IsNullOrEmpty()) {
            return songMetas;
        }

        // Split searchText at whitespaces and match each word individually
        string[] searchTexts = searchText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
        List<SongMeta> filteredSongs = songMetas
            .Where(songMeta => searchTexts.IsNullOrEmpty()
                               || searchTexts.AllMatch(searchWord => SongMetaMatchesSearchedProperties(songMeta, searchWord)))
            .ToList();
        return filteredSongs;
    }

    private string GetAvailableSearchExpressionPropertiesCsv()
    {
        return typeof(SongMeta).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(propertyName => propertyName)
            .JoinWith(", ");
    }

    private bool SongMetaMatchesSearchedProperties(SongMeta songMeta, string searchText)
    {
        if (songMeta == null)
        {
            return false;
        }

        if (searchText.IsNullOrEmpty())
        {
             return true;
        }

        // Lyrics are a special case because these first need to be loaded, which is costly
        foreach (ESearchProperty searchProperty in searchProperties.Except(new List<ESearchProperty> { ESearchProperty.Lyrics }))
        {
            string propertyValue = GetPropertyValue(songMeta, searchProperty);
            if (propertyValue.IsNullOrEmpty())
            {
                continue;
            }

            if (StringUtils.ContainsIgnoreCaseAndDiacritics(propertyValue, searchText))
            {
                return true;
            }
        }

        if (searchProperties.Contains(ESearchProperty.Lyrics)
            && SongMetaMatchesLyrics(songMeta, searchText))
        {
            return true;
        }

        return false;
    }

    private string GetPropertyValue(SongMeta songMeta, ESearchProperty searchProperty)
    {
        if (songMeta == null)
        {
            return "";
        }

        switch (searchProperty)
        {
            case ESearchProperty.Artist:
                return songMeta.Artist;
            case ESearchProperty.Title:
                return songMeta.Title;
            case ESearchProperty.Language:
                return songMeta.Language;
            case ESearchProperty.Genre:
                return songMeta.Genre;
            case ESearchProperty.Tags:
                return songMeta.Tag;
            case ESearchProperty.Edition:
                return songMeta.Edition;
            case ESearchProperty.Year:
                return songMeta.Year.ToString();
            default:
                throw new ArgumentOutOfRangeException(nameof(searchProperty), searchProperty, null);
        }
    }

    private bool SongMetaMatchesLyrics(SongMeta songMeta, string searchText)
    {
        if (songMeta == null)
        {
            return false;
        }

        if (searchText.IsNullOrEmpty())
        {
            return true;
        }

        // TODO: Implement search on separate thread and concurrent update of search result.
        return songMeta.Voices
            .Select(voice => SongMetaUtils.GetLyrics(voice)
                // The character '~' is often used in UltraStar files to indicate a change of pitch during the same syllable.
                // Thus, it should be ignored when searching in lyrics.
                .Replace("~", ""))
            .Any(lyrics => StringUtils.ContainsIgnoreCaseAndDiacritics(lyrics, searchText));
    }

    public string GetRawSearchText()
    {
        return searchTextField.value;
    }

    public string GetSearchText()
    {
        return GetRawSearchText().TrimStart().ToLowerInvariant();
    }

    public void FocusSearchTextField()
    {
        searchTextField.Focus();
    }

    public void AddSearchProperty(ESearchProperty searchProperty)
    {
        searchProperties.Add(searchProperty);
        settings.SearchProperties = searchProperties.ToList();
        searchChangedEventStream.OnNext(new SearchPropertyChangedEvent());
    }

    public void RemoveSearchProperty(ESearchProperty searchProperty)
    {
        searchProperties.Remove(searchProperty);
        settings.SearchProperties = searchProperties.ToList();
        searchChangedEventStream.OnNext(new SearchPropertyChangedEvent());
    }

    public void SetSearchText(string newValue)
    {
        searchTextField.value = newValue;
    }

    public SongSelectEntry GetFuzzySearchMatch(string searchTextNoWhitespace)
    {
        string GetEntryTitle(SongSelectEntry songSelectEntry)
        {
            if (songSelectEntry is SongSelectSongEntry songEntry)
            {
                return songEntry.SongMeta.Title;
            }
            else if (songSelectEntry is SongSelectFolderEntry folderEntry)
            {
                return folderEntry.DirectoryInfo.Name;
            }
            else
            {
                return "";
            }
        }

        string GetEntryArtist(SongSelectEntry songSelectEntry)
        {
            if (songSelectEntry is SongSelectSongEntry songEntry)
            {
                return songEntry.SongMeta.Artist;
            }
            else
            {
                return "";
            }
        }

        if (searchTextNoWhitespace.Length == 1)
        {
            SongSelectEntry matchingEntry = GetFirstSongOfCurrentOrderThatStartsWith(searchTextNoWhitespace);
            return matchingEntry;
        }

        // Search title that starts with the text
        SongSelectEntry titleStartsWithMatch = songRouletteControl.Find(it =>
        {
            string titleNoWhitespace = GetEntryTitle(it).Replace(" ", "");
            return StringUtils.StartsWithIgnoreCaseAndDiacritics(titleNoWhitespace, searchTextNoWhitespace);
        });
        if (titleStartsWithMatch != null)
        {
            return titleStartsWithMatch;
        }

        // Search artist that starts with the text
        SongSelectEntry artistStartsWithMatch = songRouletteControl.Find(it =>
        {
            string artistNoWhitespace = GetEntryArtist(it).Replace(" ", "");
            return StringUtils.StartsWithIgnoreCaseAndDiacritics(artistNoWhitespace, searchTextNoWhitespace);
        });
        if (artistStartsWithMatch != null)
        {
            return artistStartsWithMatch;
        }

        // Search title or artist contains the text
        SongSelectEntry artistOrTitleContainsMatch = songRouletteControl.Find(it =>
        {
            string artistNoWhitespace = GetEntryArtist(it).Replace(" ", "");
            string titleNoWhitespace = GetEntryTitle(it).Replace(" ", "");
            return StringUtils.ContainsIgnoreCaseAndDiacritics(artistNoWhitespace, searchTextNoWhitespace)
                   || StringUtils.ContainsIgnoreCaseAndDiacritics(titleNoWhitespace, searchTextNoWhitespace);
        });
        if (artistOrTitleContainsMatch != null)
        {
            return artistOrTitleContainsMatch;
        }

        return null;
    }

    private SongSelectEntry GetFirstSongOfCurrentOrderThatStartsWith(string searchTextNoWhitespace)
    {
        // search from the current entry in the current order
        SongSelectSongEntry currentEntry = songRouletteControl.SelectedEntry as SongSelectSongEntry;
        if (currentEntry == null)
        {
            return null;
        }

        ESearchProperty searchProperty = GetSearchProperty(settings.SongOrder);
        string currentProperty = GetPropertyValue(currentEntry.SongMeta, searchProperty);

        SongSelectEntry matchingEntry = songRouletteControl.Find(it =>
        {
            SongSelectSongEntry songEntry = it as SongSelectSongEntry;
            if (songEntry?.SongMeta == null)
            {
                return false;
            }

            string property = GetPropertyValue(songEntry.SongMeta, searchProperty);
            return currentProperty == null || StringUtils.StartsWithIgnoreCaseAndDiacritics(property, searchTextNoWhitespace);
        });
        return matchingEntry;
    }

    private ESearchProperty GetSearchProperty(ESongOrder songOrder)
    {
        switch (songOrder)
        {
            case ESongOrder.Artist:
                return ESearchProperty.Artist;
            case ESongOrder.Title:
                return ESearchProperty.Title;
            case ESongOrder.Genre:
                return ESearchProperty.Genre;
            case ESongOrder.Language:
                return ESearchProperty.Language;
            case ESongOrder.Year:
                return ESearchProperty.Year;
            default:
                return ESearchProperty.Artist;
        }
    }

    public void SelectNextEntryByOrderProperty()
    {
        SelectEntryByOrderProperty(1);
    }

    public void SelectPreviousEntryByOrderProperty()
    {
        SelectEntryByOrderProperty(-1);
    }

    private void SelectEntryByOrderProperty(int direction)
    {
        ESongOrder currentOrder = settings.SongOrder;
        SongSelectSongEntry currentEntry = songRouletteControl.SelectedEntry as SongSelectSongEntry;
        if (currentEntry == null)
        {
            return;
        }

        ESearchProperty currentOrderProperty = GetSearchProperty(currentOrder);
        string currentOrderPropertyValue = GetPropertyValue(currentEntry.SongMeta, currentOrderProperty);
        if (currentOrderPropertyValue.IsNullOrEmpty())
        {
            return;
        }

        Func<Predicate<SongSelectEntry>,SongSelectEntry> findMethod = direction > 0
            ? songRouletteControl.Find
            : songRouletteControl.FindLast;

        SongSelectEntry songSelectEntry = findMethod(entry => entry is SongSelectSongEntry songEntry
                                                             && IsPropertyValueNextInDirection(currentOrderPropertyValue, GetPropertyValue(songEntry.SongMeta, currentOrderProperty), direction));
        if (songSelectEntry != null)
        {
            songRouletteControl.SelectEntry(songSelectEntry);
        }
        else if (direction > 0)
        {
            songRouletteControl.SelectEntry(songRouletteControl.Entries.FirstOrDefault());
        }
        else if (direction < 0)
        {
            songRouletteControl.SelectEntry(songRouletteControl.Entries.LastOrDefault());
        }
    }

    private bool IsPropertyValueNextInDirection(string current, string potentialNext, int direction)
    {
        if (current.IsNullOrEmpty()
            || potentialNext.IsNullOrEmpty())
        {
            return false;
        }

        string currentNormalized = StringUtils.RemoveDiacritics(current).ToLowerInvariant();
        string potentialNextNormalized = StringUtils.RemoveDiacritics(potentialNext).ToLowerInvariant();

        return direction > 0
            ? potentialNextNormalized[0] > currentNormalized[0]
            : potentialNextNormalized[0] < currentNormalized[0];
    }

    public void ResetSearchText()
    {
        searchTextField.value = "";
    }

    private void RegisterToggleSearchPropertyCallback(Toggle toggle, ESearchProperty searchProperty)
    {
        toggle.value = settings.SearchProperties.Contains(searchProperty);
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
            {
                AddSearchProperty(searchProperty);
            }
            else
            {
                RemoveSearchProperty(searchProperty);
            }
        });
    }

    ///////////////////////////////////////////////////////////
    public class SearchChangedEvent
    {

    }

    public class SearchPropertyChangedEvent : SearchChangedEvent
    {

    }

    public class SearchTextChangedEvent : SearchChangedEvent
    {

    }
}
