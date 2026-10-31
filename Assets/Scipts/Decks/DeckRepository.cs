using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.Core;
using UnityEngine;

/* The logged-in account's decks, kept in Unity Cloud Save so they follow the login to any PC.
 Starter decks are made from the Start/Trial Deck sets in the card pictures and never saved;
 their id is the set code, e.g. "D-SD01". */
public static class DeckRepository
{
    const string Key = "decks"; // one Cloud Save key holding every saved deck as JSON

    static readonly Regex StarterSet = new Regex(@"-(SD|TD|TTD|LTD)\d+$");

    // JsonUtility can't write a bare list
    [Serializable]
    class SavedDecks
    {
        public List<Deck> decks = new List<Deck>();
    }

    // Read once per login, then kept here
    static List<Deck> sSaved;
    static string sSavedFor;

    public static bool IsLoggedIn =>
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    // The account's saved decks; empty when not logged in. Throws if Cloud Save can't be reached
    public static async Task<List<Deck>> LoadSavedAsync()
    {
        if (!IsLoggedIn) return new List<Deck>();
        if (sSaved != null && sSavedFor == PlayerSession.Id) return sSaved;

        Dictionary<string, Item> items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { Key });
        SavedDecks saved = items.TryGetValue(Key, out Item item) ? JsonUtility.FromJson<SavedDecks>(item.Value.GetAsString()) : null;

        sSaved = saved?.decks ?? new List<Deck>();
        sSavedFor = PlayerSession.Id;
        return sSaved;
    }

    // Adds the deck, or replaces the saved one with its id. A new deck gets an id here
    public static async Task SaveAsync(Deck deck)
    {
        if (string.IsNullOrEmpty(deck.id)) deck.id = Guid.NewGuid().ToString("N");

        var decks = new List<Deck>(await LoadSavedAsync());
        int index = decks.FindIndex(d => d.id == deck.id);
        if (index >= 0) decks[index] = deck;
        else decks.Add(deck);

        await WriteAsync(decks);
    }

    public static async Task DeleteAsync(string id)
    {
        var decks = new List<Deck>(await LoadSavedAsync());
        if (decks.RemoveAll(d => d.id == id) > 0) await WriteAsync(decks);
    }

    // The cache only changes once Cloud Save has the new list
    static async Task WriteAsync(List<Deck> decks)
    {
        if (!IsLoggedIn) throw new InvalidOperationException("Log in to save decks.");

        string json = JsonUtility.ToJson(new SavedDecks { decks = decks });
        await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { [Key] = json });
        sSaved = decks;
        sSavedFor = PlayerSession.Id;
    }

    // A starter or saved deck by id; null if there's none
    public static async Task<Deck> FindAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (IsStarter(id)) return Starter(id);
        return (await LoadSavedAsync()).FirstOrDefault(d => d.id == id);
    }

    // Starter decks: every Start/Trial Deck set with pictures

    public static bool IsStarter(string id) => !string.IsNullOrEmpty(id) && StarterSet.IsMatch(id);

    public static List<Deck> Starters() => CardImageLibrary.SetCodes().Where(IsStarter).Select(Starter).ToList();

    // Every card in the set once
    public static Deck Starter(string setCode) => new Deck
    {
        id = setCode,
        deckName = (Regex.IsMatch(setCode, @"-SD\d+$") ? "Start Deck " : "Trial Deck ") + setCode,
        cards = CardImageLibrary.CardNumbersInSet(setCode).Select(number => new DeckEntry { cardNumber = number, count = 1 }).ToList(),
    };
}
