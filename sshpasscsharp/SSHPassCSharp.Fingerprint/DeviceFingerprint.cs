using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO;
using System.Reflection;
using SSHPassCSharp.Core;

namespace SSHPassCSharp.Fingerprint
{
    public class DeviceFingerprint : IDisposable
    {
        private readonly SSHClient _sshClient;
        private readonly StringBuilder _outputBuffer = new StringBuilder();
        private readonly DeviceInfo _deviceInfo = new DeviceInfo();
        private readonly int _connectionTimeout;
        private bool _isConnected = false;
        private bool _pagingDisabled = false;
        private readonly bool _verbose;
        private readonly bool _debug;
        
        // Common prompt patterns across different device types
        private static readonly string[] PromptPatterns = new[]
        {
            @"[-\w]+[#>$](\s*)$",           // Most common pattern: hostname followed by #, >, or $
            @"[-\w]+[@][-\w]+[#>$:](\s*)$", // username@hostname format
            @"\([-\w]+\)[#>$](\s*)$",       // (hostname) format - some devices
            @"[#>$](\s*)$",                 // Just the prompt character
            @"[-\w]+(?::|>|\]|\))\s*$"      // hostname: or hostname> or hostname] formats
        };
        

public DeviceFingerprint(string host, int port, string username, string password, 
    Action<string> outputCallback = null, bool debug = false, bool verbose = false, int connectionTimeout = 5000)
{
    _deviceInfo.Host = host;
    _deviceInfo.Port = port;
    _deviceInfo.Username = username;
    _connectionTimeout = connectionTimeout;
    _verbose = verbose;
    _debug = debug;
    
    // Configure SSH client for fingerprinting with broader compatibility
    var sshOptions = new SSHClient.SSHClientOptions
    {
        Host = host,
        Port = port,
        Username = username,
        Password = password,
        InvokeShell = true,
        // Start with a very broad prompt pattern that will match most devices
        Prompt = "[#>$\\]\\):]",  // Will match #, >, $, ], ), or : at end of line
        ExpectPrompt = null,     // We'll set this after detecting the actual prompt
        PromptCount = 1,
        ShellTimeout = 2,        // Reduced timeout
        InterCommandTime = 0,    // No wait between commands
        ExpectPromptTimeout = 5000, // 5 second timeout for expect prompt
        Debug = debug,
        RequireHyphenInPrompt = false
    };
    
    // Set up output capture
    Action<string> bufferCallback = (output) => {
        _outputBuffer.Append(output);
    };
    
    if (outputCallback != null)
    {
        sshOptions.OutputCallback = (output) => {
            outputCallback(output);
            bufferCallback(output);
        };
    }
    else
    {
        sshOptions.OutputCallback = bufferCallback;
    }
    
    _sshClient = new SSHClient(sshOptions);
}


        // Execute command safely with timeout and retry logic
 // Execute command safely with timeout and retry logic


// Improved command execution that handles buffer position tracking
private string SafeExecuteCommand(string command, int timeoutMs = 3000, int retries = 1)
{
    for (int attempt = 0; attempt <= retries; attempt++)
    {
        try
        {
            // Record the current buffer length to track only new output
            int startPosition = _outputBuffer.Length;
            
            Console.WriteLine($"Executing command (attempt {attempt+1}/{retries+1}): '{command}'");
            Console.WriteLine($"Buffer position before command: {startPosition}");
            
            // Execute the command
            _sshClient.ExecuteCommand(command);
            
            // Wait for initial response
            Thread.Sleep(300);
            
            // Get current position
            int currentPosition = _outputBuffer.Length;
            Console.WriteLine($"Buffer position after initial wait: {currentPosition}");
            
            // Only wait longer if we need to - up to max timeout
            DateTime startTime = DateTime.Now;
            DateTime endTime = startTime.AddMilliseconds(timeoutMs);
            
            // We'll consider the command complete when:
            // 1. We've waited at least 500ms (to avoid premature exit)
            // 2. The buffer hasn't changed for 300ms OR
            // 3. We see the prompt at the end of the output OR
            // 4. We exceed the maximum timeout
            
            int lastKnownLength = currentPosition;
            DateTime lastChangeTime = DateTime.Now;
            
            while (DateTime.Now < endTime)
            {
                // Check if buffer has changed
                currentPosition = _outputBuffer.Length;
                
                if (currentPosition > lastKnownLength)
                {
                    // Buffer has grown, update last change time
                    lastKnownLength = currentPosition;
                    lastChangeTime = DateTime.Now;
                }
                else if ((DateTime.Now - lastChangeTime).TotalMilliseconds > 300 && 
                         (DateTime.Now - startTime).TotalMilliseconds > 500)
                {
                    // Buffer hasn't changed for 300ms and we've waited at least 500ms total
                    Console.WriteLine("Command appears complete (no buffer change)");
                    break;
                }
                
                // Check if output ends with the prompt (if we know it)
                if (!string.IsNullOrEmpty(_deviceInfo.DetectedPrompt))
                {
                    string currentOutput = _outputBuffer.ToString();
                    if (currentOutput.TrimEnd().EndsWith(_deviceInfo.DetectedPrompt))
                    {
                        Console.WriteLine("Command appears complete (prompt detected)");
                        break;
                    }
                }
                
                // Short sleep to prevent CPU spinning
                Thread.Sleep(50);
            }
            
            // Extract only the new output
            string result = "";
            if (_outputBuffer.Length > startPosition)
            {
                result = _outputBuffer.ToString().Substring(startPosition);
            }
            
            Console.WriteLine($"Command complete, received {result.Length} bytes of output");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing command: {ex.Message}");
            
            // If we've reached the maximum number of retries, return the error
            if (attempt == retries)
                return $"ERROR: {ex.Message}";
                
            // Otherwise wait and try again
            Thread.Sleep(1000);
            
            // Try to recover if this is a channel issue
            if (ex.Message.Contains("channel") && attempt < retries)
            {
                Console.WriteLine("Detected channel issue, attempting to reconnect...");
                try
                {
                    _sshClient.Disconnect();
                    Thread.Sleep(1000);
                    _sshClient.Connect();
                }
                catch (Exception reconnectEx)
                {
                    Console.WriteLine($"Reconnection attempt failed: {reconnectEx.Message}");
                }
            }
        }
    }
    
    return "ERROR: Max retries exceeded";
}
// Simple prompt detection that just sends a newline and captures the response
// In DeviceFingerprint.cs, modify the DetectPrompt() method

// Improved prompt detection that handles buffer accumulation

private string DetectPrompt()
{
    Console.WriteLine("Starting improved prompt detection...");
    
    // First, check the current content of the buffer
    string currentBuffer = _outputBuffer.ToString();
    Console.WriteLine($"Current buffer length: {currentBuffer.Length} bytes");
    
    // Look at the last few lines of the existing buffer for a prompt
    string[] existingLines = currentBuffer.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    
    if (existingLines.Length > 0)
    {
        // Get the last line which is likely to be a prompt
        string lastLine = existingLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        Console.WriteLine($"Last line from existing buffer: '{lastLine}'");
        
        // Check if this looks like a valid prompt
        if (!string.IsNullOrEmpty(lastLine) && 
            (lastLine.EndsWith("#") || lastLine.EndsWith(">") || lastLine.EndsWith("$") || 
             lastLine.EndsWith(":") || lastLine.EndsWith("]") || lastLine.EndsWith(")")))
        {
            Console.WriteLine($"Detected prompt from existing buffer: '{lastLine}'");
            
            // Set the expect prompt on the SSH client if possible
            if (_sshClient is SSHClient client)
            {
                try {
                    client.SetExpectPrompt(lastLine);
                    Console.WriteLine($"Set expect prompt on SSH client to: '{lastLine}'");
                } catch (Exception ex) {
                    Console.WriteLine($"Error setting expect prompt: {ex.Message}");
                }
            }
            
            return lastLine;
        }
    }
    
    // If we don't have a valid prompt from existing buffer, try sending a newline
    Console.WriteLine("No valid prompt found in buffer, sending newline...");
    
    // Mark the current length so we can extract only new content
    int previousLength = _outputBuffer.Length;
    
    // Send a newline and wait for response
    _sshClient.ExecuteCommand("\n");
    
    // Give it time to receive the response
    Thread.Sleep(1000);
    
    // Get only the new content received after our command
    string newContent = "";
    if (_outputBuffer.Length > previousLength)
    {
        newContent = _outputBuffer.ToString().Substring(previousLength);
        Console.WriteLine($"New content after newline ({newContent.Length} bytes): '{newContent}'");
    }
    else
    {
        Console.WriteLine("No new content received after newline command");
    }
    
    // Parse the new content for a prompt
    string[] newLines = newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    string promptLine = newLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
    
    if (!string.IsNullOrEmpty(promptLine))
    {
        Console.WriteLine($"Detected prompt after newline: '{promptLine}'");
        
        // Set the expect prompt on the SSH client if possible
        if (_sshClient is SSHClient client)
        {
            try {
                client.SetExpectPrompt(promptLine);
                Console.WriteLine($"Set expect prompt on SSH client to: '{promptLine}'");
            } catch (Exception ex) {
                Console.WriteLine($"Error setting expect prompt: {ex.Message}");
            }
        }
        
        return promptLine;
    }
    
    // If still not successful, try a different approach - send a harmless command
    Console.WriteLine("Trying with a harmless command...");
    previousLength = _outputBuffer.Length;
    
    // Send a harmless command that works on most devices
    _sshClient.ExecuteCommand("?");
    
    // Give it time to receive the response
    Thread.Sleep(1000);
    
    if (_outputBuffer.Length > previousLength)
    {
        newContent = _outputBuffer.ToString().Substring(previousLength);
        Console.WriteLine($"New content after ? command ({newContent.Length} bytes): '{newContent}'");
        
        // Get the last line which should include the prompt
        newLines = newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        promptLine = newLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        
        if (!string.IsNullOrEmpty(promptLine))
        {
            Console.WriteLine($"Detected prompt after ? command: '{promptLine}'");
            
            // Set the expect prompt on the SSH client if possible
            if (_sshClient is SSHClient client)
            {
                try {
                    client.SetExpectPrompt(promptLine);
                    Console.WriteLine($"Set expect prompt on SSH client to: '{promptLine}'");
                } catch (Exception ex) {
                    Console.WriteLine($"Error setting expect prompt: {ex.Message}");
                }
            }
            
            return promptLine;
        }
    }
    
    // Fall back to a default pattern
    Console.WriteLine("Failed to detect prompt through all methods, using default pattern");
    return "[#>$]";
}

// Modified method to update the client with the detected prompt
           
        // Enhanced vendor identification from command output
        private DeviceType IdentifyVendorFromOutput(string output)
        {
            string lowerOutput = output.ToLower();
            
            // Identify based on explicit vendor/OS mentions
            // Cisco product family
            if (lowerOutput.Contains("cisco ios") || lowerOutput.Contains("cisco internetwork operating system"))
                return DeviceType.CiscoIOS;
            
            if (lowerOutput.Contains("ios-xe"))
                return DeviceType.CiscoIOS;
                
            if (lowerOutput.Contains("nx-os") || lowerOutput.Contains("nexus"))
                return DeviceType.CiscoNXOS;
                
            if (lowerOutput.Contains("adaptive security appliance") || lowerOutput.Contains("asa"))
                return DeviceType.CiscoASA;
            
            // Arista
            if (lowerOutput.Contains("arista") || (lowerOutput.Contains("eos") && !lowerOutput.Contains("cisco")))
                return DeviceType.AristaEOS;
            
            // Juniper
            if (lowerOutput.Contains("junos") || lowerOutput.Contains("juniper"))
                return DeviceType.JuniperJunOS;
            
            // HPE/Aruba products    
            if ((lowerOutput.Contains("hp") || lowerOutput.Contains("hewlett-packard")) && 
                lowerOutput.Contains("procurve"))
                return DeviceType.HPProCurve;
                
            if (lowerOutput.Contains("aruba"))
                return DeviceType.HPProCurve; // Use HPProCurve for Aruba switches
                
            // Fortinet    
            if (lowerOutput.Contains("fortigate") || lowerOutput.Contains("fortios"))
                return DeviceType.FortiOS;
                
            // Palo Alto    
            if (lowerOutput.Contains("pan-os") || lowerOutput.Contains("palo alto"))
                return DeviceType.PaloAltoOS;
            
            // Generic OS types
            if (lowerOutput.Contains("linux") || lowerOutput.Contains("ubuntu") || 
                lowerOutput.Contains("centos") || lowerOutput.Contains("debian") || 
                lowerOutput.Contains("redhat") || lowerOutput.Contains("fedora"))
                return DeviceType.Linux;
                
            if (lowerOutput.Contains("freebsd"))
                return DeviceType.FreeBSD;
                
            if (lowerOutput.Contains("windows") || lowerOutput.Contains("microsoft"))
                return DeviceType.Windows;
            
            // Identify based on product model mentions that imply vendor
            if (Regex.IsMatch(lowerOutput, @"\bws-c\d{4}\b") || // Catalyst switches
               Regex.IsMatch(lowerOutput, @"\bc\d{4}\b"))       // Cisco router models
                return DeviceType.CiscoIOS;
                
            if (Regex.IsMatch(lowerOutput, @"\bn\d{4}\b") ||    // Nexus models 
               lowerOutput.Contains("nexus"))
                return DeviceType.CiscoNXOS;
                
            if (Regex.IsMatch(lowerOutput, @"\bdgs-\d{4}\b"))   // D-Link switches
                return DeviceType.Unknown; // Not in the enum, categorize as Unknown
            
            return DeviceType.Unknown;
        }
        
        // Extract more detailed information from the output
        private void ExtractDeviceDetails()
        {
            string output = _outputBuffer.ToString();
            
            // Extract hostname
            var hostnamePatterns = new Dictionary<DeviceType, string> 
            {
                { DeviceType.CiscoIOS, @"hostname\s+([^\s\r\n]+)" },
                { DeviceType.CiscoNXOS, @"hostname\s+([^\s\r\n]+)" },
                { DeviceType.CiscoASA, @"hostname\s+([^\s\r\n]+)" },
                { DeviceType.AristaEOS, @"hostname\s+([^\s\r\n]+)" },
                { DeviceType.JuniperJunOS, @"host-name\s+([^\s\r\n;]+)" },
                { DeviceType.Linux, @"Hostname:[^\n]*(\S+)[\r\n]" },
                { DeviceType.GenericUnix, @"([A-Za-z0-9\-]+)[@][^:]+:" }
            };
            
            if (hostnamePatterns.TryGetValue(_deviceInfo.DeviceType, out string pattern))
            {
                var match = Regex.Match(output, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    _deviceInfo.Hostname = match.Groups[1].Value;
                }
            }
            
            // If we couldn't extract a hostname, use the prompt as a fallback
            if (string.IsNullOrEmpty(_deviceInfo.Hostname) && !string.IsNullOrEmpty(_deviceInfo.DetectedPrompt))
            {
                // Extract hostname from prompt (typical format username@hostname or hostname#)
        var promptHostnameMatch = Regex.Match(
                _deviceInfo.DetectedPrompt, 
                @"^([A-Za-z0-9\-._]+)(?:[>#]|$)"
            );
                if (promptHostnameMatch.Success && promptHostnameMatch.Groups.Count > 1)
                {
                    _deviceInfo.Hostname = promptHostnameMatch.Groups[1].Value;
                }
            }
            
            // Extract serial number - common pattern across many devices
            var serialMatch = Regex.Match(output, @"[Ss]erial\s*[Nn]umber\s*:?\s*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
            if (serialMatch.Success && serialMatch.Groups.Count > 1)
            {
                _deviceInfo.SerialNumber = serialMatch.Groups[1].Value.Trim();
            }
            
            // Extract more details based on device type
            switch (_deviceInfo.DeviceType)
            {
                case DeviceType.CiscoIOS:
                    // Extract version from "show version" output
                    var versionMatch = Regex.Match(output, @"(?:IOS|Software).+?Version\s+([^,\s\r\n]+)", RegexOptions.IgnoreCase);
                    if (versionMatch.Success && versionMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Version = versionMatch.Groups[1].Value.Trim();
                    }
                    
                    // Extract model information
                    var modelMatch = Regex.Match(output, @"[Cc]isco\s+([A-Za-z0-9\-]+)(?:\s+[^\n]*?)(?:processor|chassis|router|switch)", RegexOptions.Singleline);
                    if (modelMatch.Success && modelMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Model = modelMatch.Groups[1].Value.Trim();
                    }
                    break;
                
                case DeviceType.CiscoNXOS:
                    // Extract version for NX-OS
                    var nxosVersionMatch = Regex.Match(output, @"NXOS:\s+version\s+([^,\s\r\n]+)", RegexOptions.IgnoreCase);
                    if (nxosVersionMatch.Success && nxosVersionMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Version = nxosVersionMatch.Groups[1].Value.Trim();
                    }
                    
                    // Extract model for Nexus
                    var nxosModelMatch = Regex.Match(output, @"cisco\s+Nexus\s+([^\s]+)", RegexOptions.IgnoreCase);
                    if (nxosModelMatch.Success && nxosModelMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Model = "Nexus " + nxosModelMatch.Groups[1].Value.Trim();
                    }
                    break;
                
                case DeviceType.AristaEOS:
                    // Extract version for Arista EOS
                    var aristaVersionMatch = Regex.Match(output, @"EOS\s+version\s+([^,\s\r\n]+)", RegexOptions.IgnoreCase);
                    if (aristaVersionMatch.Success && aristaVersionMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Version = aristaVersionMatch.Groups[1].Value.Trim();
                    }
                    
                    // Extract model for Arista switches
                    var aristaModelMatch = Regex.Match(output, @"Arista\s+([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (aristaModelMatch.Success && aristaModelMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Model = aristaModelMatch.Groups[1].Value.Trim();
                    }
                    break;
                    
                case DeviceType.JuniperJunOS:
                    // Extract version for JunOS
                    var junosVersionMatch = Regex.Match(output, @"JUNOS\s+([^,\s\r\n\]]+)", RegexOptions.IgnoreCase);
                    if (junosVersionMatch.Success && junosVersionMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Version = junosVersionMatch.Groups[1].Value.Trim();
                    }
                    
                    // Extract model for Juniper
                    var junosModelMatch = Regex.Match(output, @"Model:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (junosModelMatch.Success && junosModelMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Model = junosModelMatch.Groups[1].Value.Trim();
                    }
                    break;
                
                case DeviceType.Linux:
                    // Extract Linux distribution and version
                    var linuxVersionMatch = Regex.Match(output, @"PRETTY_NAME=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (linuxVersionMatch.Success && linuxVersionMatch.Groups.Count > 1)
                    {
                        _deviceInfo.Version = linuxVersionMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        // Try uname output
                        var unameMatch = Regex.Match(output, @"Linux\s+\S+\s+([^\s]+)");
                        if (unameMatch.Success && unameMatch.Groups.Count > 1)
                        {
                            _deviceInfo.Version = unameMatch.Groups[1].Value.Trim();
                        }
                    }
                    break;
                    
                // Add other device type specific extraction logic as needed
            }
        }
        
public DeviceInfo Fingerprint(bool verbose = false)
{
    try
    {
        LogMessage("Starting fingerprinting process...");
        
        if (_isConnected)
        {
            LogDebug("Disconnecting existing connection before starting fingerprint");
            Disconnect();
        }
        
        // Connect - no more Task.Wait()
        try
        {
            LogDebug($"Connecting to {_deviceInfo.Host}:{_deviceInfo.Port}...");
            _sshClient.Connect();
            
            _isConnected = true;
            LogDebug("Successfully connected!");
            
            // Wait a moment to ensure connection is fully established
            Thread.Sleep(500);
            LogMessage("Detecting prompt...");
        string foundPrompt = DetectPrompt();
        _deviceInfo.DetectedPrompt = foundPrompt;
        LogMessage($"Detected prompt: '{foundPrompt}'");
        }
        catch (Exception ex)
        {
            LogMessage($"Connection error: {ex.Message}");
            throw;
        }
        // Step 1: Detect prompt using our enhanced prompt detection
        LogMessage("Detecting prompt...");
        string detectedPrompt = DetectPrompt();
        _deviceInfo.DetectedPrompt = detectedPrompt;
        LogMessage($"Detected prompt: '{detectedPrompt}'");
        

        
        // Step 2: Try to disable paging with common commands
        LogMessage("Attempting to disable paging...");
        
        // List of common paging disable commands for various vendors
        // Try faster commands first, multi-step commands last
        string[] disablePagingCommands = new[]
        {
            "terminal length 0",         // Cisco IOS, Arista EOS
            "terminal pager 0",          // Cisco ASA
            "set cli screen-length 0",   // Juniper
            "no page",                   // HP ProCurve
            "set cli pager off",         // Palo Alto
            "stty rows 1000",            // Unix/Linux
            "export TERM=xterm; stty rows 1000", // More complete Unix/Linux
            "config system console\nset output standard\nend" // FortiOS (multi-line)
        };
        
        foreach (var command in disablePagingCommands)
        {
            LogDebug($"Trying paging command: {command}");
            
            string result = SafeExecuteCommand(command, 100);
            
            // Check for explicit error responses
            bool hasError = result.ToLower().Contains("error") || 
                            result.ToLower().Contains("invalid") || 
                            result.ToLower().Contains("unknown") ||
                            result.ToLower().Contains("% ") ||   // Cisco IOS error marker
                            result.ToLower().Contains("syntax error");
            
            if (!hasError)
            {
                LogDebug($"Paging command '{command}' appears successful");
                _deviceInfo.DisablePagingCommand = command;
                _pagingDisabled = true;
                break;
            }
        }
        
        // Give a short break after disabling paging
        Thread.Sleep(500);
        
        // Step 3: Issue version/info commands to determine device type
        LogMessage("Running device identification commands...");
        
        // Try common version commands, starting with "show version"
        string[] versionCommands = new[]
        {
            "show version",              // Works on many network devices (Cisco, Arista, Juniper)
            "show system info",          // Works on some devices
            "show system information",   // HP/Aruba
            "get system status",         // FortiGate
            "display version",           // Huawei
            "uname -a"                   // Unix/Linux
        };
        
        bool deviceIdentified = false;
        
        // Try each command with a pause between attempts
        foreach (var command in versionCommands)
        {
            LogDebug($"Trying identification command: {command}");
            
            string output = SafeExecuteCommand(command, 5000);
            _deviceInfo.CommandOutputs[command] = output;
            
            // If we got substantial output, try to identify the device
            if (output.Length > 50 && !output.ToLower().Contains("error") && 
                !output.ToLower().Contains("invalid") && !output.ToLower().Contains("% "))
            {
                DeviceType detectedType = IdentifyVendorFromOutput(output);
                
                if (detectedType != DeviceType.Unknown)
                {
                    _deviceInfo.DeviceType = detectedType;
                    deviceIdentified = true;
                    LogMessage($"Identified device as: {detectedType}");
                    break;
                }
            }
            
            // Give a short break between commands to avoid overwhelming the device
            Thread.Sleep(1000);
        }
        
        // If we couldn't identify from version commands, check prompt patterns
        if (!deviceIdentified)
        {
            LogDebug("Device not identified from commands, guessing based on prompt");
            
            if (detectedPrompt.EndsWith("#"))
            {
                // Network devices with # are commonly Cisco IOS in privileged mode
                _deviceInfo.DeviceType = DeviceType.CiscoIOS;
                LogDebug("Assuming Cisco IOS based on # prompt");
            }
            else if (detectedPrompt.EndsWith(">"))
            {
                // Network devices with > are commonly Cisco IOS in user mode
                _deviceInfo.DeviceType = DeviceType.CiscoIOS;
                LogDebug("Assuming Cisco IOS based on > prompt");
            }
            else if (detectedPrompt.Contains("@") && (detectedPrompt.EndsWith("$") || detectedPrompt.EndsWith(":")))
            {
                // Linux/Unix pattern username@hostname$ or username@hostname:
                _deviceInfo.DeviceType = DeviceType.Linux;
                LogDebug("Assuming Linux based on username@hostname prompt");
            }
        }
        
        // Step 4: Get the vendor-specific disable paging command now that we know the device type
        if (_deviceInfo.DeviceType != DeviceType.Unknown && string.IsNullOrEmpty(_deviceInfo.DisablePagingCommand))
        {
            string vendorPagingCommand = _deviceInfo.DeviceType.GetDisablePagingCommand();
            
            if (!string.IsNullOrEmpty(vendorPagingCommand))
            {
                LogDebug($"Using vendor-specific paging command: {vendorPagingCommand}");
                
                string result = SafeExecuteCommand(vendorPagingCommand, 3000);
                
                // If no explicit error, assume it worked
                bool hasError = result.ToLower().Contains("error") || 
                                result.ToLower().Contains("invalid") || 
                                result.ToLower().Contains("unknown") ||
                                result.ToLower().Contains("% ");
                                
                if (!hasError)
                {
                    LogDebug("Vendor-specific paging command succeeded");
                    _deviceInfo.DisablePagingCommand = vendorPagingCommand;
                    _pagingDisabled = true;
                }
            }
        }
        
        // Step 5: Run specific identification commands for the detected device type
        if (_deviceInfo.DeviceType != DeviceType.Unknown)
        {
            string[] deviceSpecificCommands = _deviceInfo.DeviceType.GetIdentificationCommands();
            
            foreach (var command in deviceSpecificCommands)
            {
                // Skip commands we've already run
                if (_deviceInfo.CommandOutputs.ContainsKey(command))
                    continue;
                    
                LogDebug($"Running device-specific command: {command}");
                
                string output = SafeExecuteCommand(command, 5000);
                _deviceInfo.CommandOutputs[command] = output;
                
                // Give a short break between commands
                Thread.Sleep(1000);
            }
        }
        
        // Step 6: Extract detailed device information
        LogMessage("Extracting device details...");
        ExtractDeviceDetails();
        
        // Step 7: Validate the information we collected
        ValidateDeviceInfo();
        
        // Save raw output
        _deviceInfo.RawOutput = _outputBuffer.ToString();
        
        LogMessage("Fingerprinting complete!");
        
        return _deviceInfo;
    }
    catch (Exception ex)
    {
        LogMessage($"Error during fingerprinting: {ex.Message}");
        
        if (_debug && ex.InnerException != null)
        {
            LogDebug($"Inner exception: {ex.InnerException.Message}");
        }
        
        _deviceInfo.CommandOutputs["error"] = ex.ToString();
        return _deviceInfo;
    }
    finally
    {
        // Don't disconnect here - keep the connection open for future commands
        // The caller is responsible for calling Disconnect() when done
    }
}
// Validate and fill in missing details where possible
private void ValidateDeviceInfo()
{
    // If device type is still unknown but we have substantial output, try harder
    if (_deviceInfo.DeviceType == DeviceType.Unknown && _outputBuffer.Length > 100)
    {
        string fullOutput = _outputBuffer.ToString().ToLower();
        
        // Look for additional device type indicators in the full output
        if (fullOutput.Contains("cisco"))
        {
            if (fullOutput.Contains("nexus") || fullOutput.Contains("nx-os"))
                _deviceInfo.DeviceType = DeviceType.CiscoNXOS;
            else if (fullOutput.Contains("asa"))
                _deviceInfo.DeviceType = DeviceType.CiscoASA;
            else
                _deviceInfo.DeviceType = DeviceType.CiscoIOS; // Default Cisco
        }
        else if (fullOutput.Contains("juniper") || fullOutput.Contains("junos"))
        {
            _deviceInfo.DeviceType = DeviceType.JuniperJunOS;
        }
        else if (fullOutput.Contains("arista"))
        {
            _deviceInfo.DeviceType = DeviceType.AristaEOS;
        }
        else if (fullOutput.Contains("fortinet") || fullOutput.Contains("fortigate"))
        {
            _deviceInfo.DeviceType = DeviceType.FortiOS;
        }
        else if (fullOutput.Contains("palo alto"))
        {
            _deviceInfo.DeviceType = DeviceType.PaloAltoOS;
        }
        else if (fullOutput.Contains("linux") || fullOutput.Contains("ubuntu") || 
                fullOutput.Contains("centos") || fullOutput.Contains("fedora"))
        {
            _deviceInfo.DeviceType = DeviceType.Linux;
        }
        else if (fullOutput.Contains("freebsd"))
        {
            _deviceInfo.DeviceType = DeviceType.FreeBSD;
        }
        else if (fullOutput.Contains("windows") || fullOutput.Contains("microsoft"))
        {
            _deviceInfo.DeviceType = DeviceType.Windows;
        }
    }
    
    // If we still don't have a hostname but have a prompt, try harder to extract it
    if (string.IsNullOrEmpty(_deviceInfo.Hostname) && !string.IsNullOrEmpty(_deviceInfo.DetectedPrompt))
    {
        // Various prompt patterns with hostname capture
        var promptPatterns = new[]
        {
            @"^([a-zA-Z0-9\-\_\.]+)[>#\$]", // hostname followed by prompt character
            @"([a-zA-Z0-9\-\_\.]+)(?:\(.*\))?[>#\$]", // hostname possibly with context
            @".*@([a-zA-Z0-9\-\_\.]+):", // username@hostname pattern
        };
        
        foreach (var pattern in promptPatterns)
        {
            var match = Regex.Match(_deviceInfo.DetectedPrompt, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                _deviceInfo.Hostname = match.Groups[1].Value;
                break;
            }
        }
    }
}

// Method to execute a command on the existing connection
public string ExecuteCommand(string command, int timeout = 5000)
{
    if (!_isConnected)
    {
        throw new InvalidOperationException("Not connected to device. Run Fingerprint() first.");
    }
    
    return SafeExecuteCommand(command, timeout);
}

// Execute multiple commands in sequence
public Dictionary<string, string> ExecuteCommands(string[] commands, int timeout = 5000)
{
    if (!_isConnected)
    {
        throw new InvalidOperationException("Not connected to device. Run Fingerprint() first.");
    }
    
    var results = new Dictionary<string, string>();
    foreach (var command in commands)
    {
        results[command] = SafeExecuteCommand(command, timeout);
    }
    
    return results;
}

// Execute a command that might have multiple pages of output
public string ExecutePaginatedCommand(string command, int timeout = 10000, string morePrompt = "--More--")
{
    if (!_isConnected)
    {
        throw new InvalidOperationException("Not connected to device. Run Fingerprint() first.");
    }
    
    // If paging is already disabled, just run the command normally
    if (_pagingDisabled)
    {
        return SafeExecuteCommand(command, timeout);
    }
    
    // Otherwise use a specialized approach for pagination
    var output = new StringBuilder();
    
    // Clear buffer
    _outputBuffer.Clear();
    
    // Send the initial command - no more Task.Wait()
    _sshClient.ExecuteCommand(command);
    
    // Wait to ensure we get output
    Thread.Sleep(1000);
    
    // Initial capture
    output.Append(_outputBuffer.ToString());
    
    // Handle pagination
    int pageCount = 0;
    int maxPages = 100; // Safety limit
    
    while (output.ToString().Contains(morePrompt) && pageCount < maxPages)
    {
        pageCount++;
        LogDebug($"Handling pagination page {pageCount}");
        
        // Clear buffer for next page
        _outputBuffer.Clear();
        
        // Send space to get next page - no more Task.Wait()
        _sshClient.ExecuteCommand(" ");
        
        // Wait to ensure we get output
        Thread.Sleep(1000);
        
        // Get new output
        output.Append(_outputBuffer.ToString());
    }
    
    // Return combined output
    return output.ToString();
}
// Disconnect from device
public void Disconnect()
{
    if (_isConnected)
    {
        try
        {
            LogDebug("Disconnecting from device");
            _sshClient.Disconnect();
            _isConnected = false;
        }
        catch (Exception ex)
        {
            LogDebug($"Error disconnecting: {ex.Message}");
        }
    }
}

// Check if connected
public bool IsConnected => _isConnected;

// Helper method for Debug messages
private void LogDebug(string message)
{
    if (_debug)
    {
        Console.WriteLine($"[DEBUG] {message}");
    }
}

// Helper method for standard messages
private void LogMessage(string message)
{
    if (_verbose || _debug)
    {
        Console.WriteLine(message);
    }
}

public void Dispose()
{
    try
    {
        Disconnect();
        _sshClient.Dispose();
    }
    catch (Exception ex)
    {
        LogDebug($"Error during disposal: {ex.Message}");
    }
}
    }
}