﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;

namespace expoLauncher {
    class Program {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private static readonly int VK_F5 = 0x74; // https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        private static readonly int VK_F6 = 0x75;

        private class Config {
            public bool isBrowser = true;
            public string path = "D:\\itchGames\\coming-out-simulator-2014";
            public string chrome = "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe";
            public string args = "-screen-fullscreen";
            public string[] clearFolders = new String[0];
        }
        
        static async Task Main(string[] args) {
            var config = GetConfig();
            if (config.isBrowser) {
                await StartBrowserApp(config);
            } else {
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
                await StartProcess(config.path, config.args);
            } while (!IsF6Pressed());
        }

        public static bool IsF5Pressed() {
            return ((GetAsyncKeyState(VK_F5) >> 15) & 0x0001) == 0x0001;
        }
        
        public static bool IsF6Pressed() {
            return ((GetAsyncKeyState(VK_F6) >> 15) & 0x0001) == 0x0001;
        }
        public static void Cleanup(string[] directoryPaths) {
            foreach (var directoryPath in directoryPaths) {
                if (string.IsNullOrEmpty(directoryPath)) continue;
                if(!Directory.Exists(directoryPath)) continue;

                System.IO.DirectoryInfo di = new DirectoryInfo(directoryPath);

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

        public static async Task StartProcess(string exe, string args) {

            ProcessStartInfo startInfo = new ProcessStartInfo(exe, args);

            using var process = Process.Start(startInfo);

            bool closing = false;
            
            while (IsF5Pressed()) { //wait for unpress
                await Task.Delay(10);
            }
            
            while (process != null && !process.HasExited ) {
                if ((IsF5Pressed() || IsF6Pressed())&& !closing) {
                    process.Kill();
                    closing = true;
                }

                await Task.Delay(10);
            }
            Console.WriteLine("done");
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