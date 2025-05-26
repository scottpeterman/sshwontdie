using System;
using System.Threading;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SSHPassCSharp.Core
{
    public class SSHClient : IDisposable
    {
        public class SSHClientOptions
        {
            public string Host { get; set; }
            public int Port { get; set; } = 22;
            public string Username { get; set; }
            public string Password { get; set; }
            public bool InvokeShell { get; set; }
            public string Prompt { get; set; }  // Legacy prompt pattern
            public string ExpectPrompt { get; set; } // New: specific prompt string to expect
            public int PromptCount { get; set; } = 1;
            public int Timeout { get; set; } = 360;
            public int ShellTimeout { get; set; } = 5;  // Default shell timeout in seconds
            public int InterCommandTime { get; set; } = 1;
            public string LogFile { get; set; }
            public Action<string> OutputCallback { get; set; }
            public Action<string> ErrorCallback { get; set; }
            public bool RequireHyphenInPrompt { get; set; } = false;
            public bool Debug { get; set; } = false;
            public int ExpectPromptTimeout { get; set; } = 30000; // Default timeout for expect prompt in ms
        }

        private readonly SSHClientOptions _options;
        private SshClient _sshClient;
        private ShellStream _shellStream;
        private StringBuilder _outputBuffer = new StringBuilder();
        private bool _promptDetected = false;

        public SSHClient(SSHClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            
            if (string.IsNullOrEmpty(_options.Host))
                throw new ArgumentException("Host is required", nameof(options.Host));
            
            if (string.IsNullOrEmpty(_options.Username))
                throw new ArgumentException("Username is required", nameof(options.Username));
            
            if (string.IsNullOrEmpty(_options.Password))
                throw new ArgumentException("Password is required", nameof(options.Password));
            
            // Default callbacks if none provided
            _options.OutputCallback ??= Console.Write;
            _options.ErrorCallback ??= Console.Error.Write;
        }

        // Helper method to log with timestamp
        private void LogWithTimestamp(string message, bool alwaysPrint = false)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string timestampedMessage = $"[{timestamp}] {message}";
            
            // Always print to console if debug mode is on or alwaysPrint is true
            if (_options.Debug || alwaysPrint)
            {
                Console.WriteLine(timestampedMessage);
            }
            
            // Always log to file if LogFile is set
            LogMessage(timestampedMessage);
        }

        public void Connect()
        {
            LogWithTimestamp($"Connecting to {_options.Host}:{_options.Port}...", true);
            
            // Create connection info with multiple authentication methods
            var connectionInfo = new ConnectionInfo(
                _options.Host,
                _options.Port,
                _options.Username,
                // Add multiple authentication methods to handle different device requirements
                new AuthenticationMethod[]
                {
                    // Password authentication
                    new PasswordAuthenticationMethod(_options.Username, _options.Password),
                    // Keyboard interactive (for devices that prompt for password)
                    new KeyboardInteractiveAuthenticationMethod(_options.Username)
                });

            // Set up handler for keyboard interactive authentication
            if (connectionInfo.AuthenticationMethods.FirstOrDefault(m => m is KeyboardInteractiveAuthenticationMethod) 
                is KeyboardInteractiveAuthenticationMethod keyboard)
            {
                keyboard.AuthenticationPrompt += (sender, e) =>
                {
                    foreach (var prompt in e.Prompts)
                    {
                        if (prompt.Request.ToLower().Contains("password"))
                        {
                            prompt.Response = _options.Password;
                        }
                    }
                };
            }
            
            _sshClient = new SshClient(connectionInfo);
            _sshClient.Connect();
            
            LogWithTimestamp($"Connected to {_options.Host}:{_options.Port}", true);

            // Create shell stream if we're using shell mode
            if (_options.InvokeShell)
            {
                CreateShellStream();
                
                // Check if a prompt pattern is defined
                if (string.IsNullOrEmpty(_options.Prompt) && string.IsNullOrEmpty(_options.ExpectPrompt))
                {
                    LogWithTimestamp("WARNING: No prompt pattern or expect prompt defined. Shell commands may not work correctly!", true);
                    LogWithTimestamp("Consider setting a prompt pattern for proper command handling.", true);
                }
            }
        }

        private void CreateShellStream()
        {
            LogWithTimestamp("Creating shell stream");
            
            if (_shellStream != null)
            {
                LogWithTimestamp("Shell stream already exists, reusing");
                return; // Stream already exists
            }

            _shellStream = _sshClient.CreateShellStream("vt100", 80, 24, 800, 600, 1024);

            // Setup persistent event handler to capture output
            _shellStream.DataReceived += HandleShellOutput;

            // Wait for the shell to initialize properly
            LogWithTimestamp("SSHClient Message: Waiting for shell initialization (2000ms)");
            Thread.Sleep(2000);
        }

        private void HandleShellOutput(object sender, ShellDataEventArgs e)
        {
            var outputText = Encoding.UTF8.GetString(e.Data);
            
            // Append to our internal buffer for prompt detection
            _outputBuffer.Append(outputText);
            
            // Check if we see the expected prompt in the output
            if (!string.IsNullOrEmpty(_options.ExpectPrompt) && 
                _outputBuffer.ToString().Contains(_options.ExpectPrompt))
            {
                _promptDetected = true;
                LogWithTimestamp($"Detected expected prompt: '{_options.ExpectPrompt}'");
            }
            
            // Pass the output to the callback
            _options.OutputCallback(outputText);
        }

        public void Disconnect()
        {
            LogWithTimestamp("Disconnecting from device");
            
            try
            {
                if (_shellStream != null)
                {
                    _shellStream.DataReceived -= HandleShellOutput;
                    _shellStream.Dispose();
                    _shellStream = null;
                }

                if (_sshClient != null && _sshClient.IsConnected)
                {
                    _sshClient.Disconnect();
                }
                
                LogWithTimestamp("Successfully disconnected");
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"Error during disconnect: {ex.Message}", true);
            }
            finally
            {
                _sshClient?.Dispose();
                _sshClient = null;
            }
        }
        
        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
        }
        
        ~SSHClient()
        {
            Dispose();
        }

        // Method to set the expect prompt
        public void SetExpectPrompt(string promptString)
        {
            if (!string.IsNullOrEmpty(promptString))
            {
                _options.ExpectPrompt = promptString;
                LogWithTimestamp($"Expect prompt set to: '{promptString}'", true);
            }
        }



public string ExecuteCommand(string command)
{
    if (_sshClient == null || !_sshClient.IsConnected)
        throw new InvalidOperationException("SSH client is not connected");

    // Only warn if using shell mode with no prompt information
    if (_options.InvokeShell && string.IsNullOrEmpty(_options.Prompt) && 
        string.IsNullOrEmpty(_options.ExpectPrompt))
    {
        LogWithTimestamp("WARNING: Executing shell command with no prompt pattern or expect prompt defined!", true);
    }

    LogWithTimestamp($"SSHClient Message: Executing command: '{command}'", true);
    DateTime startTime = DateTime.Now;

    string result;
    if (_options.InvokeShell)
    {
        // Handle multiple comma-separated commands for shell mode
        string[] commands = command.Split(',');
        result = ExecuteShellCommands(commands);
    }
    else
    {
        result = ExecuteDirectCommand(command);
    }

    // Wait between commands if specified
    if (_options.InterCommandTime > 0)
    {
        LogWithTimestamp($"SSHClient Message: Waiting between commands: {_options.InterCommandTime}s");
        Thread.Sleep(_options.InterCommandTime * 1000);
    }
    
    TimeSpan duration = DateTime.Now - startTime;
    LogWithTimestamp($"SSHClient Message: Command execution completed in {duration.TotalMilliseconds}ms", true);
    
    return result;
}

private string ExecuteDirectCommand(string command)
{
    LogWithTimestamp("Using direct command execution mode");
    DateTime startTime = DateTime.Now;
    
    using var cmd = _sshClient.CreateCommand(command);
    cmd.CommandTimeout = TimeSpan.FromSeconds(_options.Timeout);
    
    LogWithTimestamp("Executing command via SSH client");
    var result = cmd.Execute();
    
    TimeSpan executionTime = DateTime.Now - startTime;
    LogWithTimestamp($"Command execution took {executionTime.TotalMilliseconds}ms");
    
    _options.OutputCallback(result);
    
    if (!string.IsNullOrEmpty(cmd.Error))
    {
        LogWithTimestamp($"Command produced error output: {cmd.Error}", true);
        _options.ErrorCallback(cmd.Error);
    }
    
    LogMessage(result);
    if (!string.IsNullOrEmpty(cmd.Error))
    {
        LogMessage(cmd.Error);
    }
    
    return result;
}

private string ExecuteShellCommands(string[] commands)
{
    LogWithTimestamp("Using shell mode for command execution");
    DateTime startTime = DateTime.Now;
    
    if (_shellStream == null)
    {
        // Create shell stream if not already created
        LogWithTimestamp("Shell stream not initialized, creating now");
        CreateShellStream();
    }
    
    // Clear our buffer and reset prompt detection flag
    _outputBuffer.Clear();
    _promptDetected = false;
    
    try
    {
        // Only process commands if there are meaningful commands to send
        bool hasCommands = commands.Any(cmd => !string.IsNullOrWhiteSpace(cmd));
        
        if (hasCommands)
        {
            // Process each command with appropriate timing
            for (int i = 0; i < commands.Length; i++)
            {
                string cmd = commands[i];
                
                // Skip empty commands to prevent unnecessary prompts
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    LogWithTimestamp($"Skipping empty command {i+1}/{commands.Length}");
                    continue;
                }
                
                LogWithTimestamp($"Sending command {i+1}/{commands.Length}: '{cmd}'");
                
                // Send command with newline
                _shellStream.WriteLine(cmd);
                
                // Wait between commands
                if (_options.InterCommandTime > 0 && i < commands.Length - 1)
                {
                    LogWithTimestamp($"Waiting between sub-commands: {_options.InterCommandTime}s");
                    Thread.Sleep(_options.InterCommandTime * 1000);
                }
            }

            // If an expect prompt is set, wait for it with timeout
            if (!string.IsNullOrEmpty(_options.ExpectPrompt))
            {
                LogWithTimestamp($"Waiting for expect prompt: '{_options.ExpectPrompt}'");
                int timeoutMs = _options.ExpectPromptTimeout;
                DateTime timeoutTime = DateTime.Now.AddMilliseconds(timeoutMs);
                
                // Wait until prompt is detected or timeout
                while (!_promptDetected && DateTime.Now < timeoutTime)
                {
                    Thread.Sleep(50); // Small sleep to prevent CPU spinning
                }
                
                if (_promptDetected)
                {
                    LogWithTimestamp("Expected prompt detected, command complete");
                }
                else
                {
                    LogWithTimestamp($"Timed out waiting for expect prompt after {timeoutMs}ms", true);
                }
            }
            else
            {
                // Fall back to the old timeout-based approach if no expect prompt
                LogWithTimestamp($"No expect prompt defined, waiting shell timeout: {_options.ShellTimeout}s");
                Thread.Sleep(_options.ShellTimeout * 1000);
            }
            
            LogWithTimestamp("Shell command execution completed");
        }
        else
        {
            LogWithTimestamp("No commands to execute, skipping shell command execution");
        }
    }
    catch (Exception ex)
    {
        string errorMessage = $"Error during shell execution: {ex.Message}";
        LogWithTimestamp(errorMessage, true);
        
        LogMessage(errorMessage);
        _options.ErrorCallback(errorMessage);
    }
    
    TimeSpan totalTime = DateTime.Now - startTime;
    LogWithTimestamp($"Total shell command execution time: {totalTime.TotalMilliseconds}ms");
    
    // Return the accumulated output buffer content
    return _outputBuffer.ToString();
}


        private void LogMessage(string message)
        {
            if (string.IsNullOrEmpty(_options.LogFile))
                return;

            try
            {
                // Ensure directory exists
                string logDir = Path.GetDirectoryName(_options.LogFile);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                using (StreamWriter writer = File.AppendText(_options.LogFile))
                {
                    writer.WriteLine(message);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                _options.ErrorCallback($"Error writing to log file: {ex.Message}");
            }
        }
    }
}