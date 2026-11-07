using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace iKVMConnector
{
    class StunService { public int LocalPort; public string Host; public int RemotePort; }

    static class StunnelMain
    {
        static string logPath;

        static void Log(string s)
        {
            try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss ") + s + "\r\n"); } catch { }
        }

        static void Main(string[] args)
        {
            logPath = Path.Combine(Path.GetTempPath(), "iKVM-stunnel.log");
            try
            {
                Log("=== start ===");
                string conf = args.Length > 0 ? args[0] : "";
                if (!Path.IsPathRooted(conf))
                    conf = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), conf);
                Log("conf=" + conf);
                List<StunService> svcs = Parse(conf);
                X509Certificate2 cert = LoadCert();
                Log("cert=" + (cert == null ? "none" : cert.Subject));
                foreach (StunService s in svcs)
                {
                    Tunnel t = new Tunnel();
                    t.ListenPort = s.LocalPort; t.Host = s.Host; t.RemotePort = s.RemotePort; t.Cert = cert;
                    t.Log = Log;
                    t.Start();
                    Log("listen 127.0.0.1:" + s.LocalPort + " -> " + s.Host + ":" + s.RemotePort);
                }
                Log("ready");
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex) { Log("FATAL " + ex); }
        }

        static X509Certificate2 LoadCert()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string n in asm.GetManifestResourceNames())
            {
                if (n.EndsWith("client.pfx", StringComparison.OrdinalIgnoreCase))
                {
                    using (Stream st = asm.GetManifestResourceStream(n))
                    {
                        byte[] b = new byte[st.Length];
                        st.Read(b, 0, b.Length);
                        return new X509Certificate2(b, "ikvm", X509KeyStorageFlags.Exportable);
                    }
                }
            }
            return null;
        }

        static List<StunService> Parse(string path)
        {
            List<StunService> list = new List<StunService>();
            StunService cur = null;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[")) { cur = new StunService(); list.Add(cur); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0 || cur == null) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                if (k == "accept") { string[] p = v.Split(':'); cur.LocalPort = int.Parse(p[p.Length - 1]); }
                else if (k == "connect") { int i = v.LastIndexOf(':'); cur.Host = v.Substring(0, i); cur.RemotePort = int.Parse(v.Substring(i + 1)); }
            }
            return list;
        }
    }
}
