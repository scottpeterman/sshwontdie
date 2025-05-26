using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using SSHPassCSharp.Core;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SSHPassCSharp.CLI
{
    class Program
    {
        public class Options
        {
            [Option('h', "host", Required = true, HelpText = "SSH Host (ip:port)")]
            public string Host { get; set; }

            [Option('u', "user", Required = true, HelpText = "SSH Username")]
            public string Username { get; set; }

            [Option('p', "password", Required = true, HelpText = "SSH Password")]
            public string Password { get; set; }

            [Option('c', "cmds", Default = "", HelpText = "Commands to run, separated by comma")]
            public string Commands { get; set; }

            [Option("invoke-shell", Default = false, HelpText = "Invoke shell before running the command")]
            public bool InvokeShell { get; set; }

            [Option("prompt", Default = "", HelpText = "Prompt to look for before breaking the shell")]
            public string Prompt { get; set; }

            [Option("prompt-count", Default = 1, HelpText = "Number of prompts to look for before breaking the shell")]
            public int PromptCount { get; set; }

            [Option('t', "timeout", Default = 360, HelpText = "Command timeout duration in seconds")]
            public int Timeout { get; set; }

            [Option("shell-timeout", Default = 10, HelpText = "Overall shell session timeout in seconds (default is 10 seconds)")]
            public int ShellTimeout { get; set; }

            [Option('i', "inter-command-time", Default = 1, HelpText = "Inter-command time in seconds")]
            public int InterCommandTime { get; set; }

            [Option("log-file", Default = "", HelpText = "Path to log file (default is ./logs/hostname.log)")]
            public string LogFile { get; set; }
            
            [Option("require-hyphen", Default = false, HelpText = "Require hyphen in prompt detection (for Cisco/network devices)")]
            public bool RequireHyphenInPrompt { get; set; }
            
            [Option('d', "debug", Default = false, HelpText = "Enable debug output")]
            public bool Debug { get; set; }
        }

        static async Task<int> Main(string[] args)
        {
            return await Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    async options => await RunWithOptions(options),
                    errors => Task.FromResult(1)
                );
        }

        static async Task<int> RunWithOptions(Options options)
        {
            // Parse host and port
            string host = options.Host;
            int port = 22;

            if (options.Host.Contains(':'))
            {
                var parts = options.Host.Split(':');
                host = parts[0];
                if (int.TryParse(parts[1], out int parsedPort))
                {
                    port = parsedPort;
                }
            }

            // Set log file path if not specified
            if (string.IsNullOrEmpty(options.LogFile))
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDirectory);
                options.LogFile = Path.Combine(logDirectory, $"{host}.log");
            }

            try
            {
                // Create SSH client with options
                var sshClientOptions = new SSHClient.SSHClientOptions
                {
                    Host = host,
                    Port = port,
                    Username = options.Username,
                    Password = options.Password,
                    InvokeShell = options.InvokeShell,
                    Prompt = options.Prompt,
                    PromptCount = options.PromptCount,
                    Timeout = options.Timeout,
                    ShellTimeout = options.ShellTimeout,
                    InterCommandTime = options.InterCommandTime,
                    LogFile = options.LogFile,
                    RequireHyphenInPrompt = options.RequireHyphenInPrompt,
                    Debug = options.Debug  // Pass the debug flag
                };

                using (var client = new SSHClient(sshClientOptions))
                {
                    await client.Connect();

                    // Important: If using invoke-shell, handle ALL commands as a single session
                    if (options.InvokeShell)
                    {
                        string allCommands = options.Commands;
                        // For empty commas, we'll send a newline (which is the Python behavior)
                        allCommands = allCommands.Replace(",,", ",\n,");
                        await client.ExecuteCommand(allCommands);
                    }
                    else
                    {
                        // For direct command mode, execute each command separately
                        string[] commandList = options.Commands
                            .Split(',')
                            .Select(cmd => cmd.Trim().Replace("\"", "")) // Remove quotes similar to Python
                            .Where(cmd => !string.IsNullOrEmpty(cmd))
                            .ToArray();

                        foreach (var command in commandList)
                        {
                            await client.ExecuteCommand(command);
                        }
                    }

                    client.Disconnect();
                }

                return 0; // Success
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                
                if (options.Debug)
                {
                    Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        Console.Error.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                    }
                }
                
                return 1; // Error
            }
        }
    }
}