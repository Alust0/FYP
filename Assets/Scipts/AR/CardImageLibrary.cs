using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/* Finds a card's picture under StreamingAssets/cards by card number. Pictures sit in numbered
 set folders, named by card number with "/" as "-" (e.g. "DZ-BT01-001.png"). */
public static class CardImageLibrary
{
    const string Folder = "cards";
    static Dictionary<string, string> sPaths;

    // Full path to the card's picture, or null if there isn't one
    public static string GetPath(string cardNumber)
    {
        if (sPaths == null) BuildIndex();
        return sPaths.TryGetValue(cardNumber.Trim().Replace('/', '-'), out string path) ? path : null;
    }

    // Every card number with a picture in a set, e.g. "D-SD03" -> D-SD03-001 ... D-SD03-015
    public static List<string> CardNumbersInSet(string setCode)
    {
        if (sPaths == null) BuildIndex();
        string prefix = setCode.Trim() + "-";
        var numbers = new List<string>();
        foreach (string number in sPaths.Keys)
            if (number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) numbers.Add(number);
        numbers.Sort(StringComparer.OrdinalIgnoreCase);
        return numbers;
    }

    // Every card number with a picture, sorted
    public static List<string> AllCardNumbers()
    {
        if (sPaths == null) BuildIndex();
        var numbers = new List<string>(sPaths.Keys);
        numbers.Sort(StringComparer.OrdinalIgnoreCase);
        return numbers;
    }

    // How many card pictures there are; the lobby shows it so players can spot different card sets
    public static int Count
    {
        get
        {
            if (sPaths == null) BuildIndex();
            return sPaths.Count;
        }
    }

    // Every set code with pictures, e.g. "D-SD01", from card numbers minus their last "-number"
    public static List<string> SetCodes()
    {
        if (sPaths == null) BuildIndex();
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string number in sPaths.Keys)
        {
            int dash = number.LastIndexOf('-');
            if (dash > 0) codes.Add(number.Substring(0, dash));
        }
        return new List<string>(codes);
    }

    // True for sideways pictures; reads the size straight from the PNG header
    public static bool IsLandscape(string path)
    {
        byte[] header = new byte[24];
        using (FileStream stream = File.OpenRead(path))
            if (stream.Read(header, 0, header.Length) < header.Length) return false;

        int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return width > height;
    }

    static void BuildIndex()
    {
        sPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string root = Path.Combine(Application.streamingAssetsPath, Folder);
        if (!Directory.Exists(root))
        {
            Debug.LogError("[CardImageLibrary] No card pictures folder at " + root);
            return;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            sPaths[Path.GetFileNameWithoutExtension(file)] = file;

        Debug.Log("[CardImageLibrary] Indexed " + sPaths.Count + " card pictures.");
    }
}
