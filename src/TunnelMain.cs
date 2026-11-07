using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace iKVMConnector
{
    static class TunnelMain
    {
        static void Main(string[] args)
        {
            JnlpArgs j = JnlpArgs.Load(args[0]);
            string pfx = args.Length > 1 ? args[1] : "client.pfx";
            X509Certificate2 cert = new X509Certificate2(pfx, "ikvm", X509KeyStorageFlags.Exportable);
            Tunnel t1 = new Tunnel();
            t1.ListenPort = j.ConsolePort; t1.Host = j.Host; t1.RemotePort = j.RemoteConsole; t1.Cert = cert;
            t1.Log = delegate (string s) { Console.WriteLine("[c] " + s); };
            Tunnel t2 = new Tunnel();
            t2.ListenPort = j.MediaPort; t2.Host = j.Host; t2.RemotePort = j.RemoteMedia; t2.Cert = cert;
            t2.Log = delegate (string s) { Console.WriteLine("[m] " + s); };
            t1.Start(); t2.Start();
            Console.WriteLine("listening " + j.ConsolePort + " / " + j.MediaPort + " -> " + j.Host);
            Thread.Sleep(Timeout.Infinite);
        }
    }
}