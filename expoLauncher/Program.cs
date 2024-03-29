﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace expoLauncher {
    class Program {
        [DllImport("xinput1_3.dll", EntryPoint = "#100")]
        static extern int secret_get_gamepad(int playerIndex, out XINPUT_GAMEPAD_SECRET struc);
        

        public struct XINPUT_GAMEPAD_SECRET
        {
            public UInt32 eventCount;
            public ushort wButtons;
            public Byte bLeftTrigger;
            public Byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }
        
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private static readonly int VK_F5 = 0x74; // https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        private static readonly int VK_F6 = 0x75;
        private static readonly int VK_NUMPAD0 = 0x60;

        private class Config {
            public bool isBrowser = false;
            public bool isSteam = false;
            public string path = "notepad.exe";
            public string processToClose = "";
            public int steamId = 95400;
            public string chrome = "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe";
            public string args = "";
            public string[] clearFolders = new String[0];
        }

        public static int GetSteamApp() {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                if (key != null)
                {
                    Object o = key.GetValue("RunningAppID");
                    return (int)o;
                }
            }

            return 0;
        }
        static async Task Main(string[] args) {
            var config = GetConfig();
            if (config.isBrowser) {
                await StartBrowserApp(config);
            } else if (config.isSteam) {
                await StartSteam(config);
            }else {
                await StartExe(config);
            }
        }

        static Config GetConfig() {
            var path = Path.Join(Directory.GetCurrentDirectory(), "config.json");
            Console.WriteLine("using "+ path);
            if (File.Exists(path)) {
                return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
            } else {
                var config = new Config();
                File.WriteAllText(path,JsonConvert.SerializeObject(config, Formatting.Indented));
                return config;
            }
        }
        static async Task StartBrowserApp(Config config) {
            using CancellationTokenSource source = new CancellationTokenSource();
            var t = StartWebServer(config.path, source.Token);
            do {
                await StartBrowserProcess(config.chrome);
            } while (!IsF6Pressed());
        }
        static async Task StartExe(Config config) {
            do {
                Cleanup(config.clearFolders);
                await StartProcess(config.path, config.args, config.processToClose);
            } while (!IsF6Pressed());
        }
        static async Task StartSteam(Config config) {
            if (string.IsNullOrEmpty(config.processToClose)) config.processToClose = config.path;
            do {
                Cleanup(config.clearFolders);
                await StartSteamGame(config.steamId, config.processToClose);
            } while (!IsF6Pressed());
        }

        public static bool IsF5Pressed() {
            XINPUT_GAMEPAD_SECRET state; 
            for (int i = 0; i < 4; i++) {
                secret_get_gamepad(i, out state);
                if((state.wButtons & 0x0400) != 0) return true;
            }
            return ((GetAsyncKeyState(VK_F5) >> 15) & 0x0001) == 0x0001 || ((GetAsyncKeyState(VK_NUMPAD0) >> 15) & 0x0001) == 0x0001;
        }
        
        public static bool IsF6Pressed() {
            return ((GetAsyncKeyState(VK_F6) >> 15) & 0x0001) == 0x0001;
        }
        
        public static void Cleanup(string[] directoryPaths) {
            foreach (var directoryPath in directoryPaths) {
                if (string.IsNullOrEmpty(directoryPath)) continue;
                var path = Environment.ExpandEnvironmentVariables(directoryPath);
                if(!Directory.Exists(path)) continue;

                System.IO.DirectoryInfo di = new DirectoryInfo(path);

                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete(); 
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true); 
                } 
            }
        }

        public static async Task StartSteamGame(int id, string processName) {
            ProcessStartInfo startInfo = new ProcessStartInfo("cmd", $"/c start steam://rungameid/"+id);
            using var process = Process.Start(startInfo);
            
            Console.WriteLine("launching");
            while (GetSteamApp() != id) {
                await Task.Delay(50);
            }
            Console.WriteLine("launched ");
            await WaitToClose(id, processName);
        }
        
        public static async Task WaitToClose(int steamID, string processName) {
            bool closing = false;
            while (GetSteamApp() == steamID ) {
                if ((IsF5Pressed() || IsF6Pressed())&& !closing) {
                    Console.WriteLine("shouldQuit ");
                    Process[] procs = Process.GetProcesses();

                    try {
                        Process process = procs.First(x => x.ProcessName == processName);
                        process.Kill();
                    } catch (Exception e) {
                        Console.WriteLine($"COULD NOT FIND PROCESS {processName}\n running:");
                        foreach (var p in procs) {
                            Console.WriteLine($"{p.ProcessName} {p.Id} ");
                        }
                        throw;
                    }
                    closing = true;
                }

                await Task.Delay(10);
            }
        }

        public static async Task StartProcess(string exe, string args, string processToClose) {

            ProcessStartInfo startInfo = new ProcessStartInfo(exe, args);
            startInfo.WorkingDirectory = Path.GetDirectoryName(exe);
            using var process = Process.Start(startInfo);

            if(string.IsNullOrEmpty(processToClose)) processToClose = process.ProcessName;
            if (!IsF5Pressed()) {
                await Task.Delay(1000);
            }
            
            while (IsF5Pressed()) { //wait for unpress
                await Task.Delay(10);
            }

            await WaitToClose(process);
Debug.WriteLine("1");
            var relaunched = Process.GetProcessesByName(processToClose);
            if (relaunched.Length > 0) {
                Console.WriteLine("waiting for relaunch");
                await WaitToClose(relaunched[0]);
            }
            
            Console.WriteLine("closing");

            //kill all to be sure
            foreach (var p in Process.GetProcessesByName(processToClose)) {
                p.Kill();
            }
            
            Console.WriteLine("done " + processToClose);
        }

        public static async Task WaitToClose(Process process) {
            bool closing = false;
            while (process != null && !process.HasExited ) {
                if ((IsF5Pressed() || IsF6Pressed())&& !closing) {
                    process.Kill();
                    closing = true;
                }

                await Task.Delay(10);
            }
        }

        public static async Task StartBrowserProcess(string chromeExe) {
            foreach (var p in Process.GetProcessesByName("chrome"))
            {
                Console.WriteLine("killing chrome");
                p.Kill();
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(chromeExe, " --kiosk  \"http://localhost:5000/index.html\"  --incognito --start-fullscreen --disable-pinch --no-user-gesture-required --overscroll-history-navigation=0");

            using var process = Process.Start(startInfo);
            
            bool closing = false;
            while (process != null && !process.HasExited ) {
                if ( IsF6Pressed()&& !closing) {
                    process.Kill();
                    closing = true;
                }

                await Task.Delay(10);
            }
            Console.WriteLine("done");
        }

        public static async Task StartWebServer(string path, 
                                                CancellationToken cancellationToken = default(CancellationToken)) {
            await WebHost
               .CreateDefaultBuilder() //default on localhost:5000
               .Configure(config => config.UseStaticFiles())
               .UseWebRoot(path).Build()
               .RunAsync(cancellationToken);
        }
    }

    public static class Extensions {
        /// <summary>
        /// Waits asynchronously for the process to exit.
        /// </summary>
        /// <param name="process">The process to wait for cancellation.</param>
        /// <param name="cancellationToken">A cancellation token. If invoked, the task will return 
        /// immediately as canceled.</param>
        /// <returns>A Task representing waiting for the process to end.</returns>
        public static Task WaitForExitAsync(this Process process,
                                            CancellationToken cancellationToken = default(CancellationToken)) {
            if (process.HasExited) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if (cancellationToken != default(CancellationToken))
                cancellationToken.Register(() => tcs.SetCanceled());

            return process.HasExited ? Task.CompletedTask : tcs.Task;
        }
    }
}