using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using CardFields = System.Collections.Generic.Dictionary<string, object>;

// Deck builder card data from StreamingAssets/Deck
public static class LocalCardData
{
    class Index
    {
        public List<string> sets = new List<string>();
        public List<string> nations = new List<string>();
        public List<string> rarities = new List<string>();
    }

    static Index sIndex;
    static List<string> sSetsByLengthDesc;
    static readonly Dictionary<string, Dictionary<string, CardFields>> sSets = new Dictionary<string, Dictionary<string, CardFields>>(); // locked: Preload fills it on a worker thread
    static Task sPreload;
    static string sFolder;

    static string Folder => sFolder ??= Path.Combine(Application.streamingAssetsPath, "Deck");

    // Reads every set on a worker thread, so filtering every set doesn't freeze the screen
    public static void Preload()
    {
        if (sPreload != null) { return; }
        string folder = Folder;
        List<string> sets = LoadIndex().sets.ToList();
        sPreload = Task.Run(() =>
        {
            foreach (string set in sets) LoadSet(set, folder);
        });
    }

    // Every set is read (Preload done)
    public static bool Ready => sPreload != null && sPreload.IsCompleted;

    // Every nation of every card here, dual nations already split
    public static List<string> Nations() => LoadIndex().nations;

    // Every rarity of every card here, e.g. C, RRR, SP
    public static List<string> Rarities() => LoadIndex().rarities;

    public static bool TryGet(string cardNumber, out CardFields fields)
    {
        fields = null;
        string key = CardFirestoreDatabase.CardKey(cardNumber);
        // Longest first, so "D-BT1" can't take a "D-BT13" card
        string set = SetsByLengthDesc().FirstOrDefault(code => key.StartsWith(code + "-"));
        return set != null && LoadSet(set, Folder).TryGetValue(key, out fields);
    }

    public static bool TryGetCard(string cardNumber, out CardData card)
    {
        card = TryGet(cardNumber, out CardFields fields) ? CardFirestoreDatabase.FromFields(fields) : null;
        return card != null;
    }

    static List<string> SetsByLengthDesc() =>
        sSetsByLengthDesc ??= LoadIndex().sets.OrderByDescending(code => code.Length).ToList();

    static Index LoadIndex()
    {
        if (sIndex != null) { return sIndex; }
        sIndex = Read<Index>(Path.Combine(Folder, "index.json")) ?? new Index();
        return sIndex;
    }

    static Dictionary<string, CardFields> LoadSet(string set, string folder)
    {
        lock (sSets)
            if (sSets.TryGetValue(set, out var loaded)) { return loaded; }

        var cards = Read<Dictionary<string, CardFields>>(Path.Combine(folder, set + ".json")) ?? new Dictionary<string, CardFields>();
        lock (sSets)
        {
            if (sSets.TryGetValue(set, out var first)) { return first; } // the other thread read it meanwhile
            sSets[set] = cards;
        }
        return cards;
    }

    static T Read<T>(string path) where T : class
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LocalCardData] Couldn't read " + path + ": " + e.Message);
            return null;
        }
    }
}
