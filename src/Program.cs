using System;
using System.Windows.Forms;

namespace iKVMConnector
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string jnlp = null, host = null, user = null, pass = null;
            bool auto = false, info = false;
            foreach (var a in args)
            {
                if (string.IsNullOrEmpty(a)) continue;
                if (a == "--auto") auto = true;
                else if (a == "--info") info = true;
                else if (a.StartsWith("--host=")) host = a.Substring(7);
                else if (a.StartsWith("--user=")) user = a.Substring(7);
                else if (a.StartsWith("--pass=")) pass = a.Substring(7);
                else if (a.StartsWith("--jnlp=")) jnlp = a.Substring(7);
                else if (!a.StartsWith("--")) jnlp = a;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(jnlp, auto, host, user, pass, info));
        }
    }
}
