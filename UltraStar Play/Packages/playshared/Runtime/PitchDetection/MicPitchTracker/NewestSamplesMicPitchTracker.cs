﻿using UniRx;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

/**
 * Analyzes the newest samples from a mic and fires an event for the analysis result.
 */
public class NewestSamplesMicPitchTracker : AbstractMicPitchTracker
{
    // Wait until at least this amount of new samples is available in the mic buffer.
    // This makes the MicPitchTracker more frame-rate independent.
    private const int MinSampleCountToUse = 1024;
    private int bufferedNotAnalyzedSampleCount;

    [ReadOnly]
    public string lastMidiNoteName;
    [ReadOnly]
    public int lastMidiNote;

    public override void OnInjectionFinished()
    {
        base.OnInjectionFinished();

        RecordingEventStream
            .Subscribe(evt => OnRecordingEvent(evt))
            .AddTo(gameObject);

        // Update label in inspector for debugging.
        pitchEventStream
            .Subscribe(UpdateLastMidiNoteFields)
            .AddTo(gameObject);
    }

    private void OnRecordingEvent(RecordingEvent recordingEvent)
    {
        // Detect the pitch of the sample
        int newSampleLength = recordingEvent.NewSampleCount;
        bufferedNotAnalyzedSampleCount += newSampleLength;

        // Wait until enough new samples are buffered
        if (bufferedNotAnalyzedSampleCount < MinSampleCountToUse)
        {
            return;
        }

        // Do not analyze more than necessary
        if (bufferedNotAnalyzedSampleCount > PitchDetectionConstants.LongestSingableNoteSampleCount)
        {
            bufferedNotAnalyzedSampleCount = PitchDetectionConstants.LongestSingableNoteSampleCount;
        }

        // Analyze the newest portion of the not-yet-analyzed MicSamples
        int startIndex = recordingEvent.MicSamples.Length - bufferedNotAnalyzedSampleCount;
        int endIndex = recordingEvent.MicSamples.Length;
        PitchEvent pitchEvent = AudioSamplesAnalyzer.ProcessAudioSamples(recordingEvent.MicSamples, startIndex, endIndex, MicProfile.AmplificationMultiplier, MicProfile.NoiseSuppression);
        bufferedNotAnalyzedSampleCount = 0;

        // Notify listeners
        pitchEventStream.OnNext(pitchEvent);
    }

    private void UpdateLastMidiNoteFields(PitchEvent pitchEvent)
    {
        if (pitchEvent == null)
        {
            lastMidiNoteName = "";
            lastMidiNote = 0;
        }
        else
        {
            lastMidiNoteName = MidiUtils.GetAbsoluteName(pitchEvent.MidiNote);
            lastMidiNote = pitchEvent.MidiNote;
        }
    }
}
