using System;
using System.Windows;

namespace cpss
{
    class Program
    {
        public const string CLIENT_ARG = "client";

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == CLIENT_ARG)
            {
                Client.Main(args);
            }
            else
            {
                ServerApp.Main();
            }
        }
    }
}
