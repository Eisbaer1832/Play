using System.Collections.Generic;
using UniInject;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class DeleteSentencesAction : INeedInjection
{
    [Inject]
    private DeleteNotesAction deleteNotesAction;

    [Inject]
    private EditorNoteDisplayer editorNoteDisplayer;

    [Inject]
    private SongMetaChangedEventStream songMetaChangedEventStream;

    public void Execute(List<Sentence> selectedSentences)
    {
        if (selectedSentences.IsNullOrEmpty())
        {
            return;
        }

        foreach (Sentence sentence in selectedSentences)
        {
            deleteNotesAction.Execute(new List<Note>(sentence.Notes));
            sentence.SetVoice(null);
        }
        editorNoteDisplayer.RemoveSentences(selectedSentences);
    }

    public void ExecuteAndNotify(List<Sentence> selectedSentences)
    {
        Execute(selectedSentences);
        songMetaChangedEventStream.OnNext(new SentencesDeletedEvent
        {
            Sentences = selectedSentences
        });
    }
}
