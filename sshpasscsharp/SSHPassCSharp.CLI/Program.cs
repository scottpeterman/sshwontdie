using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using SSHPassCSharp.Core;
using SSHPassCSharp.Fingerprint;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text.Json;

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
            
            [Option('f', "fingerprint", Default = false, HelpText = "Fingerprint device before executing commands")]
            public bool Fingerprint { get; set; }
            
            [Option('o', "fingerprint-output", Default = "", HelpText = "Save fingerprint results to JSON file")]
            public string FingerprintOutput { get; set; }
            
            [Option('v', "verbose", Default = false, HelpText = "Show verbose output")]
            public bool Verbose { get; set; }
            
            [Option('s', "save", Default = "", HelpText = "File path to save command output")]
            public string SaveOutputFile { get; set; }
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

            // Create a StringBuilder to collect all command outputs
            var commandOutputs = new StringBuilder();

            try
            {
                // If fingerprinting is enabled, do that first
                DeviceInfo deviceInfo = null;
                
                if (options.Fingerprint)
                {
                    Console.WriteLine($"Fingerprinting device at {host}:{port}...");
                    
                    // Create fingerprinter with appropriate verbosity
                    var fingerprinter = new DeviceFingerprint(
                        host,
                        port,
                        options.Username,
                        options.Password,
                        options.Verbose ? Console.Write : null, // Only show verbose output if requested
                        options.Debug,
                        options.Verbose
                    );
                    
                    // Run fingerprinting
                    deviceInfo = fingerprinter.Fingerprint(options.Verbose);
                    
                    // Display results
                    Console.WriteLine("\nDevice Fingerprinting Results:");
                    Console.WriteLine("-------------------------------------------------");
                    Console.WriteLine($"Host: {deviceInfo.Host}:{deviceInfo.Port}");
                    Console.WriteLine($"Detected Prompt: {deviceInfo.DetectedPrompt}");
                    Console.WriteLine($"Device Type: {deviceInfo.DeviceType}");
                    
                    if (!string.IsNullOrEmpty(deviceInfo.Hostname))
                        Console.WriteLine($"Hostname: {deviceInfo.Hostname}");
                    
                    if (!string.IsNullOrEmpty(deviceInfo.Model))
                        Console.WriteLine($"Model: {deviceInfo.Model}");
                    
                    if (!string.IsNullOrEmpty(deviceInfo.Version))
                        Console.WriteLine($"Version: {deviceInfo.Version}");
                    
                    if (!string.IsNullOrEmpty(deviceInfo.SerialNumber))
                        Console.WriteLine($"Serial Number: {deviceInfo.SerialNumber}");
                    
                    Console.WriteLine($"Disable Paging Command: {deviceInfo.DisablePagingCommand}");
                    
                    // Save fingerprint to file if requested
                    if (!string.IsNullOrEmpty(options.FingerprintOutput))
                    {
                        var jsonOptions = new JsonSerializerOptions
                        {
                            WriteIndented = true
                        };
                        
                        string json = JsonSerializer.Serialize(deviceInfo, jsonOptions);
                        File.WriteAllText(options.FingerprintOutput, json);
                        Console.WriteLine($"\nDevice fingerprint saved to {options.FingerprintOutput}");
                    }
                    
                    // Check if there are commands to run
                    if (string.IsNullOrEmpty(options.Commands))
                    {
                        // No commands specified, we're done
                        fingerprinter.Disconnect();
                        return 0;
                    }
                    
                    // Continue to run commands on the existing connection
                    string[] commandArray = options.Commands.Split(',');
                    
                    // Execute commands on the existing connection
                    Console.WriteLine("\nExecuting commands:");
                    foreach (var command in commandArray)
                    {
                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            Console.WriteLine($"\n> {command}");
                            string output = fingerprinter.ExecuteCommand(command);
                            commandOutputs.AppendLine($"> {command}");
                            commandOutputs.AppendLine(output);
                            commandOutputs.AppendLine();
                        }
                    }
                    
                    // Disconnect when done
                    fingerprinter.Disconnect();
                }
                else
                {
                    // If not fingerprinting, use standard SSH client
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
                        Debug = options.Debug
                    };

                    using (var client = new SSHClient(sshClientOptions))
                    {
                        client.Connect();

                        // Important: If using invoke-shell, handle ALL commands as a single session
                        if (options.InvokeShell)
                        {
                            string allCommands = options.Commands;
                            // For empty commas, we'll send a newline (which is the Python behavior)
                            allCommands = allCommands.Replace(",,", ",\n,");
                            string output = client.ExecuteCommand(allCommands);
                            commandOutputs.AppendLine(output);
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
                                string output = client.ExecuteCommand(command);
                                commandOutputs.AppendLine($"> {command}");
                                commandOutputs.AppendLine(output);
                                commandOutputs.AppendLine();
                            }
                        }

                        client.Disconnect();
                    }
                }

                // Save command outputs if requested
                if (!string.IsNullOrEmpty(options.SaveOutputFile))
                {
                    string saveDir = Path.GetDirectoryName(options.SaveOutputFile);
                    if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    {
                        Directory.CreateDirectory(saveDir);
                    }
                    
                    File.WriteAllText(options.SaveOutputFile, commandOutputs.ToString());
                    Console.WriteLine($"\nCommand output saved to {options.SaveOutputFile}");
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