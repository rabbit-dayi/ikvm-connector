using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace iKVMConnector
{
    static class ConsoleDiag
    {
        public static void Run(string jnlpPath)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                if (jnlpPath == null || !File.Exists(jnlpPath)) { sb.AppendLine("用法: iKVM-diag.exe <文件.jnlp>"); }
                else
                {
                    var j = JnlpArgs.Load(jnlpPath);
                    sb.AppendLine("目标 " + j.Host + " 控制台 " + j.RemoteConsole + " 介质 " + j.RemoteMedia);
                    string pfx = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "viewer", "client.pfx");
                    var cert = new X509Certificate2(pfx, "ikvm");
                    sb.AppendLine("客户端证书: " + cert.Subject + " 到期 " + cert.NotAfter.ToString("yyyy-MM-dd") + " hasKey=" + cert.HasPrivateKey);
                    sb.AppendLine("pfx: " + pfx);
                    try { var rsa = cert.PrivateKey as System.Security.Cryptography.RSACryptoServiceProvider; sb.AppendLine("私钥可用: " + (rsa != null ? rsa.SignData(new byte[] { 1, 2, 3 }, "SHA1").Length.ToString() + " bytes (SHA1)" : "无 RSA 私钥")); }
                    catch (Exception se) { sb.AppendLine("私钥不可用: " + se.Message); }
                    sb.AppendLine("控制台: " + Probe(cert, j.Host, j.RemoteConsole));
                    sb.AppendLine("介质  : " + Probe(cert, j.Host, j.RemoteMedia));
                }
            }
            catch (Exception ex) { sb.AppendLine("错误: " + ex.Message); }
            string text = sb.ToString();
            Console.WriteLine(text);
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "diag.log"), text); } catch { }
        }

        static string Probe(X509Certificate2 cert, string host, int port)
        {
            using (var tcp = new TcpClient())
            {
                try { tcp.Connect(host, port); } catch (Exception ex) { return "TCP 连不上: " + ex.Message; }
                using (var ssl = new SslStream(tcp.GetStream(), false, delegate { return true; }))
                {
                    var coll = new X509CertificateCollection(); coll.Add(cert);
                    try { ssl.AuthenticateAsClient(host, coll, (SslProtocols)3072, false); return "OK " + ssl.SslProtocol; }
                    catch (AuthenticationException ex) { return Tunnel.DescribeAlert(ex, host); }
                    catch (Exception ex) { return ex.Message; }
                }
            }
        }
    }
}
