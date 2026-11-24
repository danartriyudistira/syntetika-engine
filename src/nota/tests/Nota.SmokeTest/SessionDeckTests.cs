// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session decks (Syntetika): the native model, edits that keep the scene partition in step,
// undo, and the project format (v36 + a v35 file loading as one deck).
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --decks`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SessionDeckTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var transport = new TransportState(120.0, 1.0, false, false);

        // --- the model ----------------------------------------------------
        using (var e = new NotaEngine())
        {
            int rows = e.SceneCount;
            yield return (e.DeckCount == 1 && e.DeckSceneCount(0) == rows && e.CurrentDeck == 0,
                "a fresh project is one deck covering every scene");
            yield return (e.GetDeckName(0).Length > 0, $"the implicit deck has a name ({e.GetDeckName(0)})");
            yield return (e.AddScene() == rows && e.DeckSceneCount(0) == rows + 1,
                "adding a scene grows the open deck");
        }

        using (var e = new NotaEngine())
        {
            int rows = e.SceneCount;
            int second = e.AddDeck();
            yield return (second == 1 && e.DeckCount == 2 && e.DeckSceneCount(1) == 1 && e.CurrentDeck == 1,
                "add deck appends one empty scene and opens it");
            yield return (e.SceneCount == rows + 1, "…and adds one global row");

            int row = e.InsertSceneInDeck(0, 0);
            yield return (row == 0 && e.DeckSceneCount(0) == rows + 1 && e.DeckSceneStart(1) == rows + 1,
                "a scene inserted in a deck shifts the next deck's start");

            e.RenameDeck(1, "Second");
            yield return (e.GetDeckName(1) == "Second", "rename deck");

            int copy = e.DuplicateDeck(1);
            yield return (copy == 2 && e.DeckCount == 3 && e.DeckSceneCount(2) == 1 && e.CurrentDeck == 2
                          && e.GetDeckName(2) == "Second copy",
                "duplicate inserts the copy right after the source and opens it");

            // Content follows the copy: a clip in deck 1 appears in its duplicate's row.
            int track = e.AddInstrumentTrack();
            int sourceRow = e.DeckSceneStart(1);
            e.AddSessionMidiClip(track, sourceRow, 4.0);
            int copiedDeck = e.DuplicateDeck(1);
            int copiedRow = e.DeckSceneStart(copiedDeck);
            yield return (e.SessionSlotState(track, sourceRow) == 1 && e.SessionSlotState(track, copiedRow) == 1,
                "duplicate copies the deck's slot content");

            // Switching is a view change: the playing slot is untouched.
            e.SetLaunchQuant(0);
            e.LaunchSlot(track, copiedRow);
            int before = e.SessionPlayingSlot(track);
            e.SwitchDeck(0);
            yield return (e.CurrentDeck == 0 && e.SessionPlayingSlot(track) == before,
                "switching decks does not stop the playing slot");

            // Removing a deck drops its rows and shifts the later ones, but never the last deck.
            int rowsBefore = e.SceneCount;
            int removedRows = e.DeckSceneCount(1);
            bool removed = e.RemoveDeck(1);
            yield return (removed && e.DeckCount == 3 && e.SceneCount == rowsBefore - removedRows,
                "removing a deck drops its rows");
            yield return (e.DeckSceneStart(1) == e.DeckSceneCount(0), "the rows after it move up");
            while (e.DeckCount > 1) e.RemoveDeck(e.DeckCount - 1);
            yield return (!e.RemoveDeck(0) && e.DeckCount == 1, "the last deck can't be removed");
        }

        // --- undo ---------------------------------------------------------
        using (var e = new NotaEngine())
        {
            int rows = e.SceneCount;
            e.AddDeck();
            bool undone = e.Undo();
            yield return (undone && e.DeckCount == 1 && e.SceneCount == rows, "undo removes the deck it added");
            bool redone = e.Redo();
            yield return (redone && e.DeckCount == 2, "redo brings it back");
        }

        // --- the project format -------------------------------------------
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-decks-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            using (var e = new NotaEngine())
            {
                e.AddDeck();
                e.AddDeck();
                e.RenameDeck(0, "Intro");
                e.RenameDeck(2, "Outro");
                e.SetCurrentDeck(1);
                ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e);
            }
            using (var e = new NotaEngine())
            {
                ProjectService.Apply(ProjectService.Load(dir), e, dir);
                yield return (e.DeckCount == 3 && e.GetDeckName(0) == "Intro" && e.GetDeckName(2) == "Outro"
                              && e.CurrentDeck == 1,
                    "save/load keeps the decks, their names and the open tab");
                yield return (e.DeckSceneCount(0) + e.DeckSceneCount(1) + e.DeckSceneCount(2) == e.SceneCount,
                    "the loaded deck counts add up to the scene count");
            }

            // A v35 file has no deck member: one deck, audio and scenes unchanged.
            string manifest = Path.Combine(dir, ProjectService.ManifestName);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!;
            json["formatVersion"] = 35;
            if (json["session"] is System.Text.Json.Nodes.JsonObject session)
            {
                session.Remove("decks");
                session.Remove("currentDeck");
            }
            File.WriteAllText(manifest, json.ToJsonString());
            using (var e = new NotaEngine())
            {
                ProjectService.Apply(ProjectService.Load(dir), e, dir);
                yield return (e.DeckCount == 1 && e.DeckSceneCount(0) == e.SceneCount && e.CurrentDeck == 0,
                    "a v35 project opens as one deck");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
