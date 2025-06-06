﻿using System;
using System.Collections.Generic;
using System.Linq;
using UniInject;
using UniRx;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class SongEditorSelectionControl : MonoBehaviour, INeedInjection
{
    [Inject(Optional = true)]
    private EventSystem eventSystem;

    [Inject]
    private SongEditorSceneControl songEditorSceneControl;

    [Inject]
    private EditorNoteDisplayer editorNoteDisplayer;

    [Inject]
    private SongAudioPlayer songAudioPlayer;

    [Inject]
    private SongMeta songMeta;

    [Inject]
    private SongEditorLayerManager layerManager;

    [Inject]
    private UIDocument uiDocument;

    private readonly HashSet<Note> selectedNotes = new();

    private readonly Subject<NoteSelectionChangedEvent> noteSelectionChangedEventStream = new();
    public IObservable<NoteSelectionChangedEvent> NoteSelectionChangedEventStream => noteSelectionChangedEventStream;
    public bool IsSelectionEmpty => selectedNotes.IsNullOrEmpty();

    public List<Note> GetSelectedNotes()
    {
        return new List<Note>(selectedNotes);
    }

    public bool HasSelectedNotes()
    {
        return selectedNotes != null
               && selectedNotes.Count > 0;
    }

    public bool IsSelected(Note note)
    {
        return selectedNotes.Contains(note);
    }

    public void ClearSelection()
    {
        ClearSelectionWithoutNotify();
        FireNoteSelectionChangedEvent();
    }

    private void ClearSelectionWithoutNotify()
    {
        foreach (EditorNoteControl uiNote in editorNoteDisplayer.EditorNoteControls)
        {
            uiNote.SetSelected(false);
        }
        selectedNotes.Clear();
    }

    public void SelectAll()
    {
        List<Note> allNotes = songEditorSceneControl.GetAllVisibleNotes();
        SetSelection(allNotes);

        // Update selection range for pitch detection and speech recognition
        int minMidiNote = MidiUtils.SingableNoteMin;
        int maxMidiNote = MidiUtils.SingableNoteMax;
        if (!allNotes.IsNullOrEmpty())
        {
            minMidiNote = allNotes.Select(note => note.MidiNote).Min();
            maxMidiNote = allNotes.Select(note => note.MidiNote).Max();
        }
        NoteAreaSelectionDragListener.lastSelectionRect.Value = NoteAreaRect.CreateFromMillis(songMeta, 0, (int)songAudioPlayer.DurationInMillis, minMidiNote, maxMidiNote);
    }

    public void AddToSelection(List<EditorNoteControl> uiNotes)
    {
        foreach (EditorNoteControl uiNote in uiNotes)
        {
            AddToSelectionWithoutNotify(uiNote);
        }
        FireNoteSelectionChangedEvent();
    }

    public void RemoveFromSelection(List<EditorNoteControl> uiNotes)
    {
        foreach (EditorNoteControl uiNote in uiNotes)
        {
            RemoveFromSelection(uiNote);
        }
    }

    public void SetSelection(List<EditorNoteControl> uiNotes)
    {
        ClearSelectionWithoutNotify();
        foreach (EditorNoteControl uiNote in uiNotes)
        {
            AddToSelectionWithoutNotify(uiNote);
        }
        FireNoteSelectionChangedEvent();
    }

    public void SetSelection(List<Note> notes)
    {
        SetSelectionWithoutNotify(notes);
        FireNoteSelectionChangedEvent();
    }

    private void SetSelectionWithoutNotify(List<Note> notes)
    {
        ClearSelectionWithoutNotify();
        foreach (Note note in notes)
        {
            if (!note.IsEditable
                || !layerManager.IsNoteVisible(note))
            {
                continue;
            }
            selectedNotes.Add(note);

            EditorNoteControl noteControl = editorNoteDisplayer.GetNoteControl(note);
            if (noteControl != null)
            {
                noteControl.SetSelected(true);
            }
        }
        FireNoteSelectionChangedEvent();
    }

    public void AddToSelection(List<Note> notes)
    {
        notes.ForEach(AddToSelectionWithoutNotify);
        FireNoteSelectionChangedEvent();
    }

    public void AddToSelection(Note note)
    {
        AddToSelectionWithoutNotify(note);
        FireNoteSelectionChangedEvent();
    }

    private void AddToSelectionWithoutNotify(Note note)
    {
        if (!note.IsEditable)
        {
            return;
        }

        selectedNotes.Add(note);
        EditorNoteControl noteControl = editorNoteDisplayer.GetNoteControl(note);
        if (noteControl != null)
        {
            noteControl.SetSelected(true);
        }
    }

    public void AddToSelection(EditorNoteControl noteControl)
    {
        AddToSelectionWithoutNotify(noteControl);
        FireNoteSelectionChangedEvent();
    }

    private void AddToSelectionWithoutNotify(EditorNoteControl noteControl)
    {
        noteControl.SetSelected(true);
        selectedNotes.Add(noteControl.Note);
    }

    public void RemoveFromSelection(List<Note> notes)
    {
        notes.ForEach(RemoveFromSelectionWithoutNotify);
        FireNoteSelectionChangedEvent();
    }

    public void RemoveFromSelection(Note note)
    {
        RemoveFromSelectionWithoutNotify(note);
        FireNoteSelectionChangedEvent();
    }

    private void RemoveFromSelectionWithoutNotify(Note note)
    {
        selectedNotes.Remove(note);
        EditorNoteControl noteControl = editorNoteDisplayer.GetNoteControl(note);
        if (noteControl != null)
        {
            noteControl.SetSelected(false);
        }
    }

    public void RemoveFromSelection(EditorNoteControl noteControl)
    {
        RemoveFromSelectionWithoutNotify(noteControl);
        FireNoteSelectionChangedEvent();
    }

    private void RemoveFromSelectionWithoutNotify(EditorNoteControl noteControl)
    {
        noteControl.SetSelected(false);
        selectedNotes.Remove(noteControl.Note);
    }

    private void FireNoteSelectionChangedEvent()
    {
        noteSelectionChangedEventStream.OnNext(new NoteSelectionChangedEvent(selectedNotes));
    }

    public void SelectNextNote(bool updatePosition = true)
    {
        EditorNoteControl editorNoteControl = editorNoteDisplayer.EditorNoteControls.FirstOrDefault(it => it.IsEditingLyrics());
        bool wasEditingLyrics = false;
        if (editorNoteControl != null)
        {
            // Finish this lyrics editing and select following note directly in lyrics editing mode.
            wasEditingLyrics = true;
            editorNoteControl.SubmitAndCloseLyricsDialog();
            SetSelection(new List<EditorNoteControl> { editorNoteControl });
        }

        if (selectedNotes.Count == 0)
        {
            SelectFirstVisibleNote();
            return;
        }

        List<Note> notes = songEditorSceneControl.GetAllVisibleNotes()
            .Where(it => it.IsEditable)
            .ToList();
        int maxEndBeat = selectedNotes.Select(it => it.EndBeat).Max();

        // Find the next note, i.e., the note right of maxEndBeat with the smallest distance to it.
        int smallestDistance = int.MaxValue;
        Note nextNote = null;
        foreach (Note note in notes)
        {
            if (note.StartBeat >= maxEndBeat)
            {
                int distance = note.StartBeat - maxEndBeat;
                if (distance < smallestDistance)
                {
                    smallestDistance = distance;
                    nextNote = note;
                }
            }
        }

        // Unity should not select another VisualElement when using tab on keyboard
        uiDocument.rootVisualElement.focusController.focusedElement?.Blur();

        if (nextNote != null)
        {
            SetSelection(new List<Note> { nextNote });

            if (updatePosition)
            {
                double noteStartInMillis = SongMetaBpmUtils.BeatsToMillis(songMeta, nextNote.StartBeat);
                songAudioPlayer.PositionInMillis = noteStartInMillis;
            }

            if (wasEditingLyrics)
            {
                songEditorSceneControl.StartEditingSelectedNoteText();
                // When the newly selected note has not been drawn yet (because it is not in the current viewport),
                // then the lyric edit mode might not have been started. To fix this, open lyrics edit mode again few frames later.
                AwaitableUtils.ExecuteAfterDelayInFramesAsync(gameObject, 2,
                    () => songEditorSceneControl.StartEditingSelectedNoteText());
            }
        }
    }

    public void SelectPreviousNote(bool updatePosition = true)
    {
        bool wasEditingLyrics = false;
        EditorNoteControl editorNoteControl = editorNoteDisplayer.EditorNoteControls.FirstOrDefault(it => it.IsEditingLyrics());
        if (editorNoteControl != null)
        {
            // Finish this lyrics editing and select following note directly in lyrics editing mode.
            wasEditingLyrics = true;
            editorNoteControl.SubmitAndCloseLyricsDialog();
            SetSelection(new List<EditorNoteControl> { editorNoteControl });
        }

        if (selectedNotes.Count == 0)
        {
            SelectLastVisibleNote();
            return;
        }

        List<Note> notes = songEditorSceneControl.GetAllVisibleNotes()
            .Where(it => it.IsEditable)
            .ToList();
        int minStartBeat = selectedNotes.Select(it => it.StartBeat).Min();

        // Find the previous note, i.e., the note left of minStartBeat with the smallest distance to it.
        int smallestDistance = int.MaxValue;
        Note previousNote = null;
        foreach (Note note in notes)
        {
            if (minStartBeat >= note.EndBeat)
            {
                int distance = minStartBeat - note.EndBeat;
                if (distance < smallestDistance)
                {
                    smallestDistance = distance;
                    previousNote = note;
                }
            }
        }

        if (previousNote != null)
        {
            SetSelection(new List<Note> { previousNote });

            if (updatePosition)
            {
                double noteStartInMillis = SongMetaBpmUtils.BeatsToMillis(songMeta, previousNote.StartBeat);
                songAudioPlayer.PositionInMillis = noteStartInMillis;
            }

            if (wasEditingLyrics)
            {
                songEditorSceneControl.StartEditingSelectedNoteText();
                // When the newly selected note has not been drawn yet (because it is not in the current viewport),
                // then the lyric edit mode might not have been started. To fix this, open lyrics edit mode again few frames later.
                AwaitableUtils.ExecuteAfterDelayInFramesAsync(gameObject, 2,
                    () => songEditorSceneControl.StartEditingSelectedNoteText());
            }
        }
    }

    private void SelectFirstVisibleNote()
    {
        List<EditorNoteControl> sortedUiNotes = GetSortedVisibleUiNotes();
        if (sortedUiNotes.IsNullOrEmpty())
        {
            return;
        }

        EditorNoteControl firstEditableUiNote = sortedUiNotes.FirstOrDefault(it => it.IsEditable);
        SetSelection(new List<EditorNoteControl> { firstEditableUiNote });
    }

    private void SelectLastVisibleNote()
    {
        List<EditorNoteControl> sortedUiNotes = GetSortedVisibleUiNotes();
        if (sortedUiNotes.IsNullOrEmpty())
        {
            return;
        }

        EditorNoteControl lastEditableUiNote = sortedUiNotes.LastOrDefault(it => it.IsEditable);
        SetSelection(new List<EditorNoteControl> { lastEditableUiNote });
    }

    private List<EditorNoteControl> GetSortedVisibleUiNotes()
    {
        if (editorNoteDisplayer.EditorNoteControls.IsNullOrEmpty())
        {
            return new List<EditorNoteControl>();
        }

        List<EditorNoteControl> sortedUiNote = new(editorNoteDisplayer.EditorNoteControls);
        sortedUiNote.Sort(EditorNoteControl.comparerByStartBeat);
        return sortedUiNote;
    }
}
