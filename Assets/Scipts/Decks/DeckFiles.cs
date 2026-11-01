using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
using Windows.Storage;
using Windows.Storage.Pickers;
#endif

/* The deck builder's Export and Import: a deck as a .json file in Downloads, to send to another
 player or keep as a backup. The UWP build may only touch files the player picks, so there Export
 and Import open Windows' save and open dialogs at Downloads. The editor writes straight to
 Downloads and opens its own file dialog. Results come back on Unity's thread. */
public static class DeckFiles
{
    static string DownloadsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // done gets what to tell the player, or null if they cancelled
    public static void Export(Deck deck, Action<string> done)
    {
        string json = ToJson(deck);
        string name = FileName(deck.deckName);
#if ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
        UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
        {
            string message;
            try
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = name };
                picker.FileTypeChoices.Add("Deck file", new List<string> { ".json" });
                StorageFile file = await picker.PickSaveFileAsync();
                if (file == null) message = null;
                else
                {
                    await FileIO.WriteTextAsync(file, json);
                    message = "Exported to " + file.Path;
                }
            }
            catch (Exception e)
            {
                message = "Couldn't export the deck: " + e.Message;
            }
            UnityEngine.WSA.Application.InvokeOnAppThread(() => done(message), false);
        }, false);
#else
        try
        {
            Directory.CreateDirectory(DownloadsPath);
            string path = Path.Combine(DownloadsPath, name + ".json");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(DownloadsPath, name + " (" + i + ").json");
            File.WriteAllText(path, json);
            done("Exported to " + path);
        }
        catch (Exception e)
        {
            done("Couldn't export the deck: " + e.Message);
        }
#endif
    }

    // done gets the deck read (null if none) and what to tell the player (null if they cancelled)
    public static void Import(Action<Deck, string> done)
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Import deck", DownloadsPath, "json");
        if (string.IsNullOrEmpty(path)) done(null, null);
        else Read(Path.GetFileName(path), () => File.ReadAllText(path), done);
#elif ENABLE_WINMD_SUPPORT
        UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
        {
            string name = null, text = null, error = null;
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads, ViewMode = PickerViewMode.List };
                picker.FileTypeFilter.Add(".json");
                StorageFile file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    name = file.Name;
                    text = await FileIO.ReadTextAsync(file);
                }
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            UnityEngine.WSA.Application.InvokeOnAppThread(() =>
            {
                if (error != null) done(null, "Couldn't open the deck file: " + error);
                else if (name == null) done(null, null);
                else Read(name, () => text, done);
            }, false);
        }, false);
#else
        // No file dialog in a standalone build: the newest deck file in Downloads
        string newest = Directory.Exists(DownloadsPath)
            ? Directory.GetFiles(DownloadsPath, "*.json").OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
            : null;
        if (newest == null) done(null, "No .json deck file in " + DownloadsPath + ".");
        else Read(Path.GetFileName(newest), () => File.ReadAllText(newest), done);
#endif
    }

    static void Read(string fileName, Func<string> readText, Action<Deck, string> done)
    {
        Deck deck = null;
        try
        {
            deck = FromJson(readText());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DeckFiles] Couldn't read " + fileName + ": " + e.Message);
        }
        done(deck, deck == null ? fileName + " isn't a deck file." : null);
    }

    // Without its id, so importing it makes a new deck rather than overwriting one
    static string ToJson(Deck deck)
    {
        string id = deck.id;
        deck.id = null;
        try { return JsonUtility.ToJson(deck, true); }
        finally { deck.id = id; }
    }

    // Null unless it has cards. Sleeves this PC has no picture for are dropped
    static Deck FromJson(string json)
    {
        Deck deck = JsonUtility.FromJson<Deck>(json);
        if (deck == null) return null;
        deck.id = null;
        deck.cards ??= new List<DeckEntry>();
        deck.rideDeck ??= new List<DeckEntry>();
        deck.extras ??= new List<DeckEntry>();
        foreach (DeckEntry entry in deck.AllEntries)
        {
            entry.cardNumber = (entry.cardNumber ?? "").Trim().ToUpperInvariant().Replace('/', '-');
            entry.count = Math.Max(1, entry.count);
        }
        if (SleeveLibrary.GetPath(deck.deckSleeve) == null) deck.deckSleeve = null;
        if (SleeveLibrary.GetPath(deck.rideDeckSleeve) == null) deck.rideDeckSleeve = null;
        if (string.IsNullOrWhiteSpace(deck.deckName)) deck.deckName = "Imported Deck";
        return deck.AllEntries.Any(e => e.cardNumber.Length > 0) ? deck : null;
    }

    // The deck's name without the characters Windows won't have in a file name
    static string FileName(string deckName)
    {
        string name = new string((deckName ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return name.Length > 0 ? name : "Deck";
    }
}
