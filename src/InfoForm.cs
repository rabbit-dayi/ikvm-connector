using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace iKVMConnector
{
    // 只读的服务器概览：BMC 信息、电源、传感器、SEL 事件条数。
    class InfoForm : Form
    {
        TextBox txt;
        Button btnRefresh, btnCopy, btnClose;
        Label lblStatus;
        Bmc bmc;

        public InfoForm(Bmc b)
        {
            bmc = b;
            Text = "服务器信息 - " + b.Host;
            ClientSize = new Size(720, 560);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(520, 380);

            btnRefresh = new Button(); btnRefresh.Text = "刷新"; btnRefresh.SetBounds(12, 10, 80, 26);
            btnRefresh.Click += delegate { Load_(); };
            Controls.Add(btnRefresh);

            btnCopy = new Button(); btnCopy.Text = "复制"; btnCopy.SetBounds(100, 10, 80, 26);
            btnCopy.Click += delegate { try { Clipboard.SetText(txt.Text); } catch { } };
            Controls.Add(btnCopy);

            lblStatus = new Label(); lblStatus.SetBounds(190, 14, 420, 20); lblStatus.ForeColor = Color.DimGray;
            Controls.Add(lblStatus);

            btnClose = new Button(); btnClose.Text = "关闭"; btnClose.SetBounds(628, 10, 80, 26);
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);

            txt = new TextBox();
            txt.Multiline = true; txt.ReadOnly = true; txt.ScrollBars = ScrollBars.Both; txt.WordWrap = false;
            txt.Font = new Font("Consolas", 9F);
            txt.SetBounds(12, 44, 696, 504);
            txt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(txt);

            Shown += delegate { Load_(); };
        }

        void Load_()
        {
            btnRefresh.Enabled = false; lblStatus.Text = "正在读取…";
            var th = new Thread(delegate ()
            {
                string report;
                try { report = Build(); }
                catch (Exception ex) { report = "读取失败：" + ex.Message; }
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "服务器信息.txt"), report.Replace("\n", "\r\n")); } catch { }
                BeginInvoke((MethodInvoker)delegate
                {
                    txt.Text = report.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    lblStatus.Text = "更新于 " + DateTime.Now.ToString("HH:mm:ss");
                    btnRefresh.Enabled = true;
                });
            });
            th.IsBackground = true; th.Start();
        }

        string Build()
        {
            var sb = new StringBuilder();
            string gen = bmc.Ipmi("GENERIC_INFO.XML", "(0,0)&time_stamp=");
            string plat = bmc.Ipmi("Get_PlatformInfo.XML", "(0,0)&time_stamp=");
            string pwr = bmc.Ipmi("GET_POWER_INFO.XML", "(0,0)&time_stamp=");
            string sel = bmc.Ipmi("SEL_INFO.XML", "(0,0)&time_stamp=");
            string sens = bmc.Ipmi("SENSOR_INFO.XML", "(1,ff)&time_stamp=");
            string fru = Try("FRU_INFO.XML", "(0,0)&time_stamp=");
            string pw = Try("POWER_CONSUMPTION.XML", "(0,0)&time_stamp=");
            string cfg = Try("CONFIG_INFO.XML", "(0,0)&time_stamp=");
            string elog = Try("GET_SYS_ELOG.XML", "(0,0)&time_stamp=");
            string fan = Try("GET_FAN_MODE.XML", "(0,0)&time_stamp=");
            string clk = "";
            try { clk = bmc.GetClock(); } catch { }

            sb.AppendLine("== 基本信息 ==");
            sb.AppendLine("  地址        " + bmc.Host);
            sb.AppendLine("  BMC MAC     " + A(gen, "BMC_MAC"));
            sb.AppendLine("  固件        " + A(gen, "IPMIFW_TAG"));
            sb.AppendLine("  固件版本    " + A(gen, "IPMIFW_VERSION") + "   构建 " + A(gen, "IPMIFW_BLDTIME"));
            sb.AppendLine("  Web 版本    " + A(gen, "WEB_VERSION") + "    内核 " + A(gen, "VERSION").Replace("\n", " ").Trim());
            sb.AppendLine("  主板 MAC    " + A(plat, "MB_MAC_ADDR1") + " / " + A(plat, "MB_MAC_ADDR2"));
            sb.AppendLine("  BIOS        " + A(plat, "BIOS_VERSION") + "   (" + A(plat, "BIOS_BUILD_DATE") + ")");
            sb.AppendLine("  CPLD        " + A(plat, "CPLD_REV") + "     Redfish " + A(plat, "REDFISH_REV"));
            sb.AppendLine("  主机名      " + A(plat, "HOSTNAME") + "    当前用户 " + A(plat, "SESS_USER_NAME"));
            if (!string.IsNullOrEmpty(clk)) sb.AppendLine("  BMC 时间    " + clk);
            string p = A(pwr, "STATUS");
            sb.AppendLine("  电源        " + (p == "ON" ? "开机 (ON)" : p == "OFF" ? "关机 (OFF)" : (p ?? "未知")));
            string total = Attr(sel, "TOTAL_NUMBER");
            if (total != null) sb.AppendLine("  SEL 事件    " + Convert.ToInt32(total, 16) + " 条");
            sb.AppendLine();

            sb.AppendLine("== 硬件信息 ==");
            string chas = Elem(fru, "CHASSIS"), board = Elem(fru, "BOARD"), prod = Elem(fru, "PRODUCT");
            if (fru.Length == 0) sb.AppendLine("  （BMC 未返回 FRU 信息）");
            else
            {
                sb.AppendLine("  主板型号    " + A(board, "PROD_NAME") + "   " + A(board, "MFC_NAME"));
                sb.AppendLine("  主板序列    " + A(board, "SERIAL_NUM") + "   生产日期 " + A(board, "MFG_DATE"));
                sb.AppendLine("  机箱型号    " + A(chas, "PART_NUM") + "   机箱序列 " + A(chas, "SERIAL_NUM"));
                sb.AppendLine("  整机型号    " + A(prod, "PART_NUM") + "   整机序列 " + A(prod, "SERIAL_NUM"));
            }
            sb.AppendLine();

            sb.AppendLine("== 网络 ==");
            string lan = Elem(cfg, "LAN");
            if (lan.Length == 0) sb.AppendLine("  （BMC 未返回网络配置）");
            else
            {
                sb.AppendLine("  BMC 地址    " + A(lan, "BMC_IP") + " / " + A(lan, "BMC_NETMASK") + "   网关 " + A(lan, "GATEWAY_IP"));
                sb.AppendLine("  BMC MAC     " + A(lan, "BMC_MAC") + "   网关 MAC " + A(lan, "GATEWAY_MAC"));
                sb.AppendLine("  DHCP        " + (A(lan, "DHCP_EN") == "1" ? "已开启" : "已关闭") + "    VLAN " + A(lan, "VLAN_ID") + "    RMCP 端口 " + Hex2Port(A(lan, "RMCP_PORT")));
            }
            var users = new List<string>();
            foreach (Match mu in Regex.Matches(cfg ?? "", "<USER\\s([^>]*)/?>"))
            {
                string nm = Attr(mu.Groups[1].Value, "NAME") ?? "";
                nm = nm.Trim();
                if (nm.Length == 0) continue;
                users.Add(nm + "（" + AccessLevel(Attr(mu.Groups[1].Value, "USER_ACCESS")) + "）");
            }
            if (users.Count > 0) sb.AppendLine("  BMC 用户    " + string.Join("、", users.ToArray()));
            sb.AppendLine();

            sb.AppendLine("== 最近操作（BMC 审计日志）==");
            var ti = ByIdx(elog, "LOGS"); var us = ByIdx(elog, "LOGS2"); var ips = ByIdx(elog, "LOGS3");
            var keys = new List<string>(ti.Keys);
            keys.Sort(delegate (string x, string y) { return HexNum(x).CompareTo(HexNum(y)); });
            int shown = 0;
            for (int i = keys.Count - 1; i >= 0 && shown < 10; i--, shown++)
            {
                string k = keys[i];
                string uu = us.ContainsKey(k) ? us[k] : "";
                string ii = ips.ContainsKey(k) ? ips[k] : "";
                uu = uu.Replace("\n", "").Trim();
                if (uu.Length > 16) uu = uu.Substring(0, 16);
                sb.AppendLine("  " + Pad(ti[k], 20) + "   " + Pad(uu, 18) + "  " + ii);
            }
            if (shown == 0) sb.AppendLine("  （无记录）");
            else sb.AppendLine("  （时间列可能因之前拨过 BMC 时钟而不连续，属正常）");
            sb.AppendLine();

            sb.AppendLine("== 功耗 ==");
            if (pw.Length == 0 || Elem(pw, "NOW").Length == 0) sb.AppendLine("  （BMC 未返回功耗数据）");
            else
            {
                string now = Elem(pw, "NOW"), peak = Elem(pw, "PEAK");
                sb.AppendLine("  当前        " + A(now, "AVR") + " W    峰值 " + A(peak, "MAX") + " W   最低 " + A(peak, "MIN") + " W");
                string pt = A(peak, "PMAXTIME");
                if (pt.Length > 0) sb.AppendLine("  峰值时间    " + pt);
                var avr = new List<int>(); var hi = new List<int>(); var lo = new List<int>();
                for (int i = 0; ; i++)
                {
                    string h = Elem(pw, "FMINS" + i);
                    if (h.Length == 0) break;
                    avr.Add(Num(A(h, "AVR"))); hi.Add(Num(A(h, "MAX"))); lo.Add(Num(A(h, "MIN")));
                }
                if (avr.Count > 0)
                {
                    int mn = int.MaxValue, mx = 0, sum = 0;
                    foreach (int v in avr) { if (v < mn) mn = v; if (v > mx) mx = v; sum += v; }
                    sb.AppendLine("  近 " + avr.Count + " 小时  " + Spark(avr) + "   平均 " + (sum / avr.Count) + " W，最高 " + MaxOf(hi) + " W，最低 " + MinOf(lo) + " W");
                }
                string day = Elem(pw, "HOUR0");
                if (day.Length > 0) sb.AppendLine("  今日       平均 " + A(day, "AVR") + " W，最高 " + A(day, "MAX") + " W，最低 " + A(day, "MIN") + " W");
            }
            sb.AppendLine();

            sb.AppendLine("== 传感器 ==");
            sb.AppendLine("  " + Pad("名称", 20) + Pad("读数", 12) + "状态");
            int n = 0, warn = 0;
            foreach (Match m in Regex.Matches(sens, "<SENSOR\\s([^>]*)/?>"))
            {
                string a = m.Groups[1].Value;
                string name = Attr(a, "NAME") ?? "?";
                string rd = Attr(a, "READING") ?? "0";
                string st = Attr(a, "STYPE") ?? "00";
                double x;
                try { x = Convert.ToInt64(rd, 16) / 65536.0; } catch { continue; }
                int m8 = HiByte(Attr(a, "M")), b8 = SignedByte(Attr(a, "B"));
                double v; string unit, status = "";
                if (st == "01") { v = m8 * x; unit = "°C"; }
                else if (st == "02") { v = (m8 * x + b8) / 1000.0; unit = "V"; }
                else if (st == "04") { v = m8 * x; unit = "RPM"; }
                else { v = m8 * x; unit = ""; }

                // 阈值是 8 位原始读数（不是工程单位）：温度按有符号字节，电压/风扇按无符号
                int raw8 = (int)((Convert.ToInt64(rd, 16) >> 16) & 0xff);
                bool signed = st == "01";
                int cmp = signed ? (int)(sbyte)raw8 : raw8;
                if (rd.EndsWith("0000") && v == 0) status = "无读数";   // 没有插设备/没取到值，先判掉
                else if (st == "01" || st == "02" || st == "04")
                {
                    int unr = Th(a, "UNR", signed), uc = Th(a, "UC", signed), unc = Th(a, "UNC", signed);
                    int lnr = Th(a, "LNR", signed), lc = Th(a, "LC", signed), lnc = Th(a, "LNC", signed);
                    bool hasUnr = Has(a, "UNR"), hasUc = Has(a, "UC"), hasUnc = Has(a, "UNC");
                    bool hasLnr = Has(a, "LNR"), hasLc = Has(a, "LC"), hasLnc = Has(a, "LNC");
                    // 风扇的 0xf6~0xff 阈值实际上等于“没有上限”，按无符号比较自然成立
                    if ((hasUnr && cmp >= unr) || (hasUc && cmp >= uc)) { status = "过高!"; warn++; }
                    else if (hasUnc && cmp >= unc) { status = "偏高"; warn++; }
                    else if ((hasLnr && cmp <= lnr) || (hasLc && cmp <= lc)) { status = "过低!"; warn++; }
                    else if (hasLnc && cmp <= lnc) { status = "偏低"; warn++; }
                    else status = "正常";
                }

                string val = unit == "RPM" ? ((int)Math.Round(v)).ToString() : v.ToString("0.00", CultureInfo.InvariantCulture);
                if (unit == "V") val = v.ToString("0.000", CultureInfo.InvariantCulture);
                sb.AppendLine("  " + Pad(name, 20) + Pad(val + " " + unit, 12) + status);
                n++;
            }
            sb.AppendLine();
            string fm = A(Elem(fan, "FAN_INFO"), "Mode");
            sb.AppendLine("共 " + n + " 个传感器" + (warn > 0 ? "，其中 " + warn + " 个超出阈值" : "，全部在阈值内") + "。");
            if (fm.Length > 0) sb.AppendLine("风扇模式：" + FanMode(fm));
            return sb.ToString();
        }

        static string Pad(string s, int w)
        {
            int len = 0;
            foreach (char c in s) len += (c > 127 ? 2 : 1);
            var sb = new StringBuilder(s);
            while (len < w) { sb.Append(' '); len++; }
            return sb.ToString();
        }

        string Try(string name, string args)
        {
            try { return bmc.Ipmi(name, args); } catch { return ""; }
        }

        // 取某个元素标签开头的属性串，例如 <BOARD LAN="0" ... /> 里的 BOARD 那段
        static string Elem(string xml, string tag)
        {
            if (xml == null) return "";
            var m = Regex.Match(xml, "<" + tag + "\\s([^>]*)>");
            return m.Success ? m.Groups[1].Value : "";
        }

        static int Num(string s) { int v; return int.TryParse(s, out v) ? v : 0; }

        static bool Has(string a, string name)
        {
            string v = Attr(a, name);
            return v != null && v.Length > 0 && v.Trim() != "00";
        }

        static int Th(string a, string name, bool signed)
        {
            int v = Hex(Attr(a, name));
            return signed ? (int)(sbyte)v : v;
        }

        static string Hex2Port(string s) { try { return Convert.ToInt32(s, 16).ToString(); } catch { return s ?? ""; } }

        static string AccessLevel(string v)
        {
            switch (v)
            {
                case "04": return "管理员";
                case "03": return "操作员";
                case "02": return "普通用户";
                case "01": return "回调";
                default: return "无权限";
            }
        }

        static string FanMode(string v)
        {
            switch (v)
            {
                case "0": return "标准 (Standard)";
                case "1": return "全速 (Full Speed)";
                case "2": return "最优 (Optimal)";
                case "3": return "重 IO (Heavy IO)";
                default: return "模式 " + v;
            }
        }

        static int HexNum(string s) { try { return Convert.ToInt32(s, 16); } catch { return 0; } }

        // "Log1a" / "User1a" / "IP1a" 的序号都在末尾，取尾部十六进制串
        static string KeySuffix(string k)
        {
            int i = k.Length;
            while (i > 0 && Uri.IsHexDigit(k[i - 1])) i--;
            return k.Substring(i);
        }

        static Dictionary<string, string> ByIdx(string xml, string tag)
        {
            var raw = AttrMap(xml, tag);
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in raw)
            {
                string s = KeySuffix(kv.Key);
                if (s.Length > 0) d[s] = kv.Value;
            }
            return d;
        }

        // 把 <TAG attr="..."> 里的属性解成字典（值里可能带 &#10; 换行）
        static Dictionary<string, string> AttrMap(string xml, string tag)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (xml == null) return d;
            var m = Regex.Match(xml, "<" + tag + "\\s([^>]*)>", RegexOptions.Singleline);
            if (!m.Success) return d;
            foreach (Match a in Regex.Matches(m.Groups[1].Value, "(\\w+)=\"([^\"]*)\"", RegexOptions.Singleline))
                d[a.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(a.Groups[2].Value).Trim();
            return d;
        }
        static int MaxOf(List<int> a) { int v = 0; foreach (int x in a) if (x > v) v = x; return v; }
        static int MinOf(List<int> a) { int v = int.MaxValue; foreach (int x in a) if (x < v) v = x; return v == int.MaxValue ? 0 : v; }

        // 用方块字符画一条迷你走势线（只在等宽字体下有意义）
        static string Spark(List<int> vals)
        {
            const string Lv = "▁▂▃▄▅▆▇█";
            int mn = int.MaxValue, mx = int.MinValue;
            foreach (int v in vals) { if (v < mn) mn = v; if (v > mx) mx = v; }
            var sb = new StringBuilder();
            foreach (int v in vals)
            {
                int idx = (mx == mn) ? 3 : (int)Math.Round((v - mn) * 7.0 / (mx - mn));
                sb.Append(Lv[idx]);
            }
            return sb.ToString();
        }

        static string A(string xml, string name) { return Attr(xml, name) ?? ""; }

        static string Attr(string xml, string name)
        {
            if (xml == null) return null;
            var m = Regex.Match(xml, "\\b" + name + "=\"([^\"]*)\"");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        static int Hex(string s) { try { return Convert.ToInt32(s, 16); } catch { return 0; } }
        static int HiByte(string s) { int v = Hex(s); return (v >> 8) & 0xff; }
        static int SignedByte(string s) { int v = HiByte(s); return v >= 128 ? v - 256 : v; }
    }
}
