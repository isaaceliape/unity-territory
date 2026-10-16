// Entry point: `Territory.Server [--port 4173] [--wwwroot DIR] [--results-log FILE]`
// Same hosting story as `npm start`: serves the client over HTTP, upgrades to
// WebSocket on the same port, and prints the LAN addresses to share.
using System;
using System.IO;
using System.Threading;
using Territory.Sim;

namespace Territory.Server
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            int port = 0;
            string envPort = Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int ep)) port = ep;
            string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            string resultsLog = Path.Combine(Directory.GetCurrentDirectory(), "logs", "results.log");
            for (int i = 0; i < args.Length; i += 1)
            {
                if (args[i] == "--port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
                else if (args[i] == "--wwwroot" && i + 1 < args.Length) wwwroot = args[++i];
                else if (args[i] == "--results-log" && i + 1 < args.Length) resultsLog = args[++i];
            }
            if (port == 0) port = 4173;

            var options = new ServerOptions
            {
                Port = port,
                WwwRoot = wwwroot,
                ResultsLog = resultsLog,
                GameOptions = new GameOptions(),
                Log = true,
            };
            GameHost host = null;
            try
            {
                host = new GameHost(options);
                host.Start();
            }
            catch (Exception e)
            {
                string msg = e.ToString();
                if (msg.Contains("in use") || msg.Contains("EADDRINUSE"))
                    Console.Error.WriteLine($"Port {port} is already in use.");
                else
                    Console.Error.WriteLine(e.Message);
                return 1;
            }
            var done = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; done.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => done.Set();
            done.Wait();
            host.Dispose();
            return 0;
        }
    }
}
