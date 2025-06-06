using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniInject;
using UnityEngine;
using UnityEngine.UIElements;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class SongEditorFileSystemWatcher : MonoBehaviour, INeedInjection
{
#if UNITY_STANDALONE

    [Inject]
    private SongMeta songMeta;

    [Inject]
    private SongEditorSceneControl songEditorSceneControl;

    [Inject]
    private UiManager uiManager;

    [Inject]
    private UIDocument uiDocument;

    [Inject]
    private SongVideoPlayer songVideoPlayer;

    [Inject]
    private SongAudioPlayer songAudioPlayer;

    [Inject]
    private OverviewAreaControl overviewAreaControl;

    private string FolderPath => SongMetaUtils.GetDirectoryPath(songMeta);

    private readonly List<IDisposable> disposables = new();

    private readonly HashSet<string> changedVideoFiles = new();
    private readonly HashSet<string> changedAudioFiles = new();
    private readonly HashSet<string> changedImageFiles = new();

    private void Start()
    {
        if (FolderPath.IsNullOrEmpty()
            || !Directory.Exists(FolderPath))
        {
            return;
        }

        // Media formats supported in Unity editor.
        // Note: Not all of these formats are supported by Unity at runtime.
        List<string> videoFilters = new()
        { "*.asf", "*.avi", "*.dv", "*.m4v", "*.mov",
            "*.mp4", "*.mpg", "*.mpeg", "*.ogv", "*.vp8", "*.webm", "*.wmv"};
        List<string> audioFilters = new()
        { "*.aiff", "*.aif", "*.it", "*.mp3", "*.mod", "*.ogg",
            "*.s3m", "*.wav", "*.xm" };
        List<string> imageFilters = new() { "*.bmp", "*.jpg", "*.png", "*.psd", "*.tiff", "*.tga"};
        videoFilters.ForEach(filter => CreateFileSystemWatcher("SongEditorVideoWatcher", filter, OnVideoFileChanged));
        imageFilters.ForEach(filter => CreateFileSystemWatcher("SongEditorImageWatcher", filter, OnImageFileChanged));
        audioFilters.ForEach(filter => CreateFileSystemWatcher("SongEditorAudioWatcher", filter, OnAudioFileChanged));

        Debug.Log("Watching song media files in: " + FolderPath);
    }

    private void Update()
    {
        if (!changedVideoFiles.IsNullOrEmpty())
        {
            ReloadVideoFiles(changedVideoFiles);
            changedVideoFiles.Clear();
        }

        if (!changedImageFiles.IsNullOrEmpty())
        {
            ReloadImageFiles(changedImageFiles);
            changedImageFiles.Clear();
        }

        if (!changedAudioFiles.IsNullOrEmpty())
        {
            ReloadAudioFiles(changedAudioFiles);
            changedAudioFiles.Clear();
        }
    }

    private void ReloadAudioFiles(HashSet<string> changedFiles)
    {
        if (songMeta.Audio.IsNullOrEmpty())
        {
            return;
        }

        songAudioPlayer.ReloadAudio();
        overviewAreaControl.UpdateAudioWaveForm();

        string changedFileNamesCsv = string.Join(",", changedFiles.Select(path => Path.GetFileName(path)));
        Debug.Log($"Reloaded audio because of changed files: {changedFileNamesCsv}");
    }

    private void ReloadImageFiles(HashSet<string> changedFiles)
    {
        if (songMeta.Cover.IsNullOrEmpty()
            && songMeta.Background.IsNullOrEmpty())
        {
            return;
        }

        if (SongMetaUtils.CoverResourceExists(songMeta))
        {
            ImageManager.ReloadImage(SongMetaUtils.GetCoverUri(songMeta), uiDocument);
        }

        if (SongMetaUtils.BackgroundResourceExists(songMeta))
        {
            ImageManager.ReloadImage(SongMetaUtils.GetBackgroundUri(songMeta), uiDocument);
        }

        string changedFileNamesCsv = string.Join(",", changedFiles.Select(path => Path.GetFileName(path)));
        Debug.Log($"Reloaded cover and background because of changed files: {changedFileNamesCsv}");
    }

    private void ReloadVideoFiles(IEnumerable<string> changedFiles)
    {
        if (songMeta.Video.IsNullOrEmpty())
        {
            return;
        }

        songVideoPlayer.ReloadVideo();
        string changedFileNamesCsv = string.Join(",", changedFiles.Select(path => Path.GetFileName(path)));
        Debug.Log($"Reloaded video because of changed files: {changedFileNamesCsv}");
    }

    private void CreateFileSystemWatcher(string description, string filter, FileSystemEventHandler fileSystemEventHandler)
    {
        FileSystemWatcher fileSystemWatcher = FileSystemWatcherFactory.CreateFileSystemWatcher(FolderPath,
            new FileSystemWatcherConfig(description, filter),
            fileSystemEventHandler);
        disposables.Add(fileSystemWatcher);
    }

    private void OnVideoFileChanged(object sender, FileSystemEventArgs e)
    {
        // Do not use Unity API because we are not in the main thread.
        changedVideoFiles.Add(e.FullPath);
    }

    private void OnAudioFileChanged(object sender, FileSystemEventArgs e)
    {
        // Do not use Unity API because we are not in the main thread.
        changedAudioFiles.Add(e.FullPath);
    }

    private void OnImageFileChanged(object sender, FileSystemEventArgs e)
    {
        // Do not use Unity API because we are not in the main thread.
        changedImageFiles.Add(e.FullPath);
    }

    private void OnDestroy()
    {
        disposables.ForEach(disposable => disposable.Dispose());
    }

#endif
}
