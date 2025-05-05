using System.Collections.Generic;
using System.Linq;
using System.Text;
using UniInject;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class MergeNotesAction : INeedInjection
{
    [Inject]
    private DeleteNotesAction deleteNotesAction;

    [Inject]
    private SongMetaChangedEventStream songMetaChangedEventStream;

    [Inject]
    private SongEditorLayerManager layerManager;

    public bool CanExecute(IReadOnlyCollection<Note> selectedNotes)
    {
        return selectedNotes.Count > 1;
    }

    public void Execute(IReadOnlyCollection<Note> selectedNotes, Note targetNote)
    {
        List<Note> sortedNotes = new(selectedNotes);
        sortedNotes.Sort(Note.comparerByStartBeat);
        int minBeat = sortedNotes[0].StartBeat;
        int maxBeat = sortedNotes.Select(it => it.EndBeat).Max();
        StringBuilder stringBuilder = new();
        foreach (Note note in sortedNotes)
        {
            if (stringBuilder.Length == 0 || note.Text != "~")
            {
                stringBuilder.Append(note.Text);
            }
        }
        Note mergedNote = new(targetNote.Type, minBeat, maxBeat - minBeat, targetNote.TxtPitch, stringBuilder.ToString());
        mergedNote.SetSentence(targetNote.Sentence);

        if (layerManager.TryGetEnumLayer(targetNote, out SongEditorEnumLayer songEditorLayer))
        {
            layerManager.AddNoteToEnumLayer(songEditorLayer.LayerEnum, mergedNote);
        }

        // Remove old notes
        deleteNotesAction.Execute(sortedNotes);
    }

    public void ExecuteAndNotify(IReadOnlyCollection<Note> selectedNotes, Note targetNote)
    {
        Execute(selectedNotes, targetNote);
        songMetaChangedEventStream.OnNext(new NotesMergedEvent());
    }
}
