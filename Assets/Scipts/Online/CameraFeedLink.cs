using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/* Carries camera pictures (JPEG bytes) between the two PCs of an online match, for SplitScreenView.

 Direct: the host listens on a free port and puts its addresses in the room entry ("video");
 the guest connects. Works on one network or one PC, and needs Windows Firewall to let Unity in
 (a UWP build needs the privateNetworkClientServer capability for home/work networks).
 Each side's first message is the room code, so a wrong connection is dropped.

 Fallback, when there's no direct connection: about a picture a second through the Realtime Database, in
 an entry next to the room ("<code>_video<seat>"), which the same rules cover and the lobby's
 room search skips (it has no status). Slow, and every picture counts toward the database's download allowance. */
public class CameraFeedLink : MonoBehaviour
{
    public float directSeconds = 0.1f;   // between pictures over a direct connection
    public float fallbackSeconds = 1f;   // between pictures through the database
    public float fallbackPollSeconds = 0.4f; // between checks for the opponent's next database picture
    public float connectWaitSeconds = 8f; // before falling back

    public bool Direct => mConnection != null && mConnection.Verified && !mConnection.Closed;
    public bool UsingFallback => !Direct && Time.unscaledTime - mStartTime > connectWaitSeconds;

    // True when a new picture can go; then call Send
    public bool ReadyToSend
    {
        get
        {
            if (Time.unscaledTime < mNextSend) return false;
            if (Direct) return !mConnection.Sending;
            return UsingFallback && !mUploading;
        }
    }

    string mRoomCode;
    int mLocalSeat;
    float mStartTime;
    float mNextSend;

    TcpListener mListener;
    Connection mConnection;
    Connection mAccepted; // handed over by the accept thread
    readonly object mAcceptLock = new object();

    bool mUploading;
    long mLastFrameMs;
    byte[] mFallbackFrame;

    static string FramePath(string code, int seat) => "rooms/" + code + "_video" + seat;

    void Start()
    {
        // Kept, since OnlineMatch ends the MatchSession when the scene closes
        mRoomCode = MatchSession.RoomCode;
        mLocalSeat = MatchSession.LocalSeat;
        mStartTime = Time.unscaledTime;

        if (mLocalSeat == 0) StartCoroutine(Host());
        else StartCoroutine(Join());
        StartCoroutine(FallbackDownloads());
    }

    void OnDestroy()
    {
        mConnection?.Close();
        lock (mAcceptLock) mAccepted?.Close();
        try { mListener?.Stop(); } catch (SocketException) { }
        mListener = null;
        if (mRoomCode != null && mLastFrameMs != 0) RtdbRest.DeleteWithoutWaiting(FramePath(mRoomCode, mLocalSeat));
    }

    void Update()
    {
        lock (mAcceptLock)
        {
            if (mAccepted == null) return;
            mConnection?.Close(); // the guest reconnected
            mConnection = mAccepted;
            mAccepted = null;
        }
    }

    public void Send(byte[] jpeg)
    {
        if (Direct)
        {
            mConnection.Send(jpeg);
            mNextSend = Time.unscaledTime + directSeconds;
        }
        else if (UsingFallback)
        {
            mNextSend = Time.unscaledTime + fallbackSeconds;
            StartCoroutine(Upload(jpeg));
        }
    }

    // The newest picture from the opponent since the last call, or null
    public byte[] TakeLatest()
    {
        byte[] frame = Direct ? mConnection.TakeIncoming() : null;
        if (frame != null) return frame;

        frame = mFallbackFrame;
        mFallbackFrame = null;
        return frame;
    }

    // Direct connection

    IEnumerator Host()
    {
        try
        {
            mListener = new TcpListener(IPAddress.Any, 0);
            mListener.Start();
        }
        catch (SocketException e)
        {
            Debug.LogWarning("[CameraFeedLink] Couldn't listen for the guest (" + e.Message + "); pictures go through the database.");
            yield break;
        }

        int port = ((IPEndPoint)mListener.LocalEndpoint).Port;
        new Thread(AcceptLoop) { IsBackground = true, Name = "CameraFeed accept" }.Start(mListener);

        var video = new Dictionary<string, object> { ["port"] = (long)port, ["ips"] = LocalAddresses().Cast<object>().ToList() };
        RtdbRest.Result result = null;
        yield return RtdbRest.Patch("rooms/" + mRoomCode, new Dictionary<string, object> { ["video"] = video }, new[] { "video" }, r => result = r);
        Debug.Log("[CameraFeedLink] Listening on port " + port + (result.succeeded ? "." : ", but couldn't tell the guest: " + result.error));
    }

    void AcceptLoop(object state)
    {
        TcpListener listener = (TcpListener)state;
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception) { return; } // stopped

            var connection = new Connection(client, mRoomCode);
            lock (mAcceptLock)
            {
                mAccepted?.Close();
                mAccepted = connection;
            }
        }
    }

    IEnumerator Join()
    {
        while (true)
        {
            if (Direct)
            {
                yield return new WaitForSecondsRealtime(1f);
                continue;
            }

            RtdbRest.Result room = null;
            yield return RtdbRest.Get("rooms/" + mRoomCode, r => room = r);
            Dictionary<string, object> video = room.succeeded ? room.fields.GetMap("video") : null;

            if (video != null)
            {
                List<string> ips = (video.GetList("ips") ?? new List<object>()).OfType<string>().ToList();
                int port = (int)video.GetLong("port");

                Task<TcpClient> attempt = Task.Run(() => TryConnect(ips, port));
                yield return new WaitUntil(() => attempt.IsCompleted);

                if (attempt.Result != null)
                {
                    mConnection?.Close();
                    mConnection = new Connection(attempt.Result, mRoomCode);
                    yield return new WaitForSecondsRealtime(2f); // time for the hello to come back
                    if (Direct) Debug.Log("[CameraFeedLink] Connected straight to the host.");
                }
            }
            yield return new WaitForSecondsRealtime(Direct ? 1f : 5f);
        }
    }

    /* Tries every address at once, so the host's virtual and VPN adapters don't each cost a wait;
     the first that connects wins */
    static TcpClient TryConnect(List<string> ips, int port)
    {
        var attempts = new List<(TcpClient client, Task task)>();
        foreach (string ip in ips)
        {
            var client = new TcpClient();
            try { attempts.Add((client, client.ConnectAsync(ip, port))); }
            catch (Exception) { client.Close(); } // a bad address
        }

        TcpClient winner = null;
        DateTime giveUp = DateTime.UtcNow.AddSeconds(3);
        while (winner == null && DateTime.UtcNow < giveUp && attempts.Any(a => !a.task.IsCompleted))
        {
            Thread.Sleep(20);
            winner = attempts.FirstOrDefault(a => a.task.Status == TaskStatus.RanToCompletion && a.client.Connected).client;
        }
        winner ??= attempts.FirstOrDefault(a => a.task.Status == TaskStatus.RanToCompletion && a.client.Connected).client;

        foreach (var attempt in attempts)
        {
            if (attempt.client == winner) continue;
            attempt.task.ContinueWith(_ => { }); // its refusal isn't an unobserved exception
            attempt.client.Close();
        }
        return winner;
    }

    // Loopback first, for two editor windows on one PC, then this PC's network addresses
    static List<string> LocalAddresses()
    {
        var addresses = new List<string> { "127.0.0.1" };
#if ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
        // UWP: System.Net.NetworkInformation may come back empty there
        try
        {
            foreach (Windows.Networking.HostName host in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
                if (host.Type == Windows.Networking.HostNameType.Ipv4) addresses.Add(host.CanonicalName);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CameraFeedLink] Couldn't list UWP network addresses: " + e.Message);
        }
#endif
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork) addresses.Add(address.Address.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CameraFeedLink] Couldn't list network addresses: " + e.Message);
        }
        if (addresses.Count == 1) Debug.LogWarning("[CameraFeedLink] Found no network address; only another window on this PC can connect directly.");
        return addresses.Distinct().ToList();
    }

    // Database fallback

    IEnumerator Upload(byte[] jpeg)
    {
        mUploading = true;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fields = new Dictionary<string, object> { ["jpg"] = Convert.ToBase64String(jpeg), ["ms"] = now };
        RtdbRest.Result result = null;
        yield return RtdbRest.Patch(FramePath(mRoomCode, mLocalSeat), fields, new[] { "jpg", "ms" }, r => result = r);
        if (result.succeeded) mLastFrameMs = now;
        mUploading = false;
    }

    /* Polls the tiny "ms" stamp often and downloads the picture only when it changed, so a new one
     shows soon after it's sent and an unchanged one isn't downloaded again */
    IEnumerator FallbackDownloads()
    {
        string path = FramePath(mRoomCode, 1 - mLocalSeat);
        long lastSeen = 0;
        while (true)
        {
            yield return new WaitForSecondsRealtime(fallbackPollSeconds);
            if (!UsingFallback) continue;

            RtdbRest.Result stamp = null;
            yield return RtdbRest.Get(path + "/ms", r => stamp = r);
            // A single number comes back as the read's version, not as fields
            if (!stamp.succeeded || !long.TryParse(stamp.version, out long ms) || ms == lastSeen) continue;

            RtdbRest.Result frame = null;
            yield return RtdbRest.Get(path, r => frame = r);
            if (!frame.succeeded) continue;

            lastSeen = frame.fields.GetLong("ms");
            try { mFallbackFrame = Convert.FromBase64String(frame.fields.GetString("jpg") ?? ""); }
            catch (FormatException) { }
        }
    }

    /* One TCP connection: messages are a 4-byte length then the bytes. Only the newest picture is
     kept each way, so a slow link drops pictures instead of falling behind. */
    class Connection
    {
        const int MaxMessage = 8 * 1024 * 1024;

        readonly TcpClient mClient;
        readonly NetworkStream mStream;
        readonly byte[] mHello;
        readonly object mLock = new object();
        readonly AutoResetEvent mWake = new AutoResetEvent(false);
        byte[] mOutgoing;
        byte[] mIncoming;

        public volatile bool Closed;
        public volatile bool Verified; // the other side sent the right room code

        public bool Sending
        {
            get { lock (mLock) return mOutgoing != null; }
        }

        public Connection(TcpClient client, string roomCode)
        {
            mClient = client;
            mClient.NoDelay = true;
            mStream = client.GetStream();
            mHello = Encoding.UTF8.GetBytes(roomCode);
            new Thread(ReadLoop) { IsBackground = true, Name = "CameraFeed read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "CameraFeed write" }.Start();
        }

        public void Send(byte[] data)
        {
            lock (mLock) mOutgoing = data;
            mWake.Set();
        }

        public byte[] TakeIncoming()
        {
            lock (mLock)
            {
                byte[] data = mIncoming;
                mIncoming = null;
                return data;
            }
        }

        public void Close()
        {
            Closed = true;
            try { mClient.Close(); } catch (Exception) { }
            mWake.Set();
        }

        void ReadLoop()
        {
            try
            {
                if (!Read().SequenceEqual(mHello)) throw new InvalidOperationException("wrong room");
                Verified = true;
                while (!Closed)
                {
                    byte[] data = Read();
                    lock (mLock) mIncoming = data;
                }
            }
            catch (Exception) { }
            Close();
        }

        void WriteLoop()
        {
            try
            {
                Write(mHello);
                while (!Closed)
                {
                    mWake.WaitOne(500);
                    byte[] data;
                    lock (mLock) data = mOutgoing;
                    if (data == null) continue;

                    Write(data);
                    lock (mLock)
                        if (mOutgoing == data) mOutgoing = null;
                }
            }
            catch (Exception) { }
            Close();
        }

        byte[] Read()
        {
            byte[] header = ReadExactly(4);
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 0 || length > MaxMessage) throw new InvalidOperationException("bad length");
            return ReadExactly(length);
        }

        byte[] ReadExactly(int count)
        {
            byte[] data = new byte[count];
            for (int read = 0; read < count;)
            {
                int n = mStream.Read(data, read, count - read);
                if (n <= 0) throw new InvalidOperationException("closed");
                read += n;
            }
            return data;
        }

        void Write(byte[] data)
        {
            int n = data.Length;
            mStream.Write(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n }, 0, 4);
            mStream.Write(data, 0, n);
        }
    }
}
