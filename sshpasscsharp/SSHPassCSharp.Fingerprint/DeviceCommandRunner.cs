using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Text;

namespace SSHPassCSharp.Fingerprint
{
    public class DeviceCommandRunner : IDisposable
    {
        private readonly Core.SSHClient _sshClient;
        private readonly DeviceInfo _deviceInfo;
        private bool _isConnected = false;
        private readonly StringBuilder _outputBuffer = new StringBuilder();
        private readonly bool _debug;
        
        public DeviceCommandRunner(DeviceInfo deviceInfo, bool debug = false)
        {
            _deviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
            _debug = debug;
            
            // Configure SSH client using info from fingerprint
            var sshOptions = new Core.SSHClient.SSHClientOptions
            {
                Host = deviceInfo.Host,
                Port = deviceInfo.Port,
                Username = deviceInfo.Username,
                Password = deviceInfo.Password,
                InvokeShell = true,
                Prompt = deviceInfo.DetectedPrompt,
                PromptCount = 1,
                ShellTimeout = 30,        // 30 second timeout
                InterCommandTime = 1,     // 1 second between commands
                Debug = debug
            };
            
            // Set up output capture
            Action<string> bufferCallback = (output) => {
                _outputBuffer.Append(output);
            };
            
            sshOptions.OutputCallback = bufferCallback;
            
            _sshClient = new Core.SSHClient(sshOptions);
        }
        
        public void Connect()
        {
            try
            {
                if (_debug)
                    Console.WriteLine($"Connecting to {_deviceInfo.Host}:{_deviceInfo.Port}...");
                
                // Connect - synchronous now
                _sshClient.Connect();
                _isConnected = true;
                
                if (_debug)
                    Console.WriteLine("Successfully connected!");
                
                // Wait a moment to ensure connection is fully established
                Thread.Sleep(500);
                
                // Disable paging if we know how
                if (!string.IsNullOrEmpty(_deviceInfo.DisablePagingCommand))
                {
                    if (_debug)
                        Console.WriteLine($"Disabling paging with: {_deviceInfo.DisablePagingCommand}");
                    
                    // Execute the command - synchronous now
                    _sshClient.ExecuteCommand(_deviceInfo.DisablePagingCommand);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
                throw;
            }
        }
        
        public string ExecuteCommand(string command, int timeout = 5000)
        {
            if (!_isConnected)
            {
                Connect();
            }
            
            if (_debug)
                Console.WriteLine($"Executing command: {command}");
            
            // Clear output buffer
            _outputBuffer.Clear();
            
            // Execute command - synchronous now
            _sshClient.ExecuteCommand(command);
            
            // Give some time for output to fully arrive
            Thread.Sleep(500);
            
            // Return output
            return _outputBuffer.ToString();
        }
        
        public Dictionary<string, string> ExecuteCommonCommands()
        {
            var results = new Dictionary<string, string>();
            
            // Define common command types based on device type
            var commandTypes = new List<string>();
            
            switch (_deviceInfo.DeviceType)
            {
                case DeviceType.CiscoIOS:
                case DeviceType.CiscoNXOS:
                case DeviceType.CiscoASA:
                case DeviceType.AristaEOS:
                    commandTypes.AddRange(new[] { "interfaces", "routes", "config", "neighbors", "inventory", "version" });
                    break;
                    
                case DeviceType.JuniperJunOS:
                    commandTypes.AddRange(new[] { "interfaces", "routes", "config", "neighbors", "inventory", "version" });
                    break;
                    
                case DeviceType.FortiOS:
                case DeviceType.PaloAltoOS:
                    commandTypes.AddRange(new[] { "interfaces", "routes", "config", "version" });
                    break;
                    
                case DeviceType.Linux:
                case DeviceType.FreeBSD:
                    commandTypes.AddRange(new[] { "interfaces", "routes", "cpu", "memory", "disk", "version" });
                    break;
                    
                default:
                    // For unknown device types, no common commands
                    break;
            }
            
            // Execute each common command
            foreach (var commandType in commandTypes)
            {
                try
                {
                    string command = _deviceInfo.DeviceType.GetCommand(commandType);
                    if (!string.IsNullOrEmpty(command))
                    {
                        if (_debug)
                            Console.WriteLine($"Running common command: {commandType} ({command})");
                            
                        string output = ExecuteCommand(command);
                        results[commandType] = output;
                    }
                }
                catch (Exception ex)
                {
                    if (_debug)
                        Console.WriteLine($"Error executing {commandType}: {ex.Message}");
                        
                    results[$"{commandType}_error"] = ex.Message;
                }
            }
            
            return results;
        }

        public void SaveCommandOutput(string command, string filePath)
        {
            string output = ExecuteCommand(command);
            File.WriteAllText(filePath, output);
        }

        public void Disconnect()
        {
            _sshClient?.Disconnect();
            _isConnected = false;
        }

        public void Dispose()
        {
            Disconnect();
            _sshClient?.Dispose();
        }
    }
}