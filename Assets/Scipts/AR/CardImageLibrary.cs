using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

// Finds a card's picture in StreamingAssets/cards by card number
public static class CardImageLibrary
{
    const string Folder = "cards";
    static Dictionary<string, string> sStoredPaths;
    static Task<Dictionary<string, string>> sIndexing;

    // Indexed on a worker thread at start-up, as listing ~17k pictures takes a while
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Preload()
    {
        if (sStoredPaths != null || sIndexing != null) { return; }
        string root = Root;
        sIndexing = Task.Run(() => BuildIndex(root));
    }

    static string Root => Path.Combine(Application.streamingAssetsPath, Folder);

    // Waits for the start-up index if it's still going
    static Dictionary<string, string> Paths
    {
        get
        {
            if (sStoredPaths != null) { return sStoredPaths; }
            sStoredPaths = sIndexing != null ? sIndexing.Result : BuildIndex(Root);
            return sStoredPaths;
        }
    }

    // Full path to the card's picture, or null if there isn't one
    public static string GetPath(string cardNumber)
    {
        return Paths.TryGetValue(cardNumber.Trim().Replace('/', '-'), out string path) ? path : null;
    }

    // Every card number with a picture in a set, e.g. "D-SD03" -> D-SD03-001 ... D-SD03-015
    public static List<string> CardNumbersInSet(string setCode)
    {
        string prefix = setCode.Trim() + "-";
        var numbers = new List<string>();
        foreach (string number in Paths.Keys)
            if (number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { numbers.Add(number); }
        numbers.Sort(StringComparer.OrdinalIgnoreCase);
        return numbers;
    }

    // Every card number with a picture, sorted
    public static List<string> AllCardNumbers()
    {
        var numbers = new List<string>(Paths.Keys);
        numbers.Sort(StringComparer.OrdinalIgnoreCase);
        return numbers;
    }

    // How many card pictures there are; the lobby shows it so players can spot different card sets
    public static int Count
    {
        get
        {
                return Paths.Count;
        }
    }

    // Every set code with pictures, e.g. "D-SD01", from card numbers minus their last "-number"
    public static List<string> SetCodes()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string number in Paths.Keys)
        {
            int dash = number.LastIndexOf('-');
            if (dash > 0) { codes.Add(number.Substring(0, dash)); }
        }
        return new List<string>(codes);
    }

    // True for sideways pictures; reads the size straight from the PNG header
    public static bool IsLandscape(string path)
    {
        byte[] header = new byte[24];
        using (FileStream stream = File.OpenRead(path))
            if (stream.Read(header, 0, header.Length) < header.Length) { return false; }

        int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return width > height;
    }

    static Dictionary<string, string> BuildIndex(string root)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            Debug.LogError("[CardImageLibrary] No card pictures folder at " + root);
            return paths;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            paths[Path.GetFileNameWithoutExtension(file)] = file;

        Debug.Log("[CardImageLibrary] Indexed " + paths.Count + " card pictures.");
        return paths;
    }
}
