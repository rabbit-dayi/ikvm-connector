using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

namespace iKVMConnector
{
    class JnlpArgs
    {
        public string Url; public List<string> Args = new List<string>();
        public string Host { get { return Args.Count > 0 ? Args[0] : ""; } }
        public int ConsolePort { get { return GetInt(4, 63630); } }
        public int MediaPort { get { return GetInt(5, 63631); } }
        public int RemoteConsole { get { return GetInt(9, 5900); } }
        public int RemoteMedia { get { return GetInt(10, 623); } }
        int GetInt(int i, int d) { int v; if (Args.Count > i && int.TryParse(Args[i], out v)) return v; return d; }

        public static JnlpArgs Load(string path)
        {
            string xml = File.ReadAllText(path);
            var j = new JnlpArgs();
            var m = Regex.Match(xml, @"codebase\s*=\s*""([^""]+)""");
            if (m.Success) j.Url = m.Groups[1].Value;
            foreach (Match a in Regex.Matches(xml, @"<argument>(.*?)</argument>", RegexOptions.Singleline))
                j.Args.Add(System.Net.WebUtility.HtmlDecode(a.Groups[1].Value.Trim()));
            if (j.Args.Count < 11) throw new Exception("JNLP 参数不足（" + j.Args.Count + " 个），可能不是 ATEN iKVM 的启动文件。");
            return j;
        }
    }

    class Tunnel
    {
        public int ListenPort; public string Host; public int RemotePort;
        public X509Certificate2 Cert; public Action<string> Log;
        TcpListener listener; volatile bool running;

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, ListenPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            running = true;
            var t = new Thread(AcceptLoop); t.IsBackground = true; t.Start();
        }
        public void Stop() { running = false; try { listener.Stop(); } catch { } }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                var t = new Thread(delegate () { Handle(c); }); t.IsBackground = true; t.Start();
            }
        }

        void Handle(TcpClient client)
        {
            TcpClient server = null; SslStream ssl = null;
            try
            {
                client.NoDelay = true;
                server = new TcpClient();
                server.NoDelay = true;
                server.Connect(Host, RemotePort);
                ssl = new SslStream(server.GetStream(), false, delegate { return true; });
                var coll = new X509CertificateCollection();
                if (Cert != null) coll.Add(Cert);
                ssl.AuthenticateAsClient(Host, coll, (SslProtocols)3072, false);
                Log(string.Format("[{0}] TLS 已建立 → {1}:{2} ({3})", ListenPort, Host, RemotePort, ssl.SslProtocol));

                NetworkStream a = client.GetStream(), b = server.GetStream();
                var t1 = new Thread(delegate () { Pump(a, ssl, "C>S"); }); t1.IsBackground = true; t1.Start();
                var t2 = new Thread(delegate () { Pump(ssl, a, "S>C"); }); t2.IsBackground = true; t2.Start();
                t1.Join(); t2.Join();
            }
            catch (AuthenticationException ex)
            {
                Log(string.Format("[{0}] ✗ TLS 握手失败：{1}", ListenPort, DescribeAlert(ex, Host)));
            }
            catch (Exception ex)
            {
                Log(string.Format("[{0}] ✗ 连接失败：{1}", ListenPort, ex.Message));
            }
            finally
            {
                try { if (ssl != null) ssl.Dispose(); } catch { }
                try { if (server != null) server.Close(); } catch { }
                try { client.Close(); } catch { }
            }
        }

        static void Pump(Stream from, Stream to, string tag)
        {
            var buf = new byte[16384];
            int total = 0;
            try
            {
                int n;
                while ((n = from.Read(buf, 0, buf.Length)) > 0) { total += n; to.Write(buf, 0, n); to.Flush(); }
            }
            catch { }
            Unused(total);
            try { to.Dispose(); } catch { }
            try { from.Close(); } catch { }
        }

        static void Unused(int n) { }

        public static string DescribeAlert(Exception ex, string host)
        {
            string all = ""; int hr = 0;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (!string.IsNullOrEmpty(e.Message)) all += e.Message + " | ";
                if (hr == 0 && e.HResult != 0) hr = e.HResult;
            }
            string low = all.ToLowerInvariant();
            if (all.Contains("证书已过期") || low.Contains("certificate has expired") || hr == unchecked((int)0x80090328))
                return "服务端回复“证书已过期”(alert 45)。原因：BMC 的时钟已越过 2026-05-17，它用这个时间去校验内置的客户端证书链，而那条链的根证书正好在这一天到期。\r\n" +
                       "    解决办法（只需做一次）：用浏览器打开 https://" + host + "/ 登录 IPMI → Configuration → Date and Time，把日期改到 2026 年 5 月 17 日之前（例如 2026-05-01），关闭 NTP/时间同步，保存后重试。";
            if (all.Contains("没有识别提供给安全包的凭证") || low.Contains("credentials supplied to the package were not recognized"))
                return "本机的客户端证书私钥无法使用（系统密钥容器创建失败）。请把整个程序文件夹复制到普通目录（例如 C:\\Users\\你的用户名\\AppData\\Local\\iKVMConnector）后再运行。";
            if (all.Contains("安全包中没有可用的凭证") || low.Contains("no credentials are available"))
                return "握手时没有带上客户端证书。请确认程序目录里的 viewer\\client.pfx 存在且未损坏。";
            switch (hr)
            {
                case unchecked((int)0x80090326):
                    return "服务端拒绝握手 (alert 40 / illegal message)，通常表示没有提供客户端证书或协议不被接受。";
                case unchecked((int)0x80090325):
                    return "服务端不信任该客户端证书 (alert 48 unknown_ca)。";
                case unchecked((int)0x80090331):
                    return "TLS 版本/加密套件不匹配（需要 TLS 1.2）。";
                default:
                    return all.Trim() + " (0x" + hr.ToString("X8") + ")";
            }
        }
    }
}
