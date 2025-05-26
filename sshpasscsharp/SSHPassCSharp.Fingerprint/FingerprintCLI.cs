using System;
using System.IO;
using CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSHPassCSharp.Fingerprint
{
    class FingerprintCLI
    {
        public class Options
        {
            [Option('h', "host", Required = true, HelpText = "SSH Host (ip:port)")]
            public string Host { get; set; }

            [Option('u', "user", Required = true, HelpText = "SSH Username")]
            public string Username { get; set; }

            [Option('p', "password", Required = true, HelpText = "SSH Password")]
            public string Password { get; set; }
            
            [Option('o', "output", Default = "", HelpText = "Output file for the fingerprint results (JSON)")]
            public string OutputFile { get; set; }
            
            [Option('d', "debug", Default = false, HelpText = "Enable debug output")]
            public bool Debug { get; set; }
            
            [Option('v', "verbose", Default = false, HelpText = "Show verbose output including command results")]
            public bool Verbose { get; set; }
        }

        public static int Run(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    options => RunWithOptions(options),
                    errors => 1
                );
        }

        static int RunWithOptions(Options options)
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

            try
            {
                Console.WriteLine($"Fingerprinting device at {host}:{port}...");
                
                // Create fingerprinter
                var fingerprinter = new DeviceFingerprint(
                    host, 
                    port, 
                    options.Username, 
                    options.Password, 
                    options.Verbose ? Console.Write : null, // Only show verbose output if requested
                    options.Debug
                );
                
                // Run fingerprinting
                var deviceInfo = fingerprinter.Fingerprint(options.Verbose);
                
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
                
                // Save to file if requested
                if (!string.IsNullOrEmpty(options.OutputFile))
                {
                    // Configure JSON serialization
                    var jsonOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };
                    
                    string json = JsonSerializer.Serialize(deviceInfo, jsonOptions);
                    File.WriteAllText(options.OutputFile, json);
                    Console.WriteLine($"\nDevice fingerprint saved to {options.OutputFile}");
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