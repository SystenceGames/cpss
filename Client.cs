using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace cpss
{
    class Client
    {
        const int WM_KEYDOWN = 0x100;
        string username;
        string password;
        string connectionUri;
        string pipeName;
        NamedPipeClientStream pipeClient;
        bool exited = false;
        private Thread pipeReaderThread;

        [DllImport("Kernel32")]
        public static extern void AllocConsole();

        [DllImport("Kernel32")]
        public static extern void FreeConsole();

        [DllImport("User32.Dll", EntryPoint = "PostMessageA")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, int lParam);

        public static void Main(string[] args)
        {
            AllocConsole();
            new Client().Run(args);
            Console.WriteLine("Free console and then exiting...");
            FreeConsole();
            Process.GetCurrentProcess().CloseMainWindow();
        }

        public void Run(string[] args)
        {
            username = args[1];
            password = args[2];
            connectionUri = args[3];
            pipeName = args[4];

            pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
            pipeClient.Connect();

            ProcessStartInfo processStartInfo = buildProcessStartInfo();

            Process powershellProcess = new Process();
            powershellProcess.Exited += subprocessExited;
            powershellProcess.StartInfo = processStartInfo;
            
            powershellProcess.Start();

            StreamReader streamReader = new StreamReader(pipeClient);

            WriteThrough(streamReader, powershellProcess.StandardInput);

            if (pipeReaderThread != null)
            {
                pipeReaderThread.Join();
            }
            Console.WriteLine("Cleaning up pipe...");
            if (streamReader != null)
            {
                streamReader.Dispose();
            }
            if (pipeClient != null)
            {
                pipeClient.Dispose();
            }
            Console.WriteLine("Cleaning up Powershell Process");
            if (powershellProcess != null)
            {
                powershellProcess.Dispose();
            }
        }

        private ProcessStartInfo buildProcessStartInfo()
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = "powershell.exe";
            processStartInfo.WorkingDirectory = "C:\\TME\\Tools\\PlatformScripts\\";
            string powershellStartupCommands = string.Format("$secureString = convertto-securestring -String {0} -AsPlainText -Force; " +
                        "$Credentials = new-object -typename System.Management.Automation.PSCredential -argumentlist {1}, $secureString;" +
                        "Enter-PSSession -connectionuri {2} -Credential $Credentials;", password, username, connectionUri);
            processStartInfo.Arguments = string.Format("-ExecutionPolicy Unrestricted -NoExit " + powershellStartupCommands);
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardInput = true;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;
            return processStartInfo;
        }

        private void subprocessExited(object sender, EventArgs e)
        {
            exited = true;         
            Console.WriteLine("Subprocess exited");
        }

        private void WriteThrough(StreamReader reader, StreamWriter writer)
        {
            pipeReaderThread = new Thread(() =>
            {
                int currentChar;
                while ((currentChar = reader.Read()) >= 0)
                {
                    var hWnd = Process.GetCurrentProcess().MainWindowHandle;
                    PostMessage(hWnd, WM_KEYDOWN, currentChar, 0);
                }
                Console.Write("Ran out of stuff to read from pipe");
                exited = true;
            });
            pipeReaderThread.Start();
        }
    }
}

