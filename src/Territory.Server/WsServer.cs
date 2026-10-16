// Dependency-free WebSocket + static-file server (RFC 6455 server side).
// No NuGet packages: the same files must compile under Unity, so only
// BCL APIs are used. Serves wwwroot over HTTP and upgrades to WebSocket
// on the same port, exactly like territory/server/index.js.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Territory.Server
{
    public sealed class WsConnection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly object _sendGate = new object();
        private bool _open = true;

        public WsConnection(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public void SendText(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            byte[] header;
            if (payload.Length < 126) header = new byte[] { 0x81, (byte)payload.Length };
            else if (payload.Length < 65536)
            {
                header = new byte[] { 0x81, 126, (byte)(payload.Length >> 8), (byte)payload.Length };
            }
            else
            {
                header = new byte[10];
                header[0] = 0x81; header[1] = 127;
                long len = payload.Length;
                for (int i = 0; i < 8; i += 1) header[2 + i] = (byte)(len >> (56 - i * 8));
            }
            lock (_sendGate)
            {
                if (!_open) return;
                try
                {
                    _stream.Write(header, 0, header.Length);
                    if (payload.Length > 0) _stream.Write(payload, 0, payload.Length);
                }
                catch { _open = false; }
            }
        }

        public void SendPong(byte[] payload)
        {
            lock (_sendGate)
            {
                if (!_open) return;
                try
                {
                    _stream.Write(new byte[] { 0x8A, (byte)payload.Length }, 0, 2);
                    if (payload.Length > 0) _stream.Write(payload, 0, payload.Length);
                }
                catch { _open = false; }
            }
        }

        // Reads one frame. Returns false when the connection is done.
        // Collects fragmented messages; control frames are handled inline.
        public bool TryReadMessage(out string text, out bool closed)
        {
            text = null; closed = false;
            var message = new MemoryStream();
            int opcode = -1;
            while (true)
            {
                int b0 = _stream.ReadByte();
                int b1 = _stream.ReadByte();
                if (b0 < 0 || b1 < 0) { closed = true; return false; }
                bool fin = (b0 & 0x80) != 0;
                int op = b0 & 0x0F;
                bool masked = (b1 & 0x80) != 0;
                long len = b1 & 0x7F;
                if (len == 126)
                {
                    int h1 = _stream.ReadByte(), h2 = _stream.ReadByte();
                    if (h1 < 0 || h2 < 0) { closed = true; return false; }
                    len = (h1 << 8) | h2;
                }
                else if (len == 127)
                {
                    len = 0;
                    for (int i = 0; i < 8; i += 1)
                    {
                        int hb = _stream.ReadByte();
                        if (hb < 0) { closed = true; return false; }
                        len = (len << 8) | hb;
                    }
                }
                if (!masked) { closed = true; return false; } // clients must mask
                if (len > 4 * 1024 * 1024) { closed = true; return false; }
                byte[] mask = new byte[4];
                if (!ReadExact(mask, 4)) { closed = true; return false; }
                byte[] payload = new byte[len];
                if (!ReadExact(payload, (int)len)) { closed = true; return false; }
                for (long i = 0; i < len; i += 1) payload[i] ^= mask[i % 4];

                if (op == 0x8) // close
                {
                    try
                    {
                        lock (_sendGate)
                        {
                            _stream.Write(new byte[] { 0x88, (byte)Math.Min(payload.Length, 125) }, 0, 2);
                            if (payload.Length > 0) _stream.Write(payload, 0, Math.Min(payload.Length, 125));
                        }
                    }
                    catch { }
                    closed = true;
                    return false;
                }
                if (op == 0x9) { SendPong(payload); continue; } // ping
                if (op == 0xA) continue; // pong
                if (op == 0x1 || op == 0x2) // text or binary
                {
                    if (op == 0x1)
                    {
                        if (opcode == -1) opcode = op;
                        message.Write(payload, 0, payload.Length);
                    }
                    if (fin)
                    {
                        if (opcode == 0x1) text = Encoding.UTF8.GetString(message.ToArray());
                        return true;
                    }
                    continue;
                }
            }
        }

        private bool ReadExact(byte[] buf, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n;
                try { n = _stream.Read(buf, got, count - got); }
                catch { return false; }
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        public void Dispose()
        {
            _open = false;
            try { _client.Close(); } catch { }
        }
    }

    public sealed class HttpRequest
    {
        public string Method = "";
        public string Path = "";
        public Dictionary<string, string> Headers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    // Minimal HTTP server: WebSocket upgrades go to the game host,
    // everything else serves static files from wwwroot.
    public sealed class WsServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _wwwroot;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<Task> _tasks = new List<Task>();

        public Action<WsConnection> OnConnect;
        public Action<WsConnection, string> OnMessage;
        public Action<WsConnection> OnClose;

        private static readonly Dictionary<string, string> ContentTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ".html", "text/html; charset=utf-8" },
                { ".js", "text/javascript; charset=utf-8" },
                { ".css", "text/css; charset=utf-8" },
                { ".json", "application/json" },
                { ".wasm", "application/wasm" },
                { ".data", "application/octet-stream" },
                { ".png", "image/png" },
                { ".jpg", "image/jpeg" },
                { ".svg", "image/svg+xml" },
                { ".ico", "image/x-icon" },
            };

        public WsServer(string wwwroot, int port) { _wwwroot = wwwroot; _listener = new TcpListener(IPAddress.Any, port); }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Start()
        {
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _tasks.Add(Task.Run(AcceptLoop));
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client = null;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { break; }
                var c = client;
                _tasks.Add(Task.Run(() => Handle(c)));
            }
        }

        private void Handle(TcpClient client)
        {
            var conn = new WsConnection(client);
            HttpRequest req;
            try { req = ReadRequest(client); }
            catch { try { client.Close(); } catch { } return; }
            if (req == null) { try { client.Close(); } catch { } return; }

            string upgrade;
            string key;
            if (req.Headers.TryGetValue("Upgrade", out upgrade) &&
                upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase) &&
                req.Headers.TryGetValue("Sec-WebSocket-Key", out key))
            {
                try
                {
                    string accept = Convert.ToBase64String(SHA1.HashData(
                        Encoding.ASCII.GetBytes(key.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    string response =
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
                    byte[] bytes = Encoding.ASCII.GetBytes(response);
                    client.GetStream().Write(bytes, 0, bytes.Length);
                }
                catch { try { client.Close(); } catch { } return; }
                try { OnConnect?.Invoke(conn); } catch { }
                try
                {
                    while (true)
                    {
                        string text;
                        bool closed;
                        if (!conn.TryReadMessage(out text, out closed)) break;
                        if (text != null)
                        {
                            try { OnMessage?.Invoke(conn, text); } catch { }
                        }
                    }
                }
                finally
                {
                    try { OnClose?.Invoke(conn); } catch { }
                    conn.Dispose();
                }
                return;
            }

            ServeFile(client, req);
            try { client.Close(); } catch { }
        }

        private static HttpRequest ReadRequest(TcpClient client)
        {
            var stream = client.GetStream();
            var buf = new MemoryStream();
            var one = new byte[1];
            // Read until the end of the headers.
            while (true)
            {
                int n;
                try
                {
                    stream.ReadTimeout = 10000;
                    n = stream.Read(one, 0, 1);
                }
                catch { return null; }
                if (n <= 0) return null;
                buf.WriteByte(one[0]);
                if (buf.Length > 65536) return null;
                if (buf.Length >= 4)
                {
                    var arr = buf.GetBuffer();
                    long l = buf.Length;
                    if (arr[l - 4] == '\r' && arr[l - 3] == '\n' && arr[l - 2] == '\r' && arr[l - 1] == '\n') break;
                }
            }
            string head = Encoding.ASCII.GetString(buf.ToArray());
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;
            var req = new HttpRequest { Method = parts[0], Path = parts[1] };
            for (int i = 1; i < lines.Length; i += 1)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) req.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            return req;
        }

        private void ServeFile(TcpClient client, HttpRequest req)
        {
            try
            {
                if (req.Method != "GET" && req.Method != "HEAD")
                {
                    WriteStatus(client, 405, "text/plain; charset=utf-8", "method not allowed");
                    return;
                }
                string raw = req.Path.Split('?')[0];
                string decoded = Uri.UnescapeDataString(raw);
                if (decoded == "/") decoded = "/index.html";
                string relative = decoded.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string file = Path.GetFullPath(Path.Combine(_wwwroot, relative));
                string root = Path.GetFullPath(_wwwroot);
                if (file != root && !file.StartsWith(root + Path.DirectorySeparatorChar))
                {
                    WriteStatus(client, 403, "text/plain; charset=utf-8", "forbidden");
                    return;
                }
                // Pre-compressed Unity WebGL output: serve .gz when accepted.
                string encoding = null;
                string acceptEnc;
                if (req.Headers.TryGetValue("Accept-Encoding", out acceptEnc) && acceptEnc.Contains("gzip")
                    && File.Exists(file + ".gz")) { file += ".gz"; encoding = "gzip"; }
                if (!File.Exists(file))
                {
                    WriteStatus(client, 404, "text/plain; charset=utf-8", "not found");
                    return;
                }
                string ext = Path.GetExtension(encoding != null ? file.Substring(0, file.Length - 3) : file);
                string contentType;
                if (!ContentTypes.TryGetValue(ext, out contentType)) contentType = "application/octet-stream";
                byte[] body = File.ReadAllBytes(file);
                var sb = new StringBuilder();
                sb.Append("HTTP/1.1 200 OK\r\n");
                sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
                if (encoding != null) sb.Append("Content-Encoding: ").Append(encoding).Append("\r\n");
                sb.Append("Cache-Control: no-store\r\n");
                sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                sb.Append("Connection: close\r\n\r\n");
                byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
                var stream = client.GetStream();
                stream.Write(head, 0, head.Length);
                if (req.Method == "GET") stream.Write(body, 0, body.Length);
            }
            catch { try { WriteStatus(client, 500, "text/plain; charset=utf-8", "error"); } catch { } }
        }

        private static void WriteStatus(TcpClient client, int code, string contentType, string body)
        {
            string text = body ?? "";
            string head = $"HTTP/1.1 {code} {(code == 404 ? "Not Found" : "Error")}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(text)}\r\n" +
                "Connection: close\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(head);
            var stream = client.GetStream();
            stream.Write(h, 0, h.Length);
            byte[] b = Encoding.UTF8.GetBytes(text);
            stream.Write(b, 0, b.Length);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
        }
    }
}
