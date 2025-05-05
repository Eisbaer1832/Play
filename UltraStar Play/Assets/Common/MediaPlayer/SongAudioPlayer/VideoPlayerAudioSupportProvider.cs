﻿using System;
using System.Collections.Generic;
using System.IO;
using UniInject;
using UnityEngine;
using UnityEngine.Video;

public class VideoPlayerAudioSupportProvider : AbstractAudioSupportProvider
{
    [InjectedInInspector]
    public VideoPlayer videoPlayer;

    [InjectedInInspector]
    public AudioSourceAudioSupportProvider audioSourceAudioSupportProvider;

    private readonly List<string> videoPlayerErrorMessages = new();

    private void OnEnable()
    {
        videoPlayer.errorReceived += OnVideoPlayerErrorReceived;
    }

    private void OnDisable()
    {
        videoPlayer.errorReceived -= OnVideoPlayerErrorReceived;
    }

    public override async Awaitable<AudioLoadedEvent> LoadAsync(string audioUri, bool streamAudio, double startPositionInMillis)
    {
        videoPlayer.url = audioUri;
        if (videoPlayer.url.IsNullOrEmpty())
        {
            Unload();
            throw new SongAudioPlayerException($"Failed to load video from {audioUri}");
        }

        // Must play the video to trigger loading.
        videoPlayer.Play();
        PositionInMillis = startPositionInMillis;

        // The video is loaded asynchronously. The length property of the VideoPlayer indicates whether it has been loaded.
        await ConditionUtils.WaitForConditionAsync(() => !this || videoPlayer.length > 0 || videoPlayerErrorMessages.Count > 0,
            new WaitForConditionConfig {description = $"load audio '{audioUri}'" });
        if (!this)
        {
            throw new DestroyedAlreadyException($"Failed to load audio clip '{audioUri}': {nameof(VideoPlayerAudioSupportProvider)} has been destroyed already.");
        }

        if (videoPlayerErrorMessages.Count > 0)
        {
            Unload();
            throw new AudioSupportProviderException($"Failed to load audio '{audioUri}'. VideoPlayer received error messages.");
        }

        // Play the audio of the video player through the AudioSource.
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        for (ushort trackIndex = 0; trackIndex < videoPlayer.audioTrackCount; trackIndex++)
        {
            Debug.Log($"videoPlayer.SetTargetAudioSource: trackIndex: {trackIndex}, audioSource: {audioSourceAudioSupportProvider.audioSource}");
            videoPlayer.SetTargetAudioSource(trackIndex, audioSourceAudioSupportProvider.audioSource);
        }
        audioSourceAudioSupportProvider.audioSource.Play();

        return new AudioLoadedEvent(audioUri);
    }

    public override bool IsSupported(string audioUri)
    {
        return !WebViewUtils.CanHandleWebViewUrl(audioUri)
            && settings.VlcToPlayMediaFilesUsage is not EThirdPartyLibraryUsage.Always
            && ApplicationUtils.IsUnitySupportedVideoFormat(Path.GetExtension(audioUri));
    }

    public override void Unload()
    {
        videoPlayer.Stop();
        videoPlayer.url = "";
        videoPlayerErrorMessages.Clear();
        RenderTextureUtils.Clear(videoPlayer.targetTexture);
    }

    public override void Play()
    {
        // The audio output is redirected to the AudioSource. Thus, play VideoPlayer and AudioSource.
        videoPlayer.Play();
        audioSourceAudioSupportProvider.Play();
    }

    public override void Pause()
    {
        // The audio output is redirected to the AudioSource. Thus, pause VideoPlayer and AudioSource.
        videoPlayer.Pause();
        audioSourceAudioSupportProvider.Pause();
    }

    public override void Stop()
    {
        // The audio output is redirected to the AudioSource. Thus, stop VideoPlayer and AudioSource.
        videoPlayer.Stop();
        audioSourceAudioSupportProvider.Stop();
    }

    public override bool IsPlaying
    {
        get => videoPlayer.isPlaying;
        set
        {
            if (value)
            {
                Play();
            }
            else
            {
                Pause();
            }
        }
    }

    public override double PlaybackSpeed
    {
        get => videoPlayer.playbackSpeed;
        set => SetPlaybackSpeed(value, true);
    }

    public override void SetPlaybackSpeed(double newValue, bool changeTempoButKeepPitch)
    {
        videoPlayer.playbackSpeed = (float)newValue;
    }

    public override double PositionInMillis
    {
        get => videoPlayer.time * 1000.0;
        set => videoPlayer.time = value / 1000.0;
    }

    public override double DurationInMillis => videoPlayer.length * 1000.0;

    public override double VolumeFactor
    {
        // The audio output is redirected to the AudioSource. Thus, use AudioSource for volume.
        get => audioSourceAudioSupportProvider.VolumeFactor;
        set => audioSourceAudioSupportProvider.VolumeFactor = value;
    }

    private void OnVideoPlayerErrorReceived(VideoPlayer source, string message)
    {
        Debug.LogError($"SongAudioPlayer received VideoPlayer error: {message}");
        videoPlayerErrorMessages.Add(message);
    }
}
