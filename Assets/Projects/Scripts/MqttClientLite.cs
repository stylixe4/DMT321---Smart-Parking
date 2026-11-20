using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

// MQTT 3.1.1 client แบบเบา ไม่ต้องลงแพ็กเกจเพิ่ม (ใช้ TcpClient ของ .NET)
// - เชื่อมต่อ TCP (1883) หรือ TLS (8883), username/password, Last Will
// - Publish QoS 0 (+ retain), Subscribe QoS 0, keep-alive (PINGREQ) และต่อใหม่อัตโนมัติ
// - ทำงานบนเธรดเบื้องหลัง: ข้อความขาเข้าถูกเก็บในคิว ให้ฝั่ง Unity ดึงไปใช้ใน Update() ผ่าน TryReceive()
// หมายเหตุ: WebGL ใช้ TCP socket ไม่ได้ (ต้องใช้ MQTT over WebSocket แทน)
public sealed class MqttClientLite : IDisposable
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }

    public struct Message
    {
        public string Topic;
        public byte[] Payload;
        public bool Retained;
        public string Text => Payload == null ? string.Empty : Encoding.UTF8.GetString(Payload);
    }

    public class Options
    {
        public string Host = "broker.hivemq.com";
        public int Port = 1883;
        public bool UseTls;
        public bool AllowUntrustedCertificates;
        public string ClientId = "unity-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Username;
        public string Password;
        public ushort KeepAliveSeconds = 30;
        public bool CleanSession = true;
        public string WillTopic;
        public string WillPayload;
        public bool WillRetain = true;
        public int ConnectTimeoutMs = 8000;
        public int MaxReconnectDelaySeconds = 30;
        public int MaxQueuedMessages = 200;
    }

    // อ่านได้จากเธรดหลัก (Unity)
    public ConnectionState State => (ConnectionState)state;
    public bool IsConnected => State == ConnectionState.Connected;
    public string LastError => lastError;
    public int ReconnectCount => reconnectCount;
    public Options Settings => options;

    private readonly Options options;
    private readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<Message> incoming = new ConcurrentQueue<Message>();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private readonly List<string> subscriptions = new List<string>();
    private readonly object subscriptionLock = new object();

    private Thread loopThread;
    private volatile int state = (int)ConnectionState.Disconnected;
    private volatile string lastError = string.Empty;
    private volatile bool stopRequested;
    private volatile bool linkBroken;
    private volatile int generation;
    private int reconnectCount;
    private ushort packetId;
    private long lastReceiveTicks;

    private TcpClient tcp;
    private Stream stream;

    public MqttClientLite(Options options)
    {
        this.options = options ?? new Options();
    }

    // ---------------------------------------------------------------- API

    public void Start()
    {
        if (loopThread != null) return;
        stopRequested = false;
        loopThread = new Thread(Loop) { IsBackground = true, Name = "MqttClientLite" };
        loopThread.Start();
    }

    // หยุดแบบสุภาพ: ส่ง DISCONNECT แล้วปิดการเชื่อมต่อ (Last Will จะไม่ถูกส่ง)
    public void Stop(int waitMs = 1500)
    {
        if (loopThread == null) return;
        stopRequested = true;
        wake.Set();
        if (!loopThread.Join(waitMs)) CloseSocket(); // ปลดเธรดที่ค้างอยู่
        loopThread = null;
        state = (int)ConnectionState.Disconnected;
    }

    public void Dispose()
    {
        Stop();
    }

    public void Subscribe(string topicFilter)
    {
        if (string.IsNullOrEmpty(topicFilter)) return;
        lock (subscriptionLock)
        {
            if (subscriptions.Contains(topicFilter)) return;
            subscriptions.Add(topicFilter);
        }
        if (IsConnected) Enqueue(BuildSubscribe(new[] { topicFilter }));
    }

    public void Publish(string topic, string payload, bool retain = false)
    {
        Publish(topic, Encoding.UTF8.GetBytes(payload ?? string.Empty), retain);
    }

    public void Publish(string topic, byte[] payload, bool retain = false)
    {
        if (string.IsNullOrEmpty(topic)) return;
        if (!IsConnected) return; // ตอนหลุดไม่ต้องเก็บไว้ (เชื่อมใหม่แล้วฝั่งแอปจะส่งสถานะล่าสุดเอง)
        Enqueue(BuildPublish(topic, payload, retain));
    }

    // เรียกจาก Update() ของ Unity เพื่อดึงข้อความที่เข้ามา
    public bool TryReceive(out Message message)
    {
        return incoming.TryDequeue(out message);
    }

    // ---------------------------------------------------------------- Loop

    private void Loop()
    {
        int delaySeconds = 1;
        while (!stopRequested)
        {
            try
            {
                state = (int)(reconnectCount == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                OpenSocket();
                Handshake();

                while (outgoing.TryDequeue(out _)) { } // ทิ้งข้อความเก่าก่อนหลุด
                linkBroken = false;
                Interlocked.Exchange(ref lastReceiveTicks, DateTime.UtcNow.Ticks);

                string[] subs;
                lock (subscriptionLock) subs = subscriptions.ToArray();
                if (subs.Length > 0) Write(BuildSubscribe(subs));

                int gen = ++generation;
                Stream readStream = stream;
                var reader = new Thread(() => ReadLoop(readStream, gen)) { IsBackground = true, Name = "MqttClientLite.Read" };
                reader.Start();

                lastError = string.Empty;
                delaySeconds = 1;
                state = (int)ConnectionState.Connected;

                WriteLoop();

                if (stopRequested)
                {
                    while (outgoing.TryDequeue(out byte[] last)) TryWrite(last); // ส่งข้อความสุดท้าย (เช่น "offline") ให้ครบก่อน
                    TryWrite(new byte[] { 0xE0, 0x00 }); // DISCONNECT
                }
            }
            catch (Exception e)
            {
                lastError = Describe(e);
            }

            generation++; // ให้เธรดอ่านของรอบนี้เลิกทำงานโดยไม่เขียนทับข้อความ error
            CloseSocket();
            if (stopRequested) break;

            reconnectCount++;
            state = (int)ConnectionState.Reconnecting;
            wake.WaitOne(TimeSpan.FromSeconds(delaySeconds));
            delaySeconds = Math.Min(delaySeconds * 2, Math.Max(1, options.MaxReconnectDelaySeconds));
        }
        state = (int)ConnectionState.Disconnected;
    }

    private void OpenSocket()
    {
        tcp = new TcpClient { NoDelay = true };
        IAsyncResult ar = tcp.BeginConnect(options.Host, options.Port, null, null);
        if (!ar.AsyncWaitHandle.WaitOne(options.ConnectTimeoutMs))
        {
            throw new TimeoutException($"เชื่อมต่อ {options.Host}:{options.Port} ไม่ทันเวลา");
        }
        tcp.EndConnect(ar);

        Stream s = tcp.GetStream();
        if (options.UseTls)
        {
            var ssl = new SslStream(s, false, ValidateCertificate);
            ssl.AuthenticateAsClient(options.Host);
            s = ssl;
        }
        stream = s;
    }

    private bool ValidateCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
    {
        return errors == SslPolicyErrors.None || options.AllowUntrustedCertificates;
    }

    private void Handshake()
    {
        Write(BuildConnect());

        tcp.ReceiveTimeout = options.ConnectTimeoutMs;
        int header = ReadPacket(stream, out byte[] body);
        tcp.ReceiveTimeout = 0;

        if ((header >> 4) != 2 || body.Length < 2) throw new IOException("Broker ไม่ตอบ CONNACK");
        int code = body[1];
        if (code != 0) throw new IOException("Broker ปฏิเสธการเชื่อมต่อ: " + ConnackReason(code));
    }

    private void WriteLoop()
    {
        int keepAliveMs = Math.Max(5, (int)options.KeepAliveSeconds) * 1000;
        long lastSend = DateTime.UtcNow.Ticks;
        long lastPing = lastSend;

        while (!stopRequested && !linkBroken)
        {
            while (outgoing.TryDequeue(out byte[] packet))
            {
                Write(packet);
                lastSend = DateTime.UtcNow.Ticks;
            }

            long now = DateTime.UtcNow.Ticks;
            double sinceSend = TimeSpan.FromTicks(now - lastSend).TotalMilliseconds;
            double sinceReceive = TimeSpan.FromTicks(now - Interlocked.Read(ref lastReceiveTicks)).TotalMilliseconds;
            double sincePing = TimeSpan.FromTicks(now - lastPing).TotalMilliseconds;
            // ส่ง PINGREQ เมื่อเงียบฝั่งส่ง หรือไม่ได้ยินอะไรจาก broker นานครึ่งหนึ่งของ keep-alive
            if (sinceSend >= keepAliveMs * 0.5 || (sinceReceive >= keepAliveMs * 0.5 && sincePing >= keepAliveMs * 0.5))
            {
                Write(new byte[] { 0xC0, 0x00 }); // PINGREQ
                lastSend = lastPing = now;
            }
            if (sinceReceive > keepAliveMs * 1.5)
            {
                throw new IOException("Broker ไม่ตอบสนอง (keep-alive timeout)");
            }

            wake.WaitOne(250);
        }
        if (linkBroken && !stopRequested) throw new IOException(string.IsNullOrEmpty(lastError) ? "การเชื่อมต่อหลุด" : lastError);
    }

    // อ่านแพ็กเก็ตจาก stream ของการเชื่อมต่อรอบนี้เท่านั้น (gen กันเธรดเก่าไปยุ่งกับการเชื่อมต่อใหม่)
    private void ReadLoop(Stream s, int gen)
    {
        try
        {
            while (!stopRequested && gen == generation)
            {
                int header = ReadPacket(s, out byte[] body);
                Interlocked.Exchange(ref lastReceiveTicks, DateTime.UtcNow.Ticks);

                int type = header >> 4;
                if (type == 3) HandlePublish(header, body);
                // 9 = SUBACK, 13 = PINGRESP: แค่ถือว่า broker ยังอยู่
            }
        }
        catch (Exception e)
        {
            if (!stopRequested && gen == generation) lastError = Describe(e);
        }
        if (gen != generation) return;
        linkBroken = true;
        wake.Set();
    }

    private void HandlePublish(int header, byte[] body)
    {
        int qos = (header >> 1) & 0x03;
        bool retained = (header & 0x01) != 0;
        int topicLength = (body[0] << 8) | body[1];
        string topic = Encoding.UTF8.GetString(body, 2, topicLength);
        int offset = 2 + topicLength;

        if (qos > 0)
        {
            int id = (body[offset] << 8) | body[offset + 1];
            offset += 2;
            if (qos == 1) Enqueue(new byte[] { 0x40, 0x02, (byte)(id >> 8), (byte)id }); // PUBACK
        }

        var payload = new byte[body.Length - offset];
        Buffer.BlockCopy(body, offset, payload, 0, payload.Length);

        incoming.Enqueue(new Message { Topic = topic, Payload = payload, Retained = retained });
        while (incoming.Count > options.MaxQueuedMessages && incoming.TryDequeue(out _)) { }
    }

    // ---------------------------------------------------------------- Packets

    private byte[] BuildConnect()
    {
        var vh = new List<byte>();
        WriteString(vh, "MQTT");
        vh.Add(4); // protocol level 3.1.1

        byte flags = 0;
        if (options.CleanSession) flags |= 0x02;
        bool hasWill = !string.IsNullOrEmpty(options.WillTopic);
        if (hasWill)
        {
            flags |= 0x04;
            if (options.WillRetain) flags |= 0x20;
        }
        if (!string.IsNullOrEmpty(options.Username))
        {
            flags |= 0x80;
            if (options.Password != null) flags |= 0x40;
        }
        vh.Add(flags);
        vh.Add((byte)(options.KeepAliveSeconds >> 8));
        vh.Add((byte)options.KeepAliveSeconds);

        WriteString(vh, options.ClientId);
        if (hasWill)
        {
            WriteString(vh, options.WillTopic);
            WriteBinary(vh, Encoding.UTF8.GetBytes(options.WillPayload ?? string.Empty));
        }
        if (!string.IsNullOrEmpty(options.Username))
        {
            WriteString(vh, options.Username);
            if (options.Password != null) WriteBinary(vh, Encoding.UTF8.GetBytes(options.Password));
        }
        return Packet(0x10, vh);
    }

    private static byte[] BuildPublish(string topic, byte[] payload, bool retain)
    {
        var body = new List<byte>(topic.Length + payload.Length + 4);
        WriteString(body, topic);
        body.AddRange(payload);
        return Packet((byte)(0x30 | (retain ? 0x01 : 0x00)), body);
    }

    private byte[] BuildSubscribe(string[] filters)
    {
        var body = new List<byte>();
        ushort id = NextPacketId();
        body.Add((byte)(id >> 8));
        body.Add((byte)id);
        foreach (var f in filters)
        {
            WriteString(body, f);
            body.Add(0); // QoS 0
        }
        return Packet(0x82, body);
    }

    private ushort NextPacketId()
    {
        packetId++;
        if (packetId == 0) packetId = 1;
        return packetId;
    }

    private static byte[] Packet(byte header, List<byte> body)
    {
        var packet = new List<byte>(body.Count + 5) { header };
        int length = body.Count;
        do
        {
            byte digit = (byte)(length % 128);
            length /= 128;
            if (length > 0) digit |= 0x80;
            packet.Add(digit);
        } while (length > 0);
        packet.AddRange(body);
        return packet.ToArray();
    }

    private static void WriteString(List<byte> buffer, string value)
    {
        WriteBinary(buffer, Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static void WriteBinary(List<byte> buffer, byte[] data)
    {
        buffer.Add((byte)(data.Length >> 8));
        buffer.Add((byte)data.Length);
        buffer.AddRange(data);
    }

    // ---------------------------------------------------------------- IO

    private void Enqueue(byte[] packet)
    {
        outgoing.Enqueue(packet);
        while (outgoing.Count > options.MaxQueuedMessages && outgoing.TryDequeue(out _)) { }
        wake.Set();
    }

    private void Write(byte[] packet)
    {
        stream.Write(packet, 0, packet.Length);
        stream.Flush();
    }

    private void TryWrite(byte[] packet)
    {
        try { Write(packet); } catch { /* กำลังปิดอยู่แล้ว */ }
    }

    private static int ReadPacket(Stream s, out byte[] body)
    {
        int header = s.ReadByte();
        if (header < 0) throw new IOException("Broker ปิดการเชื่อมต่อ");

        int length = 0, multiplier = 1, digit;
        do
        {
            digit = s.ReadByte();
            if (digit < 0) throw new IOException("Broker ปิดการเชื่อมต่อ");
            length += (digit & 0x7F) * multiplier;
            multiplier *= 128;
            if (multiplier > 128 * 128 * 128 * 128) throw new IOException("ขนาดแพ็กเก็ตผิดรูปแบบ");
        } while ((digit & 0x80) != 0);

        if (length > 4 * 1024 * 1024) throw new IOException("แพ็กเก็ตใหญ่เกินไป");
        body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = s.Read(body, read, length - read);
            if (n <= 0) throw new IOException("Broker ปิดการเชื่อมต่อ");
            read += n;
        }
        return header;
    }

    private void CloseSocket()
    {
        try { stream?.Dispose(); } catch { }
        try { tcp?.Close(); } catch { }
        stream = null;
        tcp = null;
    }

    private static string ConnackReason(int code)
    {
        switch (code)
        {
            case 1: return "protocol version ไม่รองรับ";
            case 2: return "Client ID ไม่ถูกต้อง";
            case 3: return "server unavailable";
            case 4: return "username/password ผิด";
            case 5: return "ไม่ได้รับอนุญาต (not authorized)";
            default: return "code " + code;
        }
    }

    private static string Describe(Exception e)
    {
        if (e is SocketException se) return $"Socket: {se.SocketErrorCode}";
        if (e is AggregateException ae && ae.InnerException != null) return Describe(ae.InnerException);
        return e.Message;
    }
}
