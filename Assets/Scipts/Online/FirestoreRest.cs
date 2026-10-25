using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/* Small Firestore REST client for the online match (lobby rooms and board sync). REST, not the
 Firebase SDK, because the project builds for UWP where the SDK isn't supported, same as
 CardFirestoreDatabase. Values go in and out as plain C#: string, long, double, bool,
 Dictionary<string, object> and List<object>. Run the IEnumerators with StartCoroutine. */
public static class FirestoreRest
{
    public class Result
    {
        public bool ok;
        public long status; // HTTP code, 0 if the server was never reached
        public string error;
        public string id; // document ID, e.g. the room code
        public string updateTime; // pass back as requireUpdateTime to only write if nobody else has since
        public Dictionary<string, object> fields;
    }

    static string sDocumentsUrl; // .../v1/projects/{projectId}/databases/(default)/documents
    static string sApiKey;

    // Reads the project ID and web API key from google-services.json; false if it's missing
    public static bool Init()
    {
        if (sDocumentsUrl != null) return true;

        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "google-services.json");
            JObject config = JObject.Parse(File.ReadAllText(path));

            string projectId = (string)config["project_info"]["project_id"];
            sApiKey = (string)config["client"][0]["api_key"][0]["current_key"];
            sDocumentsUrl = "https://firestore.googleapis.com/v1/projects/" + projectId + "/databases/(default)/documents";
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[FirestoreRest] Couldn't read google-services.json: " + e.Message);
            return false;
        }
    }

    // path like "rooms/ABCDE"
    public static IEnumerator Get(string path, Action<Result> done) =>
        Send("GET", Url(path), null, request => done(ToResult(request)));

    // Fails (409) if the document already exists
    public static IEnumerator Create(string collection, string id, Dictionary<string, object> fields, Action<Result> done) =>
        Send("POST", Url(collection, "documentId=" + Uri.EscapeDataString(id)), new JObject { ["fields"] = ToFields(fields) },
            request => done(ToResult(request)));

    /* Writes only the fields named in updateMask; dotted paths like "host.beatMs" reach into maps.
     With requireUpdateTime the write fails unless the document is unchanged since that read. */
    public static IEnumerator Patch(string path, Dictionary<string, object> fields, IEnumerable<string> updateMask,
        Action<Result> done, string requireUpdateTime = null)
    {
        string query = string.Join("&", updateMask.Select(f => "updateMask.fieldPaths=" + Uri.EscapeDataString(f)));
        if (requireUpdateTime != null) query += "&currentDocument.updateTime=" + Uri.EscapeDataString(requireUpdateTime);

        return Send("PATCH", Url(path, query), new JObject { ["fields"] = ToFields(fields) }, request => done?.Invoke(ToResult(request)));
    }

    public static IEnumerator Delete(string path, Action<Result> done = null) =>
        Send("DELETE", Url(path), null, request => done?.Invoke(ToResult(request)));

    // For OnDestroy/quit, where a coroutine can't run: sent without waiting for the answer
    public static void PatchWithoutWaiting(string path, Dictionary<string, object> fields, IEnumerable<string> updateMask)
    {
        if (sDocumentsUrl == null) return;
        string query = string.Join("&", updateMask.Select(f => "updateMask.fieldPaths=" + Uri.EscapeDataString(f)));
        UnityWebRequest request = new UnityWebRequest(Url(path, query), "PATCH")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(new JObject { ["fields"] = ToFields(fields) }.ToString(Formatting.None))),
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SendWebRequest().completed += _ => request.Dispose();
    }

    public static void DeleteWithoutWaiting(string path)
    {
        if (sDocumentsUrl == null) return;
        UnityWebRequest request = UnityWebRequest.Delete(Url(path));
        request.SendWebRequest().completed += _ => request.Dispose();
    }

    // Documents in a collection whose fields equal all the given values; an empty list on failure
    public static IEnumerator Query(string collection, Dictionary<string, object> equalTo, int limit, Action<List<Result>> done)
    {
        JArray filters = new JArray(equalTo.Select(pair => new JObject
        {
            ["fieldFilter"] = new JObject
            {
                ["field"] = new JObject { ["fieldPath"] = pair.Key },
                ["op"] = "EQUAL",
                ["value"] = ToValue(pair.Value),
            },
        }));

        JObject body = new JObject
        {
            ["structuredQuery"] = new JObject
            {
                ["from"] = new JArray(new JObject { ["collectionId"] = collection }),
                ["where"] = new JObject { ["compositeFilter"] = new JObject { ["op"] = "AND", ["filters"] = filters } },
                ["limit"] = limit,
            },
        };

        return Send("POST", sDocumentsUrl + ":runQuery?key=" + sApiKey, body, request =>
        {
            var results = new List<Result>();
            if (request == null) // no database settings, already logged by Init
            {
                done(results);
                return;
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                // One entry per match; with no match the only entry is {"readTime": ...}
                foreach (JToken entry in ParseJson(request.downloadHandler.text))
                    if (entry["document"] != null)
                        results.Add(FromDocument(entry["document"]));
            }
            else
            {
                Debug.LogWarning("[FirestoreRest] Query on " + collection + " failed: " + request.error + " " + request.downloadHandler.text);
            }
            done(results);
        });
    }

    // Same encoding Firestore stores, as a string: equal strings mean equal values
    public static string ToJson(object value) => ToValue(value).ToString(Formatting.None);

    static string Url(string path, string query = null) =>
        sDocumentsUrl + "/" + path + "?key=" + sApiKey + (string.IsNullOrEmpty(query) ? "" : "&" + query);

    static IEnumerator Send(string method, string url, JObject body, Action<UnityWebRequest> done)
    {
        if (sDocumentsUrl == null && !Init())
        {
            done(null);
            yield break;
        }

        using (UnityWebRequest request = new UnityWebRequest(url, method))
        {
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;

            yield return request.SendWebRequest();
            done(request);
        }
    }

    static Result ToResult(UnityWebRequest request)
    {
        if (request == null) return new Result { error = "No database settings" };

        bool ok = request.result == UnityWebRequest.Result.Success;
        string text = request.downloadHandler?.text;

        Result result = ok && !string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")
            ? FromDocument(ParseJson(text))
            : new Result();

        result.ok = ok;
        result.status = request.responseCode;
        if (!ok) result.error = request.error + " " + text;
        return result;
    }

    /* Leaves timestamps as the exact text Firestore sent. By default Newtonsoft turns them into
     DateTimes, which print back in another format without the microseconds, so an updateTime
     sent back as a precondition would never match. */
    static JToken ParseJson(string text)
    {
        using (JsonTextReader reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            return JToken.ReadFrom(reader);
    }

    static Result FromDocument(JToken doc)
    {
        string name = (string)doc["name"] ?? "";
        return new Result
        {
            ok = true,
            id = name.Substring(name.LastIndexOf('/') + 1),
            updateTime = (string)doc["updateTime"],
            fields = FromFields(doc["fields"]),
        };
    }

    // Encoding: REST wraps each value in its type, e.g. {"integerValue": "3"} (int64s as strings)

    static JObject ToFields(IDictionary<string, object> map)
    {
        JObject fields = new JObject();
        foreach (KeyValuePair<string, object> pair in map) fields[pair.Key] = ToValue(pair.Value);
        return fields;
    }

    static JObject ToValue(object value)
    {
        switch (value)
        {
            case null: return new JObject { ["nullValue"] = null };
            case string s: return new JObject { ["stringValue"] = s };
            case bool b: return new JObject { ["booleanValue"] = b };
            case int i: return new JObject { ["integerValue"] = i.ToString() };
            case long l: return new JObject { ["integerValue"] = l.ToString() };
            case float f: return new JObject { ["doubleValue"] = f };
            case double d: return new JObject { ["doubleValue"] = d };
            case IDictionary<string, object> map: return new JObject { ["mapValue"] = new JObject { ["fields"] = ToFields(map) } };
            case IEnumerable<object> list: return new JObject { ["arrayValue"] = new JObject { ["values"] = new JArray(list.Select(ToValue)) } };
            default: throw new ArgumentException("Firestore can't store a " + value.GetType().Name);
        }
    }

    static Dictionary<string, object> FromFields(JToken fields)
    {
        var map = new Dictionary<string, object>();
        if (fields is JObject obj)
            foreach (JProperty property in obj.Properties()) map[property.Name] = FromValue(property.Value);
        return map;
    }

    static object FromValue(JToken value)
    {
        JProperty typed = (value as JObject)?.Properties().FirstOrDefault();
        if (typed == null) return null;

        switch (typed.Name)
        {
            case "stringValue": return (string)typed.Value;
            case "integerValue": return long.Parse((string)typed.Value);
            case "doubleValue": return (double)typed.Value;
            case "booleanValue": return (bool)typed.Value;
            case "mapValue": return FromFields(typed.Value["fields"]);
            case "arrayValue": return (typed.Value["values"] as JArray ?? new JArray()).Select(FromValue).ToList();
            case "timestampValue": return (string)typed.Value;
            default: return null;
        }
    }

    // Reading helpers for the decoded fields

    public static string GetString(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as string : null;

    public static long GetLong(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? (v is long l ? l : v is double d ? (long)d : 0) : 0;

    public static bool GetBool(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) && v is bool b && b;

    public static Dictionary<string, object> GetMap(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

    public static List<object> GetList(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as List<object> : null;
}
