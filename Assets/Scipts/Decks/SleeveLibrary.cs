using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/* The card sleeve pictures in StreamingAssets/Sleeves, named s0.png, s1.png and so on. A deck picks
 one for its main deck and one for its ride deck (Deck.deckSleeve / rideDeckSleeve), and
 CardBackTracker follows face-down cards in them. Ids never clash with card numbers, which have dashes. */
public static class SleeveLibrary
{
    const string Folder = "Sleeves";
    static List<string> sIds;

    static string FolderPath => Path.Combine(Application.streamingAssetsPath, Folder);

    public static bool IsSleeve(string id) =>
        !string.IsNullOrEmpty(id) && id.Length > 1 && (id[0] == 's' || id[0] == 'S') && id.Skip(1).All(char.IsDigit);

    // Every sleeve, in number order
    public static List<string> AllIds()
    {
        if (sIds != null) return sIds;
        sIds = Directory.Exists(FolderPath)
            ? Directory.GetFiles(FolderPath, "*.png").Select(Path.GetFileNameWithoutExtension).Where(IsSleeve)
                .OrderBy(id => int.Parse(id.Substring(1))).ToList()
            : new List<string>();
        return sIds;
    }

    // Full path to the sleeve's picture, or null if there isn't one
    public static string GetPath(string id)
    {
        if (!IsSleeve(id)) return null;
        string path = Path.Combine(FolderPath, id.ToLowerInvariant() + ".png");
        return File.Exists(path) ? path : null;
    }
}
