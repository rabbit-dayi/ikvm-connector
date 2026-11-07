using System;
using System.IO;

namespace iKVMConnector
{
    static class DiagMain
    {
        static void Main(string[] args)
        {
            ConsoleDiag.Run(args.Length > 0 ? args[0] : null);
        }
    }
}
