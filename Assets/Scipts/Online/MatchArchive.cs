using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/* Keeps finished matches in Firestore, matches/{auto id}: one document per player per match, with
 the summary MatchLogger works out and that player's whole log. It's one write per match, so it
 costs next to nothing of Firestore's daily writes, unlike the live sync (RtdbRest, Realtime
 Database). The local log file stays too, for when there's no connection. REST, since the Firebase
 SDK isn't supported on UWP. Firestore's rules must allow creating documents in matches. */
public static class MatchArchive
{
    const string Collection = "matches";
    const int MaxLogBytes = 900000; // Firestore's limit is 1 MiB a document; the summary is small

    static string sUrl;

    // Sent without waiting, since the match scene may be closing
    public static void Save(Dictionary<string, object> fields)
    {
        if (sUrl == null && !Init()) return;

        var body = new JObject { ["fields"] = new JObject(fields.Select(pair => new JProperty(pair.Key, ToValue(pair.Value)))) };
        var request = new UnityWebRequest(sUrl, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 20,
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SendWebRequest().completed += _ =>
        {
            if (request.result == UnityWebRequest.Result.Success) Debug.Log("[MatchArchive] Saved the match to Firestore.");
            else Debug.LogWarning("[MatchArchive] Couldn't save the match (" + request.responseCode + "): " + request.error + " " + request.downloadHandler.text);
            request.Dispose();
        };
    }

    // The log as it is, or its start if it's too long for one document
    public static string Fit(string log, out bool cut)
    {
        cut = false;
        int bytes = Encoding.UTF8.GetByteCount(log);
        while (bytes > MaxLogBytes)
        {
            log = log.Substring(0, (int)(log.Length * (MaxLogBytes / (double)bytes)) - 1);
            bytes = Encoding.UTF8.GetByteCount(log);
            cut = true;
        }
        return log;
    }

    // The project ID and web API key from google-services.json
    static bool Init()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "google-services.json");
            JObject config = JObject.Parse(File.ReadAllText(path));
            string projectId = (string)config["project_info"]["project_id"];
            string apiKey = (string)config["client"][0]["api_key"][0]["current_key"];
            sUrl = "https://firestore.googleapis.com/v1/projects/" + projectId + "/databases/(default)/documents/" + Collection + "?key=" + apiKey;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MatchArchive] Couldn't read google-services.json, so the match isn't saved online: " + e.Message);
            return false;
        }
    }

    // Firestore REST wraps each value in its type, e.g. {"integerValue": "3"} (int64s as strings)
    static JObject ToValue(object value)
    {
        switch (value)
        {
            case string s: return new JObject { ["stringValue"] = s };
            case bool b: return new JObject { ["booleanValue"] = b };
            case int i: return new JObject { ["integerValue"] = i.ToString() };
            case long l: return new JObject { ["integerValue"] = l.ToString() };
            case double d: return new JObject { ["doubleValue"] = d };
            default: return new JObject { ["nullValue"] = null };
        }
    }
}
