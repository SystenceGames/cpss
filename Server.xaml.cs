using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace cpss
{
    public partial class Server : Window
    {
        private const int ORCHESTRATOR_HEIGHT_PIXELS = 60;
        string username = string.Empty;
        string password = string.Empty;
        string clientFileName = string.Empty;
        List<string> connectionUris = new List<string>();
        Dictionary<Process, NamedPipeServerStream> pipeServerByProcess = new Dictionary<Process, NamedPipeServerStream>();
        Dictionary<NamedPipeServerStream, StreamWriter> streamWriterByPipeServer = new Dictionary<NamedPipeServerStream, StreamWriter>();
        private IntPtr mMainWindowHandle;
        Window mainWindow;
        private bool bConsoleAttached;

        [DllImport("Kernel32")]
        public static extern void FreeConsole();

        [DllImport("Kernel32", SetLastError = true)]
        public static extern bool AttachConsole(int dwProcessId);
        
        [DllImport("user32.dll")]
        private extern static bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

        public Server()
        {
            InitializeComponent();

            Loaded += WindowLoaded;

            Closed += WindowClosed;
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            if (bConsoleAttached)
            {
                FreeConsole();
            }
            disposeOfStreamWriters(streamWriterByPipeServer);
            disposeOfPipeServers(pipeServerByProcess);
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();

            if (AttachConsole(-1))
            {
                bConsoleAttached = true;
            }

            Run(args);
        }

        protected override void OnKeyDown(KeyEventArgs keyEventArgs)
        {
            base.OnKeyDown(keyEventArgs);

            foreach (NamedPipeServerStream pipeServer in pipeServerByProcess.Values)
            {
                try
                {
                    StreamWriter streamWriter = streamWriterByPipeServer[pipeServer];
                    writeKeyToStream(keyEventArgs.Key, streamWriter);
                }
                catch (Exception exception)
                {

                }
            }
        }

        void Run(string[] args)
        {
            if (args.Length < 3)
            {
                ShutdownInError("inappropriate number of arguments.  Please use the form: cpss.exe username=USERNAME password=PASSWORD https://dothejig.com:5986/");
                return;
            }

            username = getArgumentValue("username", args);
            if (string.IsNullOrEmpty(username))
            {
                ShutdownInError("No username specified.  Please specify a username with username=USERNAME");
                return;
            }
            password = getArgumentValue("password", args);
            if (string.IsNullOrEmpty(password))
            {
                ShutdownInError("No password specified.  Please specify a password with password=PASSWORD");
                return;
            }
            clientFileName = getArgumentValue("clientFileName", args); // this is a dev parameter. pass clientFileName=cpss.exe to circument Visual Studio's process-wrapping behavior.

            connectionUris = buildConnectionUris(args);
            if (connectionUris.Count < 1)
            {
                ShutdownInError("No connectionUris specified.  Please specify one or more connection uris like https://dothejig.com:5986/");
                return;
            }

            mMainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
            
            mainWindow = Application.Current.MainWindow;

            for (int i = 0; i < connectionUris.Count; i++)
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo();
                processStartInfo.FileName = determineClientFileName();
                string pipeName = buildPipeName(i);
                NamedPipeServerStream pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.Out);
                StreamWriter streamWriter = new StreamWriter(pipeServer);
                streamWriterByPipeServer.Add(pipeServer, streamWriter);
                processStartInfo.Arguments = string.Format("{0} {1} {2} {3} {4}", Program.CLIENT_ARG, username, password, connectionUris.ElementAt(i), pipeName);
                processStartInfo.WindowStyle = ProcessWindowStyle.Normal;
                processStartInfo.CreateNoWindow = false;

                Process process = Process.Start(processStartInfo);
                pipeServerByProcess.Add(process, pipeServer);
            }

            waitForClientsToConnect(pipeServerByProcess);

            arrangeWindows();

            SetOrchestratorAsTopWindow();
        }

        private string determineClientFileName()
        {
            if (string.IsNullOrEmpty(clientFileName))
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            
            return clientFileName;
        }

        private void ShutdownInError(string errorMessage)
        {
            Console.Error.WriteLine(errorMessage);
            if (bConsoleAttached)
            {
                FreeConsole();
            }
            Application.Current.Shutdown(-1);
        }

        private void SetOrchestratorAsTopWindow()
        {
            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
            mainWindow.Topmost = false;
            mainWindow.Focus();
        }

        private  void arrangeWindows()
        {
            PresentationSource mainWindowPresentationSource = PresentationSource.FromVisual(mainWindow);
            Matrix matrix = mainWindowPresentationSource.CompositionTarget.TransformToDevice;
            double dpiWidthFactor = matrix.M11;
            double dpiHeightFactor = matrix.M22;
            
            double screenWidthInches = SystemParameters.WorkArea.Width;
            double screenHeightInches = SystemParameters.WorkArea.Height;
            double orchestratorHeightInches = ((double)ORCHESTRATOR_HEIGHT_PIXELS) * dpiHeightFactor;

            double orchestratorWidthInches = screenWidthInches;
            double orchestratorYInches = screenHeightInches - orchestratorHeightInches;
            SetOrchestratorWindowPosition(orchestratorHeightInches, orchestratorWidthInches, 0, orchestratorYInches); // this guy in "inches" WPF style
            System.Diagnostics.Debug.WriteLine(String.Format("OrchestratorWindow xIn: {0} yIn: {1} heightIn: {2} widthIn: {3}", 0, orchestratorYInches, orchestratorHeightInches, orchestratorWidthInches));
            System.Diagnostics.Debug.WriteLine(String.Format("OrchestratorWindow xPx: {0} yPx: {1} heightPx: {2} widthPx: {3}", 0, orchestratorYInches * dpiHeightFactor, orchestratorHeightInches * dpiHeightFactor, orchestratorWidthInches * dpiWidthFactor));

            int screenWidthPixels = (int)(screenWidthInches * dpiWidthFactor);
            int remainingScreenHeightPixels = (int)((screenHeightInches - orchestratorHeightInches) * dpiHeightFactor);
            System.Diagnostics.Debug.WriteLine(String.Format("workingHeightPx: {0} workingWidthPx: {1}", remainingScreenHeightPixels, screenWidthPixels));

            int numberOfProcesses = pipeServerByProcess.Keys.Count;
            int numberOfTilesWide = (int)Math.Max(1.0, Math.Ceiling(Math.Sqrt(numberOfProcesses)));
            int numberOfTilesHigh = (int)Math.Max(1.0, Math.Ceiling((double)numberOfProcesses / (double)numberOfTilesWide));

            int tileHeight = remainingScreenHeightPixels / numberOfTilesHigh;
            int tileWidth = screenWidthPixels / numberOfTilesWide;
            System.Diagnostics.Debug.WriteLine(String.Format("numTileWide: {0} numTileHigh: {1} tileHeight: {2} tileWidth: {3}", numberOfTilesWide, numberOfTilesHigh, tileHeight, tileWidth));
            for (int i = 0; i < numberOfProcesses; i++)
            {
                Process process = pipeServerByProcess.Keys.ElementAt(i);
                int tileRemainder = i % numberOfTilesWide;
                int tileQuotient = i / numberOfTilesWide;
                int xPos = tileRemainder * tileWidth;
                int yPos = tileQuotient * tileHeight;
                int width = tileWidth;
                int height = tileHeight;

                bool bSetWindowPos = SetWindowPos(process.MainWindowHandle, mMainWindowHandle, xPos, yPos, width, height, 0); // all these in pixels, Windows Forms style
                if (!bSetWindowPos)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to set window position for process: {0}", process.Id);
                }
                System.Diagnostics.Debug.WriteLine(String.Format("xPosPx: {0} yPosPx: {1} widthPx: {2} heightPx: {3}", xPos, yPos, width, height));
            }
        }

        private void SetOrchestratorWindowPosition(double height, double width, double xPosition, double yPosition)
        {
            this.Height = height;
            this.Width = width;
            this.Top = yPosition;
            this.Left = xPosition;
        }

        private void writeKeyToStream(Key key, StreamWriter streamWriter)
        {
            int writeMe = KeyInterop.VirtualKeyFromKey(key);
            streamWriter.Write(((char)writeMe));
            streamWriter.Flush();
        }

        private static void disposeOfStreamWriters(Dictionary<NamedPipeServerStream, StreamWriter> streamWriterByPipeServer)
        {
            foreach (StreamWriter streamWriter in streamWriterByPipeServer.Values)
            {
                streamWriter.Dispose();
            }
        }

        private static void disposeOfPipeServers(Dictionary<Process, NamedPipeServerStream> pipeServerByProcess)
        {
            foreach (NamedPipeServerStream pipeServer in pipeServerByProcess.Values)
            {
                pipeServer.Dispose();
            }
        }

        private static void waitForClientsToConnect(Dictionary<Process, NamedPipeServerStream> pipeServerByProcess)
        {
            foreach (NamedPipeServerStream pipeServer in pipeServerByProcess.Values)
            {
                pipeServer.WaitForConnection();
            }
            Console.WriteLine("All Clients Connected");
        }

        private string buildPipeName(int i)
        {
            return "\\\\.\\pipe\\cpss" + "-" + Process.GetCurrentProcess().Id + "-" + i;
        }

        private static List<string> buildConnectionUris(string[] args)
        {
            List<string> result = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("username=") && !arg.StartsWith("password=") && !arg.StartsWith("clientFileName="))
                {
                    result.Add(arg);
                }
            }
            return result;
        }

        private static string getArgumentValue(string arg, string[] args)
        {
            foreach (string singleArg in args)
            {
                string argPrefix = arg + "=";
                if (singleArg.StartsWith(argPrefix))
                {
                    return singleArg.Substring(argPrefix.Length);
                }
            }

            return null;
        }
    }
}
