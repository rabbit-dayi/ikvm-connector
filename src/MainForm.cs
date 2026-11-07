using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace iKVMConnector
{
    class MainForm : Form
    {
        TextBox txtJnlp, txtHost, txtUser, txtPwd, txtLog;
        Button btnBrowse, btnFetch, btnConnect, btnStop, btnDiag, btnWeb, btnInfo;
        CheckBox chkClock;
        NumericUpDown numYear;
        Process viewer;
        string appDir;
        bool busy;
        static readonly string workDir = ResolveWorkDir();
        static readonly string logPath = Path.Combine(workDir, "iKVMConnector.log");
        static readonly string cfgPath = ResolveCfgPath();

        // 优先 %APPDATA%\iKVMConnector\settings.ini；那里写不进去（受限环境/绿色运行）时退回程序目录。
        static string ResolveCfgPath()
        {
            string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iKVMConnector", "settings.ini");
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
            foreach (string p in new string[] { roaming, local })
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    using (var fs = new FileStream(p, FileMode.Append, FileAccess.Write)) { }
                    return p;
                }
                catch { }
            }
            return local;
        }

        // 环境变量 TEMP 有时不可用（从某些终端启动时），这里找一个一定可写的工作目录。
        static string ResolveWorkDir()
        {
            var cands = new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iKVMConnector"),
                Path.GetTempPath(),
                Path.GetDirectoryName(Application.ExecutablePath) };
            foreach (var c in cands)
            {
                try
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    Directory.CreateDirectory(c);
                    string probe = Path.Combine(c, ".probe");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    return c;
                }
                catch { }
            }
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        public MainForm(string jnlpPath, bool auto = false, string host = null, string user = null, string pwd = null, bool info = false)
        {
            appDir = Path.GetDirectoryName(Application.ExecutablePath);
            Text = "iKVM 连接器  —  ATEN Java iKVM（绕过过期证书）";
            ClientSize = new Size(800, 560);
            MinimumSize = new Size(660, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            AllowDrop = true;

            Controls.Add(MkLabel("JNLP:", 12, 15, 44));
            txtJnlp = MkText(58, 11, 452);
            txtJnlp.AllowDrop = true;
            txtJnlp.DragEnter += DragEnterH;
            txtJnlp.DragDrop += DragDropH;
            txtJnlp.TextChanged += delegate { ParseLocal(false); };
            Controls.Add(txtJnlp);

            btnBrowse = MkBtn("浏览…", 516, 10, 70, delegate { Browse(); });
            btnFetch = MkBtn("取新令牌", 592, 10, 90, delegate { FetchOnly(); });
            btnConnect = MkBtn("连接", 688, 10, 100, delegate { Connect(); });
            btnConnect.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

            Controls.Add(MkLabel("BMC:", 12, 49, 40));
            txtHost = MkText(52, 45, 130);
            Controls.Add(MkLabel("用户", 190, 49, 34));
            txtUser = MkText(226, 45, 86);
            Controls.Add(MkLabel("密码", 320, 49, 34));
            txtPwd = MkText(356, 45, 96);
            txtPwd.UseSystemPasswordChar = true;
            chkClock = new CheckBox();
            chkClock.Text = "连接前把时钟拨回";
            chkClock.SetBounds(456, 47, 150, 22);
            chkClock.Checked = true;
            Controls.Add(chkClock);
            Controls.Add(MkLabel("年", 610, 49, 18));
            numYear = new NumericUpDown();
            numYear.SetBounds(630, 45, 58, 24);
            numYear.Minimum = 2010; numYear.Maximum = 2030;
            numYear.Value = DefaultYear();
            Controls.Add(numYear);
            btnStop = MkBtn("停止", 694, 44, 56, delegate { Stop(true); });
            btnStop.Enabled = false;
            btnDiag = MkBtn("诊断", 754, 44, 46, delegate { Diagnose(); });

            btnWeb = MkBtn("打开 BMC 网页", 12, 76, 130, delegate { OpenWeb(); });
            btnWeb.Height = 24;
            btnInfo = MkBtn("服务器信息", 150, 76, 110, delegate { ShowInfo(); });
            btnInfo.Height = 24;

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.ReadOnly = true;
            txtLog.SetBounds(12, 106, 776, 442);
            txtLog.BackColor = Color.FromArgb(250, 250, 250);
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            txtLog.AllowDrop = true;
            txtLog.DragEnter += DragEnterH;
            txtLog.DragDrop += DragDropH;
            Controls.Add(txtLog);

            DragEnter += DragEnterH;
            DragDrop += DragDropH;

            LoadSettings();
            if (!string.IsNullOrEmpty(host)) txtHost.Text = host;
            if (!string.IsNullOrEmpty(user)) txtUser.Text = user;
            if (!string.IsNullOrEmpty(pwd)) txtPwd.Text = pwd;
            if (!string.IsNullOrEmpty(jnlpPath)) txtJnlp.Text = jnlpPath;

            Log("iKVM 连接器已就绪。");
            Log("原理：jar 里内置的客户端证书 2026-05-17 到期，而 BMC 时钟已走过该日期，双向 TLS 被拒");
            Log("      （alert 45 证书已过期）。本程序登录 BMC、取新令牌、把 BMC 时钟拨回所选年份后再启动");
            Log("      Java Viewer，握手即可通过。BMC 会自行同步回主机时间，所以每次连接都会重拨一次。");
            if (File.Exists(cfgPath) && txtHost.Text.Trim().Length > 0)
                Log("已恢复上次的连接信息：" + txtHost.Text.Trim() + (txtUser.Text.Trim().Length > 0 ? " / " + txtUser.Text.Trim() : "") + "（改完直接关窗口也会记住）");
            Log("日志：" + logPath);
            Log("");
            ParseLocal(true);
            FormClosing += delegate { SaveSettings(txtHost.Text.Trim(), txtJnlp.Text.Trim()); };
            if (auto) Shown += delegate { Connect(); };
            if (info) Shown += delegate { ShowInfo(); };
        }

        Label MkLabel(string t, int x, int y, int w) { var l = new Label(); l.Text = t; l.SetBounds(x, y, w, 20); Controls.Add(l); return l; }
        TextBox MkText(int x, int y, int w)
        {
            var t = new TextBox(); t.SetBounds(x, y, w, 24); t.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(t); return t;
        }
        Button MkBtn(string text, int x, int y, int w, EventHandler h)
        {
            var b = new Button(); b.Text = text; b.SetBounds(x, y, w, 26); b.Click += h; Controls.Add(b); return b;
        }

        void DragEnterH(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
        void DragDropH(object s, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) { txtJnlp.Text = files[0]; ParseLocal(true); }
        }

        void Browse()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "JNLP 启动文件 (*.jnlp)|*.jnlp|所有文件 (*.*)|*.*";
                d.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (d.ShowDialog(this) == DialogResult.OK) { txtJnlp.Text = d.FileName; ParseLocal(true); }
            }
        }

        void ParseLocal(bool log)
        {
            string p = txtJnlp.Text.Trim();
            if (p.Length == 0 || !File.Exists(p)) return;
            try
            {
                var j = JnlpArgs.Load(p);
                if (string.IsNullOrEmpty(txtHost.Text)) txtHost.Text = j.Host;
                if (log) Log("已载入 JNLP：" + p + "（主机 " + j.Host + "）");
            }
            catch (Exception ex) { if (log) Log("✗ JNLP 解析失败：" + ex.Message); }
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Log(s); }); return; }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\r\n";
            txtLog.AppendText(line);
            try { File.AppendAllText(logPath, line); } catch { }
        }

        string JavaExe()
        {
            var cands = new List<string>();
            string jh = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(jh)) cands.Add(Path.Combine(jh, "bin", "java.exe"));
            cands.Add(@"C:\Program Files\Common Files\Oracle\Java\java8path\java.exe");
            cands.Add(@"C:\Program Files (x86)\Common Files\Oracle\Java\java8path\java.exe");
            cands.Add(Path.Combine(appDir, "viewer", "java.exe"));
            foreach (var c in cands) if (File.Exists(c) && IsJava8(c)) return c;
            foreach (var c in cands) if (File.Exists(c)) return c;
            return null;
        }

        bool IsJava8(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, "-version");
                psi.RedirectStandardError = true; psi.RedirectStandardOutput = true; psi.UseShellExecute = false; psi.CreateNoWindow = true;
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    return s.Contains("\"1.8.") || s.Contains("\"1.7.");
                }
            }
            catch { return false; }
        }

        // ---- 主流程 ----------------------------------------------------

        void Connect()
        {
            if (busy) return;
            string host = txtHost.Text.Trim();
            if (host.Length == 0) { string p = txtJnlp.Text.Trim(); if (File.Exists(p)) { try { host = JnlpArgs.Load(p).Host; } catch { } } }
            if (host.Length == 0) { MessageBox.Show(this, "请填写 BMC 地址，或选择一个 JNLP 文件。", "iKVM 连接器", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            txtHost.Text = host;

            string jar = Path.Combine(appDir, "viewer", "iKVM-fixed.jar");
            string lib = Path.Combine(appDir, "viewer", "nativelib.jar");
            string nat = Path.Combine(appDir, "viewer", "native");
            if (!File.Exists(jar) || !File.Exists(lib) || !Directory.Exists(nat))
            {
                MessageBox.Show(this, "缺少随附文件，请确认 viewer 文件夹完整：\r\n" + jar + "\r\n" + lib + "\r\n" + nat,
                    "文件不完整", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string java = JavaExe();
            if (java == null) { MessageBox.Show(this, "找不到 Java 8 运行环境。", "缺少 Java", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            busy = true; btnConnect.Enabled = false; btnStop.Enabled = true;
            var th = new Thread(delegate ()
            {
                try { ConnectWork(host, java, jar, lib, nat); }
                catch (Exception ex) { Log("✗ " + Describe(ex)); }
                finally
                {
                    busy = false;
                    BeginInvoke((MethodInvoker)delegate { btnConnect.Enabled = true; });
                }
            });
            th.IsBackground = true; th.Start();
        }

        // Viewer 退出后它拉起的 stunnel 可能残留，继续占着 127.0.0.1 的端口，下次就会一直 "Retry"。
        // 这里把工作目录下由本程序启动过的 *\*_stunnel.exe 全部清掉（不动别人的进程）。
        void KillStrayTunnels()
        {
            try
            {
                int n = 0;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string name = p.ProcessName;
                        if (!name.EndsWith("_stunnel")) continue;
                        string path = null; try { path = p.MainModule.FileName; } catch { }
                        if (path == null || !path.StartsWith(workDir, StringComparison.OrdinalIgnoreCase)) continue;
                        p.Kill(); n++;
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
                if (n > 0) Log("清理了 " + n + " 个残留的隧道进程。");
                // 顺带删掉上次运行留下的 <时间戳> 临时目录（Viewer 会把 stunnel 解到那里）。
                foreach (var d in Directory.GetDirectories(workDir))
                {
                    string nm = Path.GetFileName(d);
                    bool numeric = nm.Length > 5;
                    foreach (char c in nm) if (c < '0' || c > '9') { numeric = false; break; }
                    if (!numeric || !File.Exists(Path.Combine(d, nm + "_stunnel.exe"))) continue;
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
        }

        void ConnectWork(string host, string java, string jar, string lib, string nat)
        {
            KillStrayTunnels();
            string jnlpPath = txtJnlp.Text.Trim();
            var j = (jnlpPath.Length > 0 && File.Exists(jnlpPath)) ? TryLoad(jnlpPath) : null;

            string user = txtUser.Text.Trim(), pwd = txtPwd.Text;
            if (user.Length > 0)
            {
                var bmc = new Bmc(host, user, pwd);
                Log("登录 BMC " + host + " …");
                bmc.Login();
                Log("  已登录。");

                string before = null;
                try { before = bmc.GetClock(); if (before != null) Log("  BMC 当前时间：" + before); } catch { }

                Log("从 BMC 取新的 JNLP 令牌 …");
                string xml = bmc.FetchJnlp();
                jnlpPath = Path.Combine(workDir, "iKVMConnector-fresh.jnlp");
                File.WriteAllText(jnlpPath, xml, new UTF8Encoding(false));
                j = JnlpArgs.Load(jnlpPath);
                Log("  已取得（工作目录 " + workDir + "）。");

                if (chkClock.Checked)
                {
                    // 证书链的根在 2026-05-17 到期；BMC 会用当前时间去校验，所以握手前先把时钟拨回去。
                    // 只改年份、保留当前的月日时分秒，这样 BMC 的事件日志和实际情况仍对得上。
                    var t = ClockTarget();
                    Log("把 BMC 时钟拨到 " + t.ToString("yyyy-MM-dd HH:mm:ss") + "（内置客户端证书的有效期内，月日时分秒保持不变）…");
                    try
                    {
                        bmc.SetClock(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);
                        string after = null;
                        try { after = bmc.GetClock(); } catch { }
                        Log("  已设置" + (after != null ? "，现在为 " + after : "。"));
                    }
                    catch (Exception ex) { Log("  ⚠ 设置失败：" + ex.Message + "（继续尝试连接）"); }
                }
            }
            else if (j == null)
            {
                throw new Exception("没有可用的 JNLP，也没有填写 BMC 用户名/密码。");
            }

            var args = new List<string>(j.Args);
            while (args.Count < 11) args.Add("0");
            // arg[8]=stunEnable：必须为 1，Viewer 才会真的连接并显示窗口；
            // 它会自行解包 jar 里的 stunnel 建立本地隧道（客户端证书在 jar 内，stunnel 自己会加 -k 忽略过期）。
            args[8] = "1";
            Log("目标 " + args[0] + "，令牌 " + args[1]);

            string cp = jar + ";" + lib;
            var sb = new StringBuilder();
            sb.Append("-cp \"" + cp + "\" ");
            sb.Append("-Djava.library.path=\"" + nat + "\" ");
            sb.Append("-Dsun.java2d.noddraw=true ");
            sb.Append("tw.com.aten.ikvm.KVMMain");
            foreach (var a in args) sb.Append(" \"" + a.Replace("\"", "") + "\"");

            Log("启动 Java Viewer（" + java + "）…");
            var psi = new ProcessStartInfo(java, sb.ToString());
            psi.WorkingDirectory = Path.Combine(appDir, "viewer");
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true;
            // Viewer 会在临时目录里解包隧道程序，这里保证 TEMP 指向一个真实可写目录
            psi.EnvironmentVariables["TEMP"] = workDir;
            psi.EnvironmentVariables["TMP"] = workDir;
            var p = new Process(); p.StartInfo = psi;
            p.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) Log("java: " + e.Data); };
            p.OutputDataReceived += delegate (object o, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) Log("java: " + e.Data); };
            p.EnableRaisingEvents = true;
            p.Exited += delegate { int code = -1; try { code = p.ExitCode; } catch { } BeginInvoke((MethodInvoker)delegate { Log("Viewer 已退出（退出码 " + code + "）。"); btnStop.Enabled = false; }); };
            p.Start();
            p.BeginErrorReadLine(); p.BeginOutputReadLine();
            viewer = p;

            // 8 秒内退出 = 起动失败，把常见原因说清楚
            Thread.Sleep(8000);
            if (p.HasExited)
            {
                Log("✗ Viewer 启动后立即退出（退出码 " + p.ExitCode + "）。");
                Log("  常见原因：1) 令牌已过期（重新“取新令牌”）；2) BMC 时钟已越过证书有效期（勾选上面的时钟选项）；");
                Log("              3) viewer\\native 里的 DLL 缺失，或 Java 版本不是 1.8。");
            }
            else
            {
                Log("✓ Viewer 正在运行：应已出现 “Java iKVM Viewer … <BMC 地址>” 窗口。");
                Log("  （想恢复画面实时性，直接在窗口里点一下或按键即可。）");
            }
            SaveSettings(host, jnlpPath);
        }

        JnlpArgs TryLoad(string p) { try { return JnlpArgs.Load(p); } catch (Exception ex) { Log("✗ " + ex.Message); return null; } }

        string Describe(Exception ex)
        {
            if (ex is System.Security.Authentication.AuthenticationException) return Tunnel.DescribeAlert(ex, txtHost.Text.Trim());
            string m = ex.Message;
            var we = ex as System.Net.WebException;
            if (we != null)
            {
                if (we.Status == System.Net.WebExceptionStatus.TrustFailure) return "BMC 的 HTTPS 证书不被信任（这是预期的）——请确认地址正确。";
                if (we.Status == System.Net.WebExceptionStatus.ConnectFailure) return "连不上 BMC 的 443 端口，请检查网络/地址。";
            }
            if (m.Contains("401") || m.Contains("登录失败")) return "BMC 登录失败：" + m + "\r\n  请确认用户名/密码（就是 BMC 网页的登录账号）。";
            return m;
        }

        // 目标时间：选中年份 + 现在的月日时分秒；若已越过证书有效期的 2026-05-17，则退回 2026-05-01 12:00。
        DateTime ClockTarget()
        {
            var now = DateTime.Now;
            int year = (int)numYear.Value;
            int day = Math.Min(now.Day, DateTime.DaysInMonth(year, now.Month));
            var t = new DateTime(year, now.Month, day, now.Hour, now.Minute, now.Second);
            if (t >= new DateTime(2026, 5, 17)) t = new DateTime(2026, 5, 1, 12, 0, 0);
            return t;
        }

        decimal DefaultYear()
        {
            int y = DateTime.Now.Year - 1;
            if (new DateTime(y, Math.Min(DateTime.Now.Month, 12), 1) >= new DateTime(2026, 5, 1)) y = 2026;
            if (y < 2010) y = 2010;
            if (y > 2030) y = 2030;
            return y;
        }

        void FetchOnly()
        {
            string host = txtHost.Text.Trim();
            if (host.Length == 0 || txtUser.Text.Trim().Length == 0) { MessageBox.Show(this, "请先填写 BMC 地址、用户名和密码。", "取新令牌", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            busy = true; btnFetch.Enabled = false;
            var th = new Thread(delegate ()
            {
                try
                {
                    var bmc = new Bmc(host, txtUser.Text.Trim(), txtPwd.Text);
                    bmc.Login(); Log("已登录 " + host + "。");
                    string xml = bmc.FetchJnlp();
                    string p = Path.Combine(workDir, "iKVMConnector-fresh.jnlp");
                    File.WriteAllText(p, xml, new UTF8Encoding(false));
                    var j = JnlpArgs.Load(p);
                    Log("已刷新令牌：" + j.Args[1] + "  →  " + p);
                    BeginInvoke((MethodInvoker)delegate { txtJnlp.Text = p; });
                }
                catch (Exception ex) { Log("✗ " + Describe(ex)); }
                finally { busy = false; BeginInvoke((MethodInvoker)delegate { btnFetch.Enabled = true; }); }
            });
            th.IsBackground = true; th.Start();
        }

        void Diagnose()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string diag = Path.Combine(dir, "iKVM-diag.exe");
            if (!File.Exists(diag)) { MessageBox.Show(this, "找不到 " + diag, "诊断", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string host = txtHost.Text.Trim();
            try
            {
                var psi = new ProcessStartInfo(diag, host.Length > 0 ? host : "");
                psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true;
                using (var p = Process.Start(psi))
                {
                    Log("—— 自检 ——");
                    Log(p.StandardOutput.ReadToEnd().Trim());
                    p.WaitForExit(30000);
                }
            }
            catch (Exception ex) { Log("✗ " + ex.Message); }
        }

        // 只读概览：BMC/BIOS 信息、电源、传感器读数、SEL 条数
        void ShowInfo()
        {
            string host = txtHost.Text.Trim();
            if (host.Length == 0 || txtUser.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "请先填写 BMC 地址、用户名和密码。", "服务器信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            btnInfo.Enabled = false;
            Log("读取服务器信息 …");
            var th = new Thread(delegate ()
            {
                try
                {
                    var b = new Bmc(host, txtUser.Text.Trim(), txtPwd.Text);
                    b.Login();
                    BeginInvoke((MethodInvoker)delegate { btnInfo.Enabled = true; new InfoForm(b).Show(this); });
                }
                catch (Exception ex)
                {
                    Log("✗ " + ex.Message);
                    BeginInvoke((MethodInvoker)delegate { btnInfo.Enabled = true; });
                }
            });
            th.IsBackground = true; th.Start();
        }

        void OpenWeb()
        {
            string host = txtHost.Text.Trim();
            if (host.Length == 0) { MessageBox.Show(this, "请先填写 BMC 地址。", "打开网页", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { Process.Start("https://" + host + "/"); } catch (Exception ex) { Log("✗ " + ex.Message); }
        }

        void Stop(bool killViewer)
        {
            if (killViewer && viewer != null)
            {
                try { if (!viewer.HasExited) { viewer.Kill(); Log("已结束 Viewer。"); } } catch { }
                viewer = null;
            }
            btnStop.Enabled = false; btnConnect.Enabled = true;
        }

        // ---- 配置保存 --------------------------------------------------

        static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "plain:";
            try
            {
                byte[] b = System.Security.Cryptography.ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(s), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(b);
            }
            catch { return "plain:" + s; }
        }

        static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("dpapi:"))
            {
                try
                {
                    byte[] b = System.Security.Cryptography.ProtectedData.Unprotect(
                        Convert.FromBase64String(s.Substring(6)), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(b);
                }
                catch { return ""; }
            }
            if (s.StartsWith("plain:")) return s.Substring(6);
            return s;
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(cfgPath)) return;
                foreach (var line in File.ReadAllLines(cfgPath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "host") txtHost.Text = v;
                    else if (k == "user") txtUser.Text = v;
                    else if (k == "pass") txtPwd.Text = Unprotect(v);
                    else if (k == "jnlp") txtJnlp.Text = v;
                    else if (k == "clock") chkClock.Checked = (v != "0");
                    else if (k == "year")
                    {
                        int y;
                        if (int.TryParse(v, out y) && y >= (int)numYear.Minimum && y <= (int)numYear.Maximum) numYear.Value = y;
                    }
                }
            }
            catch { }
        }

        void SaveSettings(string host, string jnlpPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cfgPath));
                // 密码用 DPAPI 按当前 Windows 用户加密，别的账号/别的机器读到也没用。
                File.WriteAllLines(cfgPath, new string[] {
                    "host=" + host,
                    "user=" + txtUser.Text.Trim(),
                    "pass=" + Protect(txtPwd.Text),
                    "jnlp=" + (jnlpPath ?? ""),
                    "clock=" + (chkClock.Checked ? "1" : "0"),
                    "year=" + numYear.Value.ToString() }, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("⚠ 保存设置失败：" + ex.Message); }
        }
    }
}
