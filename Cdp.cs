// Cdp.cs — Antigravity 调试协议通道: browser 端点优先(多客户端共存), 页面端点兜底。
// 单次短连接(连接→握手→attach→evaluate→断开), 语言服务器的周期性 CDP 发现抢不走。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AntigravityAutoApprove
{
    public static class Cdp
    {
        private static int _port;
        private static string _browserPath;
        private static string _pageWs;
        private static DateTime _wsTime = DateTime.MinValue;

        private static bool RefreshWs()
        {
            try
            {
                string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Antigravity", "DevToolsActivePort");
                if (!File.Exists(pf)) return false;
                string[] lines = File.ReadAllLines(pf);
                int port;
                if (lines.Length < 1 || !int.TryParse(lines[0].Trim(), out port)) return false;
                if (lines.Length < 2 || lines[1].Trim().Length == 0) return false;
                _port = port;
                _browserPath = lines[1].Trim();

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                string body;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                { body = sr.ReadToEnd(); }
                MatchCollection wm = Regex.Matches(body, "\"webSocketDebuggerUrl\":\\s*\"(ws://[^\"]+)\"");
                for (int i = wm.Count - 1; i >= 0; i--)
                {
                    int start = Math.Max(0, wm[i].Index - 500);
                    string seg = body.Substring(start, wm[i].Index - start);
                    MatchCollection um = Regex.Matches(seg, "\"url\":\\s*\"([^\"]+)\"");
                    if (um.Count > 0 && um[um.Count - 1].Groups[1].Value.StartsWith("https://127.0.0.1"))
                    { _pageWs = wm[i].Groups[1].Value; return true; }
                }
                _pageWs = null;
                return _browserPath != null;
            }
            catch { return false; }
        }

        // 对外入口: 返回 JS 结果值字符串, 失败返回 null
        public static string Evaluate(string expr)
        {
            try
            {
                if (_port == 0 || (DateTime.UtcNow - _wsTime).TotalSeconds > 5)
                {
                    if (!RefreshWs()) return null;
                    _wsTime = DateTime.UtcNow;
                }
                string r = TryPage(expr);
                if (r != null) return r;
                return TryBrowser(expr);
            }
            catch { return null; }
        }

        // browser 端点: getTargets → attachToTarget(flatten) → evaluate(带 sessionId)
        private static string TryBrowser(string expr)
        {
            TcpClient tcp = null;
            try
            {
                tcp = ConnectHandshake("ws://127.0.0.1:" + _port + _browserPath);
                if (tcp == null) return null;
                NetworkStream ns = tcp.GetStream();

                string t = RawCall(ns, 1, "{\"method\":\"Target.getTargets\"}");
                if (t == null) { AppConfig.Log("CDP B1: getTargets null"); return null; }
                string targetId = null;
                MatchCollection tm = Regex.Matches(t, "\"targetId\":\\s*\"([^\"]+)\"[^{]*?\"url\":\\s*\"([^\"]+)\"");
                foreach (Match m in tm)
                {
                    if (m.Groups[2].Value.StartsWith("https://127.0.0.1")) { targetId = m.Groups[1].Value; break; }
                }
                if (targetId == null)
                {
                    Match single = Regex.Match(t, "\"targetId\":\\s*\"([^\"]+)\"");
                    if (!single.Success) return null;
                    targetId = single.Groups[1].Value;
                }

                string a = RawCall(ns, 2, "{\"method\":\"Target.attachToTarget\",\"params\":{\"targetId\":\"" + targetId + "\",\"flatten\":true}}");
                if (a == null) { AppConfig.Log("CDP B2: attach null"); return null; }
                Match sm = Regex.Match(a, "\"sessionId\":\\s*\"([^\"]+)\"");
                if (!sm.Success) { AppConfig.Log("CDP B3: no session"); return null; }
                string sessionId = sm.Groups[1].Value;

                string e = RawCall(ns, 3, "{\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"" + JsonEscape(expr) +
                    "\",\"returnByValue\":true,\"sessionId\":\"" + sessionId + "\"}}");
                if (e == null) { AppConfig.Log("CDP B4: eval null"); return null; }
                Match v = Regex.Match(e, "\"value\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (v.Success) return v.Groups[1].Value;
                return "OK";
            }
            catch (Exception ex) { AppConfig.Log("CDP B ERR: " + ex.GetType().Name + " " + ex.Message); return null; }
            finally { if (tcp != null) tcp.Close(); }
        }

        // 页面端点兜底: 直连 page 目标 evaluate(若语言服务器恰好未占用则成功)
        private static string TryPage(string expr)
        {
            TcpClient tcp = null;
            try
            {
                tcp = ConnectHandshake(_pageWs);
                if (tcp == null) return null;
                NetworkStream ns = tcp.GetStream();
                string e = RawCall(ns, 1, "{\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"" + JsonEscape(expr) +
                    "\",\"returnByValue\":true}}");
                if (e == null) { AppConfig.Log("CDP P1: eval null"); return null; }
                Match v = Regex.Match(e, "\"value\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (v.Success) return v.Groups[1].Value;
                return "OK";
            }
            catch (Exception ex) { AppConfig.Log("CDP P ERR: " + ex.GetType().Name + " " + ex.Message); return null; }
            finally { if (tcp != null) tcp.Close(); }
        }

        private static TcpClient ConnectHandshake(string wsUrl)
        {
            Uri uri = new Uri(wsUrl);
            TcpClient tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, uri.Port);
            NetworkStream ns = tcp.GetStream();
            ns.ReadTimeout = 2500;
            ns.WriteTimeout = 2500;
            string key = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N").Substring(0, 16)));
            string hs = "GET " + uri.PathAndQuery + " HTTP/1.1\r\n" +
                        "Host: 127.0.0.1:" + uri.Port + "\r\n" +
                        "Upgrade: websocket\r\nConnection: Upgrade\r\n" +
                        "Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(hs);
            ns.Write(hb, 0, hb.Length);
            StringBuilder head = new StringBuilder();
            byte[] one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n"))
            {
                if (ns.Read(one, 0, 1) <= 0) { AppConfig.Log("CDP HS EOF during read"); try { tcp.Close(); } catch { } return null; }
                head.Append((char)one[0]);
                if (head.Length > 8192) { try { tcp.Close(); } catch { } return null; }
            }
            string firstLine = head.ToString().Split('\n')[0].Trim();
            if (!head.ToString().Contains("101")) { AppConfig.Log("CDP HS FAIL: " + firstLine); try { tcp.Close(); } catch { } return null; }
            return tcp;
        }

        private static string RawCall(NetworkStream ns, int id, string methodJson)
        {
            string payload = "{\"id\":" + id + "," + methodJson.Substring(1, methodJson.Length - 2) + "}";
            SendFrame(ns, payload);
            DateTime deadline = DateTime.UtcNow.AddSeconds(2.5);
            while (DateTime.UtcNow < deadline)
            {
                byte[] h = ReadExact(ns, 2);
                int op = h[0] & 0x0F;
                int len = h[1] & 0x7F;
                if (len == 126) { byte[] e = ReadExact(ns, 2); len = (e[0] << 8) | e[1]; }
                else if (len == 127) { byte[] e = ReadExact(ns, 8); len = (e[4] << 24) | (e[5] << 16) | (e[6] << 8) | e[7]; }
                byte[] pl = len > 0 ? ReadExact(ns, len) : new byte[0];
                if (op == 0x8) return null;
                if (op == 0x9) continue;
                if (op != 0x1) continue;
                string msg = Encoding.UTF8.GetString(pl);
                if (Regex.IsMatch(msg, "\"id\":" + id + "[,}]")) return msg;
            }
            return null;
        }

        private static void SendFrame(NetworkStream ns, string text)
        {
            byte[] p = Encoding.UTF8.GetBytes(text);
            List<byte> f = new List<byte>();
            f.Add(0x81);
            byte mb = 0x80;
            if (p.Length < 126) f.Add((byte)(mb | p.Length));
            else if (p.Length <= 65535) { f.Add((byte)(mb | 126)); f.Add((byte)(p.Length >> 8)); f.Add((byte)(p.Length & 0xFF)); }
            else { f.Add((byte)(mb | 127)); for (int i = 7; i >= 0; i--) f.Add((byte)((long)p.Length >> (8 * i))); }
            byte[] g = Guid.NewGuid().ToByteArray();
            byte[] mask = new byte[] { g[0], g[1], g[2], g[3] };   // mask 必须恰好 4 字节
            f.AddRange(mask);
            for (int i = 0; i < p.Length; i++) f.Add((byte)(p[i] ^ mask[i % 4]));
            byte[] fb = f.ToArray();
            ns.Write(fb, 0, fb.Length);
        }

        private static byte[] ReadExact(NetworkStream ns, int count)
        {
            byte[] b = new byte[count];
            int off = 0;
            while (off < count)
            {
                int n = ns.Read(b, off, count - off);
                if (n <= 0) throw new IOException("eof");
                off += n;
            }
            return b;
        }

        private static string JsonEscape(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
