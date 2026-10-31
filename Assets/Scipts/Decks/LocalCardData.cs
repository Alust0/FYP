using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using CardFields = System.Collections.Generic.Dictionary<string, object>;

/* The deck builder's card details: the scraper's D and DZ cards, one file per set in
 StreamingAssets/Deck (made by Tools/build_card_data.py), plus index.json with the set codes and
 nations. A set's file is read the first time one of its cards is asked for.
 CardFirestoreDatabase.CacheDeck copies a match's deck cards from here into its cache. */
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
    static readonly Dictionary<string, Dictionary<string, CardFields>> sSets = new Dictionary<string, Dictionary<string, CardFields>>();

    static string Folder => Path.Combine(Application.streamingAssetsPath, "Deck");

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
        return set != null && LoadSet(set).TryGetValue(key, out fields);
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
        if (sIndex != null) return sIndex;
        sIndex = Read<Index>(Path.Combine(Folder, "index.json")) ?? new Index();
        return sIndex;
    }

    static Dictionary<string, CardFields> LoadSet(string set)
    {
        if (!sSets.TryGetValue(set, out var cards))
        {
            cards = Read<Dictionary<string, CardFields>>(Path.Combine(Folder, set + ".json")) ?? new Dictionary<string, CardFields>();
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
