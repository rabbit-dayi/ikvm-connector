using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace iKVMConnector
{
    // 直接和 BMC 的 Web 接口对话：登录、取新 JNLP 令牌、读写硬件时钟。
    class Bmc
    {
        public string Host, User, Password;
        public CookieContainer Cookies = new CookieContainer();
        string csrf;

        public Bmc(string host, string user, string password) { Host = host; User = user; Password = password; }

        static Bmc()
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
        }

        string Url(string path) { return "https://" + Host + path; }

        string Send(string method, string path, string body, bool withCsrf, int timeout)
        {
            var req = (HttpWebRequest)WebRequest.Create(Url(path));
            req.Method = method; req.CookieContainer = Cookies; req.Timeout = timeout; req.ReadWriteTimeout = timeout;
            req.AllowAutoRedirect = true; req.KeepAlive = false;
            req.ServicePoint.Expect100Continue = false;
            req.Expect = "";
            req.UserAgent = "Mozilla/5.0 iKVMConnector";
            if (withCsrf) req.Headers["CSRF_TOKEN"] = Csrf();
            if (body != null)
            {
                var b = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/x-www-form-urlencoded"; req.ContentLength = b.Length;
                using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new System.IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException we)
            {
                var r = we.Response as HttpWebResponse;
                string m = we.Message;
                if (r != null) m += "（HTTP " + (int)r.StatusCode + "）";
                throw new Exception(m);
            }
        }

        string Csrf()
        {
            if (!string.IsNullOrEmpty(csrf)) return csrf;
            string html = Send("GET", "/cgi/url_redirect.cgi?url_name=config_datetime", null, false, 20000);
            var m = Regex.Match(html, "SmcCsrfInsert\\s*\\(\\s*\"CSRF_TOKEN\"\\s*,\\s*\"([^\"]+)\"");
            if (!m.Success) throw new Exception("页面中找不到 CSRF 令牌，可能登录状态已失效。");
            csrf = m.Groups[1].Value;
            return csrf;
        }

        public void Login()
        {
            csrf = null; Cookies = new CookieContainer();
            string body = "name=" + Uri.EscapeDataString(User) + "&pwd=" + Uri.EscapeDataString(Password);
            Send("POST", "/cgi/login.cgi", body, false, 20000);
            bool ok = false;
            foreach (Cookie c in Cookies.GetCookies(new Uri(Url("/")))) if (c.Name == "SID" && !string.IsNullOrEmpty(c.Value)) ok = true;
            if (!ok) throw new Exception("登录失败：没有拿到会话 Cookie，请检查用户名/密码。");
        }

        public string GetClock()
        {
            string xml = Send("POST", "/cgi/ipmi.cgi", "GET_DATE_TIME.XML=(0,0)", true, 20000);
            string y = Field(xml, "YEAR"), mo = Field(xml, "MONTH"), d = Field(xml, "DAY");
            string h = Field(xml, "HOUR"), mi = Field(xml, "MIN"), s = Field(xml, "SEC");
            if (y == null) return null;
            return string.Format("{0}-{1}-{2} {3}:{4}:{5}", y, mo, d, h, mi, s);
        }

        static string Field(string xml, string name)
        {
            var m = Regex.Match(xml, name + "\\s*=\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // 把 BMC 时钟设到 2026-05-17 之前（内置客户端证书的根证书在这天到期）。
        public string SetClock(int year, int month, int day, int hour, int min, int sec)
        {
            csrf = null;
            string body = "op=config_date_time&timezone=0&dst_en=0&ntp=0&ntp_server_pri=localhost&ntp_server_2nd=127.0.0.1"
                + "&year=" + year + "&month=" + month + "&day=" + day + "&hour=" + hour + "&min=" + min + "&sec=" + sec;
            return Send("POST", "/cgi/op.cgi", body, true, 25000);
        }

        // 通用 IPMI 查询，例如 Ipmi("SENSOR_INFO.XML", "(1,ff)&time_stamp=")
        public string Ipmi(string name, string args)
        {
            return Send("POST", "/cgi/ipmi.cgi", name + "=" + args + DateTime.Now.Ticks, true, 30000);
        }

        public string FetchJnlp()
        {
            string xml = Send("GET", "/cgi/url_redirect.cgi?url_name=ikvm&url_type=jwsk", null, false, 25000);
            if (xml == null || xml.IndexOf("<jnlp", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("从 BMC 取回的内容不是 JNLP（可能是被重定向到登录页）。");
            return xml;
        }
    }
}
