﻿using System;

public class DywaAudioSamplesAnalyzer : AbstractAudioSamplesAnalyzer
{
    private const int MinSampleLength = 256;
    private readonly int maxSampleLength;

    private readonly DywaPitchTracker dywaPitchTracker;

    public DywaAudioSamplesAnalyzer(int sampleRateHz, int maxSampleLength)
    {
        this.maxSampleLength = maxSampleLength;

        // Create and configure Dynamic Wavelet Pitch Tracker.
        dywaPitchTracker = new DywaPitchTracker();
        dywaPitchTracker.SampleRateHz = sampleRateHz;
    }

    public void ClearPitchHistory()
    {
        dywaPitchTracker.ClearPitchHistory();
    }

    public override PitchEvent ProcessAudioSamples(
        float[] sampleBuffer,
        int startIndexInclusive,
        int endIndexExclusive,
        int amplificationFactor,
        int noiseSuppressionFactor)
    {
        int sampleLength = endIndexExclusive - startIndexInclusive;
        if (sampleLength < MinSampleLength)
        {
            return null;
        }
        int sampleCountToUse = PreviousPowerOfTwo(sampleLength);

        // The number of analyzed samples impacts the performance notably.
        // Do not analyze more samples than necessary.
        if (sampleCountToUse > maxSampleLength)
        {
            sampleCountToUse = PreviousPowerOfTwo(maxSampleLength);
        }

        // Copy samples if needed
        if (!ModifySamplesInPlace)
        {
            // Copy original sample buffer
            float[] sampleBufferCopy = new float[sampleBuffer.Length];
            Array.Copy(sampleBuffer, sampleBufferCopy, sampleBuffer.Length);
            sampleBuffer = sampleBufferCopy;
        }

        // Apply amplification
        ApplyAmplification(sampleBuffer, startIndexInclusive, startIndexInclusive + sampleCountToUse, amplificationFactor);

        // Check if samples is louder than threshold
        if (!IsAboveNoiseSuppressionThreshold(sampleBuffer, startIndexInclusive, startIndexInclusive + sampleCountToUse, noiseSuppressionFactor))
        {
            dywaPitchTracker.ClearPitchHistory();
            return null;
        }

        // Find frequency
        float frequency = dywaPitchTracker.ComputePitch(sampleBuffer, startIndexInclusive, sampleCountToUse);
        if (frequency <= 0)
        {
            dywaPitchTracker.ClearPitchHistory();
            return null;
        }

        int midiNote = MidiUtils.GetMidiNoteForFrequency(frequency);
        return new PitchEvent(midiNote, frequency);
    }
}
