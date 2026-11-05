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

/* Small Firebase Realtime Database REST client for the online match: lobby rooms, board sync,
 account sessions and fallback camera pictures. Realtime Database is billed by data stored and
 downloaded, not per read and write like Firestore, so a sync every second doesn't use up a daily
 write limit. REST, not the Firebase SDK, because the project builds for UWP where the SDK isn't
 supported. Paths look like "rooms/ABCDE", and values go in and out as plain C#: string, long,
 double, bool, Dictionary<string, object> and List<object>. Run the IEnumerators with StartCoroutine.
 The database's rules (Firebase console) must allow rooms and sessions, with ".indexOn": ["status"]
 on rooms for the lobby's Query. */
public static class RtdbRest
{
    public class Result
    {
        public bool succeeded;
        public long status; // HTTP code, 0 if the server was never reached; 404 when nothing is there
        public string error;
        public string id; // last part of the path, e.g. the room code
        public string version; // the value as read: pass back as requireVersion to only write if nobody else has since
        public Dictionary<string, object> fields;
    }

    // One finished request, kept after the UnityWebRequest is disposed
    class Response
    {
        public bool succeeded;
        public long status;
        public string text;
        public string error;
        public string etag;
    }

    static string sBaseUrl; // https://{project}-default-rtdb.firebaseio.com

    // Reads the database URL from google-services.json; false if it's missing
    public static bool Init()
    {
        if (sBaseUrl != null) return true;
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "google-services.json");
            JToken project = JObject.Parse(File.ReadAllText(path))["project_info"];
            // Listed in a google-services.json downloaded after the database was made; otherwise the default (us-central1) one
            string url = (string)project["firebase_url"];
            if (string.IsNullOrEmpty(url)) url = "https://" + (string)project["project_id"] + "-default-rtdb.firebaseio.com";
            sBaseUrl = url.TrimEnd('/');
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[RtdbRest] Couldn't read google-services.json: " + e.Message);
            return false;
        }
    }

    // path like "rooms/ABCDE"; 404 if nothing is there
    public static IEnumerator Get(string path, Action<Result> done) =>
        Send("GET", path, null, null, null, false, r => done(ToResult(r, path, true)));

    // Fails (409) if something is already there
    public static IEnumerator Create(string collection, string id, Dictionary<string, object> fields, Action<Result> done)
    {
        string path = collection + "/" + id;
        Response read = null;
        yield return Send("GET", path, null, null, null, true, r => read = r);
        if (!read.succeeded)
        {
            done(ToResult(read, path, false));
            yield break;
        }
        if (Parse(read.text).Type != JTokenType.Null)
        {
            done(new Result { status = 409, id = id, error = path + " already exists" });
            yield break;
        }

        // Only if it's still empty: 412 means another copy wrote it in between
        Response write = null;
        yield return Send("PUT", path, null, ToToken(fields), read.etag, false, r => write = r);
        if (write.status == 412) write.status = 409;
        done(ToResult(write, path, false));
    }

    /* Writes only the fields named in updateMask, each replaced whole; dotted paths like
     "host.beatMs" reach into maps, and a field missing from fields is removed. With
     requireVersion (a Result's version) the write fails (412) unless the value is unchanged since that read. */
    public static IEnumerator Patch(string path, Dictionary<string, object> fields, IEnumerable<string> updateMask,
        Action<Result> done, string requireVersion = null)
    {
        JObject updates = Updates(fields, updateMask);
        if (requireVersion == null)
        {
            yield return Send("PATCH", path, null, updates, null, false, r => done?.Invoke(ToResult(r, path, false)));
            yield break;
        }

        /* A PATCH can't be made conditional, so: read with an ETag, check it's what the caller
         read, then PUT the whole value back with the changes, only if the ETag still matches */
        Response read = null;
        yield return Send("GET", path, null, null, null, true, r => read = r);
        if (!read.succeeded)
        {
            done?.Invoke(ToResult(read, path, false));
            yield break;
        }
        JToken current = Parse(read.text);
        if (!JToken.DeepEquals(current, Parse(requireVersion)))
        {
            done?.Invoke(new Result { status = 412, id = LastPart(path), error = path + " changed since it was read" });
            yield break;
        }

        JObject merged = current as JObject ?? new JObject();
        foreach (JProperty update in updates.Properties()) SetAt(merged, update.Name.Split('/'), update.Value);
        yield return Send("PUT", path, null, merged, read.etag, false, r => done?.Invoke(ToResult(r, path, false)));
    }

    public static IEnumerator Delete(string path, Action<Result> done = null) =>
        Send("DELETE", path, null, null, null, false, r => done?.Invoke(ToResult(r, path, false)));

    // For OnDestroy/quit, where a coroutine can't run: sent without waiting for the answer
    public static void PatchWithoutWaiting(string path, Dictionary<string, object> fields, IEnumerable<string> updateMask)
    {
        if (sBaseUrl == null) return;
        UnityWebRequest request = new UnityWebRequest(Url(path), "PATCH")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Updates(fields, updateMask).ToString(Formatting.None))),
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SendWebRequest().completed += _ => request.Dispose();
    }

    public static void DeleteWithoutWaiting(string path)
    {
        if (sBaseUrl == null) return;
        UnityWebRequest request = UnityWebRequest.Delete(Url(path));
        request.SendWebRequest().completed += _ => request.Dispose();
    }

    /* Children of a path whose fields equal all the given values; an empty list on failure.
     The database filters on the first one (it needs an ".indexOn" rule for it), the rest are checked here. */
    public static IEnumerator Query(string collection, Dictionary<string, object> equalTo, int limit, Action<List<Result>> done)
    {
        KeyValuePair<string, object> first = equalTo.First();
        string query = "orderBy=" + Uri.EscapeDataString("\"" + first.Key + "\"") +
            "&equalTo=" + Uri.EscapeDataString(ToJson(first.Value)) + "&limitToFirst=" + limit;

        return Send("GET", collection, query, null, null, false, r =>
        {
            var results = new List<Result>();
            if (!r.succeeded)
            {
                Debug.LogWarning("[RtdbRest] Query on " + collection + " failed (" + r.status + "): " + r.error + " " + r.text);
                done(results);
                return;
            }

            if (Parse(r.text) is JObject children)
            {
                foreach (JProperty child in children.Properties())
                {
                    if (!(child.Value is JObject value)) continue;
                    if (!equalTo.All(pair => JToken.DeepEquals(value[pair.Key], ToToken(pair.Value)))) continue;
                    results.Add(new Result
                    {
                        succeeded = true,
                        status = r.status,
                        id = child.Name,
                        version = value.ToString(Formatting.None),
                        fields = (Dictionary<string, object>)FromToken(value),
                    });
                }
            }
            done(results);
        });
    }

    // As stored, as a string: equal strings mean equal values
    public static string ToJson(object value) => ToToken(value).ToString(Formatting.None);

    /* Listens to path with the database's streaming (server-sent events), so changes arrive as they're
     written instead of by polling. onChange gets each "put" or "patch" as the changed path inside the
     listened one ("/" for all of it, "/jpg" for one field) and its value. Runs until the connection
     ends or stop() is true; done gets whether anything arrived (the server sends the current value
     straight away, so false means streaming doesn't work here). */
    public static IEnumerator Listen(string path, Action<string, JToken> onChange, Func<bool> stop, Action<bool> done)
    {
        if (sBaseUrl == null && !Init())
        {
            done(false);
            yield break;
        }

        var events = new EventStream();
        using (UnityWebRequest request = new UnityWebRequest(Url(path), "GET", events, null))
        {
            request.SetRequestHeader("Accept", "text/event-stream");
            UnityWebRequestAsyncOperation sending = request.SendWebRequest();
            while (true)
            {
                // Handed over here, on the main thread, whichever thread the bytes came in on
                foreach ((string name, string data) in events.Take())
                {
                    if (name != "put" && name != "patch") continue; // keep-alive, cancel, auth_revoked
                    JObject change;
                    try { change = Parse(data) as JObject; }
                    catch (JsonException) { continue; }
                    if (change != null) onChange((string)change["path"] ?? "/", change["data"]);
                }
                if (sending.isDone || stop()) break;
                yield return null;
            }
            if (!sending.isDone) request.Abort();
            done(events.AnyEvent);
        }
    }

    // Splits a server-sent event stream into (event name, data) pairs
    class EventStream : DownloadHandlerScript
    {
        readonly Decoder mDecoder = Encoding.UTF8.GetDecoder();
        readonly StringBuilder mText = new StringBuilder();
        readonly List<(string, string)> mReady = new List<(string, string)>();
        readonly object mLock = new object();

        public bool AnyEvent { get; private set; }

        public EventStream() : base(new byte[64 * 1024]) { }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            char[] chars = new char[mDecoder.GetCharCount(data, 0, dataLength)];
            mDecoder.GetChars(data, 0, dataLength, chars, 0);
            mText.Append(chars);

            // An event ends at a blank line
            string text = mText.ToString().Replace("\r\n", "\n");
            int end;
            while ((end = text.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
            {
                string name = null, value = null;
                foreach (string line in text.Substring(0, end).Split('\n'))
                {
                    if (line.StartsWith("event:")) name = line.Substring(6).Trim();
                    else if (line.StartsWith("data:")) value = line.Substring(5).Trim();
                }
                if (name != null)
                    lock (mLock)
                    {
                        mReady.Add((name, value));
                        AnyEvent = true;
                    }
                text = text.Substring(end + 2);
            }
            mText.Clear().Append(text);
            return true;
        }

        public List<(string, string)> Take()
        {
            lock (mLock)
            {
                var taken = new List<(string, string)>(mReady);
                mReady.Clear();
                return taken;
            }
        }
    }

    static string Url(string path, string query = null) =>
        sBaseUrl + "/" + path + ".json" + (string.IsNullOrEmpty(query) ? "" : "?" + query);

    static string LastPart(string path) => path.Substring(path.LastIndexOf('/') + 1);

    static IEnumerator Send(string method, string path, string query, JToken body, string ifMatch, bool wantEtag, Action<Response> done)
    {
        if (sBaseUrl == null && !Init())
        {
            done(new Response { error = "No database settings" });
            yield break;
        }

        using (UnityWebRequest request = new UnityWebRequest(Url(path, query), method))
        {
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (wantEtag) request.SetRequestHeader("X-Firebase-ETag", "true");
            if (ifMatch != null) request.SetRequestHeader("if-match", ifMatch);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;

            yield return request.SendWebRequest();
            done(new Response
            {
                succeeded = request.result == UnityWebRequest.Result.Success,
                status = request.responseCode == 401 ? 403 : request.responseCode, // 401 is the rules saying no
                text = request.downloadHandler?.text,
                error = request.error,
                etag = request.GetResponseHeader("ETag"),
            });
        }
    }

    // A read of nothing (null) is a 404, like a missing Firestore document
    static Result ToResult(Response response, string path, bool isRead)
    {
        var result = new Result { succeeded = response.succeeded, status = response.status, id = LastPart(path) };
        if (!response.succeeded)
        {
            result.error = response.error + " " + response.text;
            return result;
        }

        JToken value = Parse(response.text);
        if (isRead && value.Type == JTokenType.Null)
        {
            result.succeeded = false;
            result.status = 404;
            result.error = "Nothing at " + path;
            return result;
        }
        result.version = value.ToString(Formatting.None);
        result.fields = FromToken(value) as Dictionary<string, object> ?? new Dictionary<string, object>();
        return result;
    }

    // The masked fields as a multi-path update: "host.beatMs" becomes "host/beatMs", a missing one null (removed)
    static JObject Updates(Dictionary<string, object> fields, IEnumerable<string> updateMask)
    {
        var updates = new JObject();
        foreach (string field in updateMask)
        {
            object value = fields;
            foreach (string key in field.Split('.'))
                value = value is Dictionary<string, object> map && map.TryGetValue(key, out object inner) ? inner : null;
            updates[field.Replace('.', '/')] = ToToken(value);
        }
        return updates;
    }

    static void SetAt(JObject target, string[] keys, JToken value)
    {
        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (!(target[keys[i]] is JObject inner))
            {
                inner = new JObject();
                target[keys[i]] = inner;
            }
            target = inner;
        }
        if (value.Type == JTokenType.Null) target.Remove(keys[keys.Length - 1]);
        else target[keys[keys.Length - 1]] = value;
    }

    // Leaves strings that look like dates as strings
    static JToken Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return JValue.CreateNull();
        using (JsonTextReader reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            return JToken.ReadFrom(reader);
    }

    // Encoding: the database stores plain JSON, but drops nulls and empty maps and lists

    static JToken ToToken(object value)
    {
        switch (value)
        {
            case null: return JValue.CreateNull();
            case string s: return new JValue(s);
            case bool b: return new JValue(b);
            case int i: return new JValue((long)i);
            case long l: return new JValue(l);
            case float f: return new JValue((double)f);
            case double d: return new JValue(d);
            case IDictionary<string, object> map: return new JObject(map.Select(pair => new JProperty(pair.Key, ToToken(pair.Value))));
            case IEnumerable<object> list: return new JArray(list.Select(ToToken));
            default: throw new ArgumentException("The database can't store a " + value.GetType().Name);
        }
    }

    static object FromToken(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object: return ((JObject)token).Properties().ToDictionary(p => p.Name, p => FromToken(p.Value));
            case JTokenType.Array: return token.Select(FromToken).ToList();
            case JTokenType.Integer: return (long)token;
            case JTokenType.Float: return (double)token;
            case JTokenType.Boolean: return (bool)token;
            case JTokenType.String: return (string)token;
            default: return null;
        }
    }

    // Reading helpers for the decoded fields

    public static string GetString(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as string : null;

    public static long GetLong(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? (v is long l ? l : v is int i ? i : v is double d ? (long)d : 0) : 0; // int: Firestore card fields (CardFirestoreDatabase.ToFields)

    public static bool GetBool(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) && v is bool b && b;

    public static Dictionary<string, object> GetMap(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

    public static List<object> GetList(this Dictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as List<object> : null;
}
