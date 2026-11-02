using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/* A one-time code that proves an email at sign-up. The code is made here and mailed by EmailJS (REST,
 free tier), set up in StreamingAssets/emailjs.json: {service_id, template_id, public_key, private_key}.
 The EmailJS template gets {{email}}, {{passcode}} and {{time}} (when the code runs out). The check
 runs in the game, so it proves the player can read that mailbox, not against a modified client. */
public class EmailOtp
{
    const string SendUrl = "https://api.emailjs.com/api/v1.0/email/send";
    const float ValidSeconds = 600f;
    const float ResendSeconds = 30f;
    const int MaxWrong = 5;

    public readonly string email;
    string mCode;
    float mExpiresAt;
    float mSentAt = float.NegativeInfinity;
    int mWrong;

    static JObject sConfig;

    public EmailOtp(string email) => this.email = email;

    public static int ValidMinutes => Mathf.RoundToInt(ValidSeconds / 60f);

    // Seconds until another code may be sent
    public float ResendIn => Mathf.Max(0f, mSentAt + ResendSeconds - Time.realtimeSinceStartup);

    // A sent code that can still be entered
    public bool Live => mCode != null && Time.realtimeSinceStartup < mExpiresAt && mWrong < MaxWrong;

    // Mails a new code (the old one stops working); null when sent, otherwise why not, for the player
    public async Task<string> SendAsync()
    {
        string problem = ReadConfig();
        if (problem != null) return problem;

        string code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
        var body = new JObject
        {
            ["service_id"] = (string)sConfig["service_id"],
            ["template_id"] = (string)sConfig["template_id"],
            ["user_id"] = (string)sConfig["public_key"],
            ["template_params"] = new JObject
            {
                ["email"] = email,
                ["to_email"] = email,
                ["passcode"] = code,
                ["time"] = DateTime.Now.AddSeconds(ValidSeconds).ToString("HH:mm"),
            },
        };
        string privateKey = (string)sConfig["private_key"];
        if (!string.IsNullOrEmpty(privateKey) && !privateKey.StartsWith("PASTE")) body["accessToken"] = privateKey;

        using (UnityWebRequest request = new UnityWebRequest(SendUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
            request.SetRequestHeader("Content-Type", "application/json");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 15;
            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError)
                return "Can't reach the email service. Check your internet connection.";
            if (request.result != UnityWebRequest.Result.Success)
            {
                // EmailJS says why in plain text, e.g. a wrong template ID or API calls from apps turned off
                Debug.LogWarning($"[EmailOtp] EmailJS answered {request.responseCode}: {request.downloadHandler.text}");
                return "Couldn't send the code. Please try again.";
            }
        }

        mCode = code;
        mSentAt = Time.realtimeSinceStartup;
        mExpiresAt = mSentAt + ValidSeconds;
        mWrong = 0;
        return null;
    }

    // Null when the typed code is right, otherwise why not
    public string Check(string typed)
    {
        if (mCode == null) return "Press Resend for a code.";
        if (Time.realtimeSinceStartup >= mExpiresAt) return "That code has expired. Press Resend for a new one.";
        if (mWrong >= MaxWrong) return "Too many wrong codes. Press Resend for a new one.";
        if (typed.Trim() == mCode) return null;

        mWrong++;
        return mWrong >= MaxWrong ? "Too many wrong codes. Press Resend for a new one." : $"Wrong code. {MaxWrong - mWrong} tries left.";
    }

    static string ReadConfig()
    {
        if (sConfig != null) return null;
        try
        {
            JObject config = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "emailjs.json")));
            string serviceId = (string)config["service_id"];
            if (string.IsNullOrEmpty(serviceId) || serviceId.StartsWith("PASTE"))
                return "Email codes aren't set up yet (StreamingAssets/emailjs.json).";
            sConfig = config;
            return null;
        }
        catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
        {
            Debug.LogError("[EmailOtp] Couldn't read StreamingAssets/emailjs.json: " + e.Message);
            return "Email codes aren't set up yet (StreamingAssets/emailjs.json).";
        }
    }
}
