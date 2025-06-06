﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrimeInputActions;
using UniInject;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class SongSelectEntryControl : INeedInjection, IInjectionFinishedListener, IDisposable
{
    public const float MaxClickDistanceThresholdInPx = 5f;

    private const string NavigateToParentFolderIcon = "↑";

    [Inject]
    private SongRouletteControl songRouletteControl;

    [Inject]
    private SongSelectSceneControl songSelectSceneControl;

    [Inject]
    private SongSelectSongQueueControl songSelectSongQueueControl;

    [Inject]
    private PlaylistManager playlistManager;

    [Inject(UxmlName = R.UxmlNames.songImageOuter)]
    private VisualElement songImageOuter;

    [Inject(UxmlName = R.UxmlNames.songImageInner)]
    private VisualElement songImageInner;

    [Inject(UxmlName = R.UxmlNames.folderImage)]
    private VisualElement folderImage;

    [Inject(UxmlName = R.UxmlNames.folderPreviewImage)]
    private VisualElement folderPreviewImage;

    [Inject(UxmlName = R.UxmlNames.songArtist)]
    private Label songArtist;

    [Inject(UxmlName = R.UxmlNames.songTitle)]
    private Label songTitle;

    [Inject(UxmlName = R.UxmlNames.songEntryFavoriteIcon)]
    private VisualElement favoriteIcon;

    [Inject(UxmlName = R.UxmlNames.songEntryDuetIcon)]
    private VisualElement duetIcon;

    [Inject(UxmlName = R.UxmlNames.songEntryAutoGeneratedIcon)]
    private VisualElement songEntryAutoGeneratedIcon;

    [Inject(UxmlName = R.UxmlNames.songEntryRemoteSourceIcon)]
    private VisualElement songEntryRemoteSourceIcon;

    [Inject(UxmlName = R.UxmlNames.songEntryUiRoot)]
    private VisualElement songEntryUiRoot;

    [Inject(UxmlName = R.UxmlNames.openSongMenuButton)]
    private Button openSongMenuButton;

    [Inject(UxmlName = R.UxmlNames.notAvailableInOnlineGameIcon)]
    private VisualElement notAvailableInOnlineGameIcon;

    [Inject(UxmlName = R.UxmlNames.innerSongEntryUi)]
    private VisualElement innerSongEntryUi;

    [Inject]
    private CreateSingAlongSongControl createSingAlongSongControl;

    [Inject]
    private NonPersistentSettings nonPersistentSettings;

    [Inject]
    private AudioSeparationManager audioSeparationManager;

    [Inject]
    private SongMetaManager songMetaManager;

    [Inject]
    private UiManager uiManager;

    [Inject]
    private Injector injector;

    [Inject]
    private Settings settings;

    [Inject]
    private FolderPreviewImageManager folderPreviewImageManager;

    private TooltipControl songEntryRemoteSourceIconTooltipControl;

    /**
     * Name for debugging purpose
     */
    public string Name { get; set; }

    public SongSelectEntry SongSelectEntry
    {
        get => songSelectEntryProperty.Value;
        set => songSelectEntryProperty.Value = value;
    }

    private readonly ReactiveProperty<SongSelectEntry> songSelectEntryProperty = new();
    public IObservable<SongSelectEntry> SongSelectEntryAsObservable => songSelectEntryProperty;

    [Inject(Key = Injector.RootVisualElementInjectionKey)]
    public VisualElement VisualElement { get; private set; }

    private ContextMenuControl contextMenuControl;
    private bool isPopupMenuOpen;
    private float popupMenuClosedTimeInSeconds;

    private readonly Subject<VoidEvent> clickEventStream = new();
    public IObservable<VoidEvent> ClickEventStream => clickEventStream
        // Prevent accidental double click
        .ThrottleFirst(TimeSpan.FromMilliseconds(300));

    private bool isInitialized;

    private readonly SongSelectSongRatingIconControl songRatingIconControl = new();

    private Vector2 pointerDownMousePosition;

    private string lastSongMetaCover;
    private string lastSongMetaBackground;

    private readonly List<IDisposable> disposables = new();

    public void OnInjectionFinished()
    {
        Init();
        RegisterCallbacks();

    }

    private void RegisterCallbacks()
    {
        innerSongEntryUi.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
        innerSongEntryUi.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        openSongMenuButton.RegisterCallbackButtonTriggered(OnOpenSongMenuButtonClicked);
        disposables.Add(songMetaManager.ReloadedSongMetaEventStream.Subscribe(songMeta => OnSongMetaReloaded(songMeta)));
    }

    private void UnregisterCallbacks()
    {
        innerSongEntryUi.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
        innerSongEntryUi.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        openSongMenuButton.UnregisterCallbackButtonTriggered(OnOpenSongMenuButtonClicked);
        disposables.ForEach(it => it.Dispose());;
        disposables.Clear();
    }

    private void Init()
    {
        if (isInitialized)
        {
            return;
        }
        isInitialized = true;

        injector.Inject(songRatingIconControl);

        songEntryRemoteSourceIconTooltipControl = new(songEntryRemoteSourceIcon);

        InitSongMenu();

        playlistManager.PlaylistChangedEventStream
            .Subscribe(evt => UpdateIcons());

        settings.ObserveEveryValueChanged(it => it.Difficulty)
            .Subscribe(_ => UpdateIcons());

        SongSelectEntryAsObservable.Subscribe(newValue => OnSongSelectEntryChanged(newValue));
    }

    private void OnSongSelectEntryChanged(SongSelectEntry newValue)
    {
        VisualElement.SetVisibleByVisibility(newValue != null);
        UpdateLabels();
        UpdateIcons();
        UpdateCover();
    }

    private void OnSongMetaReloaded(SongMeta songMeta)
    {
        if (SongSelectEntry is SongSelectSongEntry songEntry
            && songEntry.SongMeta?.FileInfo != null
            && songEntry.SongMeta?.FileInfo?.FullName == songMeta.FileInfo?.FullName)
        {
            Debug.Log($"Updating {nameof(SongSelectEntryControl)} because SongMeta was reloaded: path '{songMeta.FileInfo.FullName}'");
            OnSongSelectEntryChanged(SongSelectEntry);
        }
    }

    private void InitSongMenu()
    {
        contextMenuControl = injector
            .WithRootVisualElement(openSongMenuButton)
            .CreateAndInject<ContextMenuControl>();
        contextMenuControl.FillContextMenuAction = FillContextMenu;
        contextMenuControl.ContextMenuOpenedEventStream.Subscribe(OnContextMenuOpened);
        contextMenuControl.ContextMenuClosedEventStream.Subscribe(OnContextMenuClosed);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        pointerDownMousePosition = evt.position;
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        // Open context menu on right click
        if (evt.button == 1
            && !isPopupMenuOpen
            && TimeUtils.IsDurationAboveThresholdInSeconds(popupMenuClosedTimeInSeconds, 0.1f))
        {
            contextMenuControl.OpenContextMenu(evt.position, this);
            return;
        }

        // Fire click event when not clicking a button
        if (evt.button == 0
            && Vector2.Distance(pointerDownMousePosition ,evt.position) < MaxClickDistanceThresholdInPx
            && evt.target != openSongMenuButton)
        {
            clickEventStream.OnNext(VoidEvent.instance);
        }
    }

    private void OnOpenSongMenuButtonClicked(EventBase evt)
    {
        if (isPopupMenuOpen
            || !TimeUtils.IsDurationAboveThresholdInSeconds(popupMenuClosedTimeInSeconds, 0.1f))
        {
            return;
        }

        contextMenuControl.OpenContextMenu(new Vector2(-1, -1), this);
    }

    private void FillContextMenu(ContextMenuPopupControl contextMenuPopup)
    {
        Debug.Log($"Context: {contextMenuPopup.Context}");
        if (SongSelectEntry is SongSelectSongEntry songEntry)
        {
            FillSongEntryContextMenu(contextMenuPopup, songEntry);
        }
        else if (SongSelectEntry is SongSelectFolderEntry folderEntry)
        {
            FillFolderEntryContextMenu(contextMenuPopup, folderEntry);
        }
    }

    private void FillSongEntryContextMenu(ContextMenuPopupControl contextMenuPopup, SongSelectSongEntry songEntry)
    {
        // Start song
        contextMenuPopup.AddButton(Translation.Get(R.Messages.songSelectScene_action_start), "play_arrow",
            () =>
            {
                songSelectSceneControl.AttemptStartEntry(songEntry);
            });

        // Add / remove from playlist
        playlistManager.Playlists
            .Where(playlist => playlist is UltraStarPlaylist
                               && playlist is not UltraStarAllSongsPlaylist)
            .ForEach(playlist =>
            {
                UltraStarPlaylist ultraStarPlaylist = playlist as UltraStarPlaylist;
                string playlistName = playlist.Name;
                if (playlistManager.HasSongEntry(playlist, songEntry.SongMeta))
                {
                    contextMenuPopup.AddButton(Translation.Get(R.Messages.songSelectScene_action_removeFromPlaylist,
                            "playlist", playlistName), "favorite_border", R.Messages.songSelectScene_action_removeFromPlaylist,
                        () => playlistManager.RemoveSongFromPlaylist(ultraStarPlaylist, songEntry.SongMeta));
                }
                else
                {
                    contextMenuPopup.AddButton(Translation.Get(R.Messages.songSelectScene_action_addToPlaylist,
                            "playlist", playlistName), "favorite", R.Messages.songSelectScene_action_addToPlaylist,
                        () => playlistManager.AddSongToPlaylist(ultraStarPlaylist, songEntry.SongMeta));
                }
            });

        VisualElement enqueueMenuEntry = contextMenuPopup.AddButton(Translation.Get(R.Messages.songQueue_action_add), "playlist_add",
            () =>
            {
                songSelectSongQueueControl.AddSongToSongQueue(songEntry.SongMeta);
            });
        enqueueMenuEntry.Q<Button>().name = "enqueueButton";

        VisualElement enqueueAsMenuEntry = contextMenuPopup.AddButton(Translation.Get(R.Messages.songQueue_action_addAsMedley), "link",
            () =>
            {
                songSelectSongQueueControl.AddSongToSongQueueAsMedley(songEntry.SongMeta);
            });
        enqueueAsMenuEntry.Q<Button>().name = "enqueueAsMedleyButton";

        // Open song editor / song folder
        contextMenuPopup.AddButton(Translation.Get(R.Messages.action_openSongEditor), "edit",
            () => songSelectSceneControl.StartSongEditorScene());
        if (PlatformUtils.IsStandalone)
        {
            if (DirectoryUtils.Exists(SongMetaUtils.GetDirectoryPath(songEntry.SongMeta)))
            {
                contextMenuPopup.AddButton(Translation.Get(R.Messages.action_openFolder), "open_in_new",
                    () => SongMetaUtils.OpenDirectory(songEntry.SongMeta));
            }
            contextMenuPopup.AddButton(Translation.Get(R.Messages.action_reloadSong), "replay",
                () => songMetaManager.ReloadSong(songEntry.SongMeta));
        }

        if (Application.isEditor)
        {
            // TODO: enable the "recreate song" feature in production when AI tools are running on Unity API?
            contextMenuPopup.AddButton(Translation.Get(R.Messages.action_recreateSong), "replay_circle_filled",
            () => songSelectSceneControl.AskToRecreateSingAlongData(songEntry.SongMeta));
        }

        contextMenuPopup.AddButton(Translation.Get(R.Messages.action_showInfo), "lyrics",
            () =>
            {
                songSelectSceneControl.ShowLyricsAndInfoPopup(songEntry.SongMeta);
            });

        VisualElement buttonContainer = contextMenuPopup.AddButton(Translation.Get(R.Messages.action_separateAudio), "call_split",
            async () =>
            {
                await audioSeparationManager.ProcessSongMetaJob(songEntry.SongMeta, true).GetResultAsync();
            });

        // Disable button if vocals and instrumental audio already exist.
        if (SongMetaUtils.VocalsAudioResourceExists(songEntry.SongMeta)
            && SongMetaUtils.InstrumentalAudioResourceExists(songEntry.SongMeta))
        {
            buttonContainer.Q<Button>().SetEnabled(false);
        }
    }

    private void FillFolderEntryContextMenu(ContextMenuPopupControl contextMenuPopup, SongSelectFolderEntry folderEntry)
    {
        if (PlatformUtils.IsStandalone)
        {
            if (DirectoryUtils.Exists(folderEntry.DirectoryInfo.FullName))
            {
                contextMenuPopup.AddButton(Translation.Get(R.Messages.action_openFolder), "open_in_new",
                    () => ApplicationUtils.OpenDirectory(folderEntry.DirectoryInfo.FullName));
            }
        }
    }

    private void OnContextMenuClosed(ContextMenuPopupControl contextMenuPopupControl)
    {
        isPopupMenuOpen = false;
        popupMenuClosedTimeInSeconds = Time.time;
    }

    private void OnContextMenuOpened(ContextMenuPopupControl contextMenuPopupControl)
    {
        isPopupMenuOpen = true;
        if (contextMenuPopupControl.Position.x < 0
            || contextMenuPopupControl.Position.y < 0)
        {
            new AnchoredPopupControl(contextMenuPopupControl.VisualElement, openSongMenuButton, Corner2D.BottomLeft);
        }
        contextMenuPopupControl.VisualElement.AddToClassList("singSceneContextMenu");
    }

    private void UpdateCover()
    {
        notAvailableInOnlineGameIcon.HideByDisplay();

        if (SongSelectEntry is SongSelectSongEntry songEntry)
        {
            UpdateSongCover(songEntry);
        }
        else if (SongSelectEntry is SongSelectFolderEntry folderEntry)
        {
            UpdateFolderCover(folderEntry);
        }
        else
        {
            SetDefaultSongCoverImageWithColor();
        }
    }

    private async void UpdateSongCover(SongSelectSongEntry songEntry)
    {
        SongMeta songMeta = songEntry.SongMeta;
        lastSongMetaCover = songMeta.Cover;
        lastSongMetaBackground = songMeta.Background;

        folderImage.HideByDisplay();
        folderPreviewImage.HideByDisplay();

        notAvailableInOnlineGameIcon.HideByDisplay();

        try
        {
            string uri = await SongMetaImageUtils.GetCoverOrBackgroundImageUriAsync(songMeta);
            if (SongEntryChanged(songMeta))
            {
                return;
            }

            if (uri.IsNullOrEmpty())
            {
                SongMetaImageUtils.SetDefaultSongImageAndColor(songMeta, songImageOuter, songImageInner);
                return;
            }

            Sprite sprite = await ImageManager.LoadSpriteFromUriAsync(uri);
            if (SongEntryChanged(songMeta))
            {
                return;
            }

            if (sprite == null)
            {
                SongMetaImageUtils.SetDefaultSongImageAndColor(songMeta, songImageOuter, songImageInner);
                return;
            }
            SongMetaImageUtils.SetCoverOrBackgroundImageAsync(sprite, songImageOuter, songImageInner);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);

            if (SongEntryChanged(songMeta))
            {
                return;
            }
            SongMetaImageUtils.SetDefaultSongImageAndColor(songMeta, songImageOuter, songImageInner);
        }
    }

    public void ShowNotAvailableInOnlineGameIcon()
    {
        notAvailableInOnlineGameIcon.ShowByDisplay();
    }

    private bool SongEntryChanged(SongMeta songMeta)
    {
        return SongSelectEntry is not SongSelectSongEntry songEntry
               || songEntry.SongMeta != songMeta;
    }

    private bool FolderEntryChanged(DirectoryInfo directoryInfo)
    {
        return SongSelectEntry is not SongSelectFolderEntry folderEntry
               || folderEntry.DirectoryInfo.FullName != directoryInfo.FullName;
    }

    private void UpdateFolderCover(SongSelectFolderEntry folderEntry)
    {
        songImageOuter.style.backgroundImage = new StyleBackground(StyleKeyword.Undefined);
        songImageOuter.style.unityBackgroundImageTintColor = new StyleColor(Color.clear);

        songImageInner.style.backgroundImage = new StyleBackground(StyleKeyword.Undefined);
        songImageInner.style.unityBackgroundImageTintColor = new StyleColor(Color.clear);

        folderImage.ShowByDisplay();
        UpdateFolderPreviewImage(folderEntry);
    }

    private async void UpdateFolderPreviewImage(SongSelectFolderEntry folderEntry)
    {
        folderPreviewImage.HideByDisplay();
        await Awaitable.BackgroundThreadAsync();
        string imageUri = folderPreviewImageManager.GetFolderPreviewImageUri(folderEntry.DirectoryInfo);
        if (imageUri.IsNullOrEmpty()
            || FolderEntryChanged(folderEntry.DirectoryInfo))
        {
            return;
        }

        await Awaitable.MainThreadAsync();
        Sprite sprite = await ImageManager.LoadSpriteFromUriAsync(imageUri);
        if (sprite == null)
        {
            return;
        }
        folderPreviewImage.ShowByDisplay();
        folderPreviewImage.style.backgroundImage = new StyleBackground(sprite);
    }

    private void SetDefaultSongCoverImageWithColor()
    {
        SongMetaImageUtils.SetDefaultSongImage(songImageOuter, songImageInner);
        if (SongSelectEntry is SongSelectSongEntry songEntry)
        {
            SongMetaImageUtils.SetDefaultSongImageColor(songEntry.SongMeta, songImageOuter, songImageInner);
        }
        else
        {
            songImageOuter.style.unityBackgroundImageTintColor = new StyleColor(StyleKeyword.Undefined);
            songImageInner.style.unityBackgroundImageTintColor = new StyleColor(StyleKeyword.Undefined);
        }
    }

    private void UpdateIcons()
    {
        if (SongSelectEntry is SongSelectSongEntry songEntry)
        {
            UpdateSongIcons(songEntry);
        }
        else
        {
            favoriteIcon.HideByDisplay();
            duetIcon.HideByDisplay();
            songEntryAutoGeneratedIcon.HideByDisplay();
            songEntryRemoteSourceIcon.HideByDisplay();
            songEntryRemoteSourceIconTooltipControl.TooltipText = Translation.Empty;
            songRatingIconControl.HideSongRatingIcons();
        }
    }

    private void UpdateSongIcons(SongSelectSongEntry songEntry)
    {
        favoriteIcon.SetVisibleByDisplay(playlistManager.FavoritesPlaylist.HasSongEntry(songEntry.SongMeta));
        duetIcon.SetVisibleByDisplay(songEntry.SongMeta.VoiceCount > 1);
        songEntryAutoGeneratedIcon.SetVisibleByDisplay(!SongMetaUtils.HasSingAlongData(songEntry.SongMeta));
        songEntryRemoteSourceIcon.SetVisibleByDisplay(!songEntry.SongMeta.RemoteSource.IsNullOrEmpty());
        songEntryRemoteSourceIconTooltipControl.TooltipText = Translation.Of(songEntry.SongMeta.RemoteSource.NullToEmpty());
        songRatingIconControl.UpdateSongRatingIcons(songEntry.SongMeta, settings.Difficulty);
    }

    private void UpdateLabels()
    {
        if (SongSelectEntry is SongSelectSongEntry songEntry)
        {
            songArtist.SetTranslatedText(Translation.Of(songEntry.SongMeta.Artist));
            songTitle.SetTranslatedText(Translation.Of(songEntry.SongMeta.Title));
        }
        else if (SongSelectEntry is SongSelectFolderEntry folderEntry)
        {
            songArtist.SetTranslatedText(Translation.Empty);
            songTitle.SetTranslatedText(Translation.Of(GetSongFolderEntryTitle(folderEntry)));
        }
        else
        {
            songTitle.SetTranslatedText(Translation.Empty);
            songArtist.SetTranslatedText(Translation.Empty);
        }
    }

    private string GetSongFolderEntryTitle(SongSelectFolderEntry folderEntry)
    {
        if (SettingsUtils.IsSongFolderNavigationRootFolder(settings, folderEntry.DirectoryInfo)
            || folderEntry.DirectoryInfo?.FullName == nonPersistentSettings.SongSelectDirectoryInfo?.Parent?.FullName)
        {
            return NavigateToParentFolderIcon;
        }

        return folderEntry.DirectoryInfo.Name;
    }

    public void Dispose()
    {
        SongSelectEntry = null;
        UnregisterCallbacks();
    }

    public void OpenContextMenu()
    {
        if (!isPopupMenuOpen)
        {
            ContextMenuPopupControl contextMenuPopupControl = contextMenuControl?.OpenContextMenu(
                new Vector2(openSongMenuButton.worldBound.xMin, openSongMenuButton.worldBound.yMin),
                this);
            contextMenuPopupControl?.VisualElement.Q<Button>().Focus();
        }
    }

    public void Update()
    {
        if (SongSelectEntry is SongSelectSongEntry songEntry
            && (songEntry.SongMeta?.Cover != lastSongMetaCover
                || songEntry.SongMeta?.Background != lastSongMetaBackground))
        {
            Debug.Log($"Updating cover image because cover or background changed in song '{songEntry.SongMeta.GetArtistDashTitle()}'");
            UpdateCover();
        }
    }
}
