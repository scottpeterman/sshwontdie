using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace SSHPassCSharp.Fingerprint
{
    public static class DeviceFingerprintUtils
    {
        // Common commands mapped by device type for common tasks
        private static readonly Dictionary<DeviceType, Dictionary<string, string>> CommandMap = 
            new Dictionary<DeviceType, Dictionary<string, string>>
        {
            // Cisco IOS commands
            { DeviceType.CiscoIOS, new Dictionary<string, string> 
                {
                    { "interfaces", "show ip interface brief" },
                    { "routes", "show ip route" },
                    { "config", "show running-config" },
                    { "neighbors", "show cdp neighbors detail" },
                    { "inventory", "show inventory" },
                    { "version", "show version" }
                }
            },
            
            // Cisco NXOS commands
            { DeviceType.CiscoNXOS, new Dictionary<string, string> 
                {
                    { "interfaces", "show ip interface brief" },
                    { "routes", "show ip route" },
                    { "config", "show running-config" },
                    { "neighbors", "show cdp neighbors detail" },
                    { "inventory", "show inventory" },
                    { "version", "show version" }
                }
            },
            
            // Cisco ASA commands
            { DeviceType.CiscoASA, new Dictionary<string, string> 
                {
                    { "interfaces", "show interface ip brief" },
                    { "routes", "show route" },
                    { "config", "show running-config" },
                    { "version", "show version" }
                }
            },
            
            // Juniper JunOS commands
            { DeviceType.JuniperJunOS, new Dictionary<string, string> 
                {
                    { "interfaces", "show interfaces terse" },
                    { "routes", "show route" },
                    { "config", "show configuration" },
                    { "neighbors", "show lldp neighbors" },
                    { "inventory", "show chassis hardware" },
                    { "version", "show version" }
                }
            },
            
            // Arista EOS commands
            { DeviceType.AristaEOS, new Dictionary<string, string> 
                {
                    { "interfaces", "show ip interface brief" },
                    { "routes", "show ip route" },
                    { "config", "show running-config" },
                    { "neighbors", "show lldp neighbors" },
                    { "inventory", "show inventory" },
                    { "version", "show version" }
                }
            },
            
            // FortiOS commands
            { DeviceType.FortiOS, new Dictionary<string, string> 
                {
                    { "interfaces", "get system interface" },
                    { "routes", "get router info routing-table all" },
                    { "config", "show" },
                    { "version", "get system status" }
                }
            },
            
            // Linux commands
            { DeviceType.Linux, new Dictionary<string, string> 
                {
                    { "interfaces", "ip addr show" },
                    { "routes", "ip route" },
                    { "cpu", "cat /proc/cpuinfo" },
                    { "memory", "free -m" },
                    { "disk", "df -h" },
                    { "version", "cat /etc/os-release" }
                }
            }
        };
        
        // Get common command for a device type
        public static string GetCommand(this DeviceType deviceType, string commandType)
        {
            if (CommandMap.TryGetValue(deviceType, out var commands))
            {
                if (commands.TryGetValue(commandType, out var command))
                {
                    return command;
                }
            }
            
            return null; // Command not found for this device type
        }
        
        // Execute a common command based on the device type
        public static string ExecuteCommonCommand(this DeviceFingerprint fingerprinter, string commandType, int timeout = 5000)
        {
            if (!fingerprinter.IsConnected)
                throw new InvalidOperationException("Device is not connected");
                
            var deviceInfo = fingerprinter.Fingerprint(false);
            string command = deviceInfo.DeviceType.GetCommand(commandType);
            
            if (string.IsNullOrEmpty(command))
                throw new ArgumentException($"Command type '{commandType}' not available for device type {deviceInfo.DeviceType}");
                
            return fingerprinter.ExecuteCommand(command, timeout);
        }
        
        // Extract IP addresses from device output
        public static List<string> ExtractIPAddresses(string output)
        {
            var ipList = new List<string>();
            
            // Match IPv4 addresses
            var ipv4Pattern = @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            var ipv4Matches = Regex.Matches(output, ipv4Pattern);
            
            foreach (Match match in ipv4Matches)
            {
                if (!ipList.Contains(match.Value))
                {
                    ipList.Add(match.Value);
                }
            }
            
            // Match IPv6 addresses - more complex but covers most patterns
            var ipv6Pattern = @"\b(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}\b|" +
                             @"\b(?:[0-9a-fA-F]{1,4}:){1,7}:\b|" +
                             @"\b(?:[0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}\b|" +
                             @"\b(?:[0-9a-fA-F]{1,4}:){1,5}(?::[0-9a-fA-F]{1,4}){1,2}\b|" +
                             @"\b(?:[0-9a-fA-F]{1,4}:){1,4}(?::[0-9a-fA-F]{1,4}){1,3}\b|" +
                             @"\b(?:[0-9a-fA-F]{1,4}:){1,3}(?::[0-9a-fA-F]{1,4}){1,4}\b|" +
                             @"\b(?:[0-9a-fA-F]{1,4}:){1,2}(?::[0-9a-fA-F]{1,4}){1,5}\b|" +
                             @"\b[0-9a-fA-F]{1,4}:(?::[0-9a-fA-F]{1,4}){1,6}\b|" +
                             @"\b:(?::[0-9a-fA-F]{1,4}){1,7}\b|" +
                             @"\b::(?:[0-9a-fA-F]{1,4}:){0,5}(?:[0-9a-fA-F]{1,4})?(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
                             
            var ipv6Matches = Regex.Matches(output, ipv6Pattern);
            
            foreach (Match match in ipv6Matches)
            {
                if (!ipList.Contains(match.Value))
                {
                    ipList.Add(match.Value);
                }
            }
            
            return ipList;
        }
        
        // Enhanced fingerprinting that also collects additional information (synchronous version)
        public static DeviceInfo EnhancedFingerprint(
            string host, int port, string username, string password, 
            bool collectInterfaces = true,
            bool collectIPAddresses = true,
            Action<string> outputCallback = null,
            bool verbose = false,
            bool debug = false)
        {
            using (var fingerprinter = new DeviceFingerprint(host, port, username, password, outputCallback, debug, verbose))
            {
                // Get basic device fingerprint
                var deviceInfo = fingerprinter.Fingerprint(verbose);
                
                if (deviceInfo.DeviceType == DeviceType.Unknown)
                {
                    return deviceInfo; // Failed to fingerprint, return what we have
                }
                
                // After successful fingerprinting, gather additional information
                try
                {
                    // Collect interface information if requested
                    if (collectInterfaces)
                    {
                        string interfaceCommand = deviceInfo.DeviceType.GetCommand("interfaces");
                        if (!string.IsNullOrEmpty(interfaceCommand))
                        {
                            string interfacesOutput = fingerprinter.ExecuteCommand(interfaceCommand, 5000);
                            deviceInfo.CommandOutputs["interfaces"] = interfacesOutput;
                            
                            // TODO: Implement parsing logic for different device types to extract interface details
                            // This would be specific to each device type's output format
                        }
                    }
                    
                    // Collect IP address information
                    if (collectIPAddresses)
                    {
                        // Extract IPs from the complete output we already have
                        deviceInfo.IPAddresses = ExtractIPAddresses(deviceInfo.RawOutput);
                        
                        // If we have interface output specifically, add IPs from there too
                        if (deviceInfo.CommandOutputs.TryGetValue("interfaces", out string interfaceOutput))
                        {
                            var interfaceIPs = ExtractIPAddresses(interfaceOutput);
                            foreach (var ip in interfaceIPs)
                            {
                                if (!deviceInfo.IPAddresses.Contains(ip))
                                {
                                    deviceInfo.IPAddresses.Add(ip);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but continue - we already have the basic fingerprint
                    if (debug || verbose)
                    {
                        Console.WriteLine($"Error collecting additional information: {ex.Message}");
                    }
                }
                
                return deviceInfo;
            }
        }
        
        // Save device information to a JSON file
        public static void SaveToFile(this DeviceInfo deviceInfo, string filePath, bool prettyPrint = true)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = prettyPrint
            };
            
            // Set DefaultIgnoreCondition if running on .NET 5 or later
            // For .NET Core 3.1, we need to use DefaultIgnoreNullValues instead
            try
            {
                var ignoreConditionProperty = typeof(JsonSerializerOptions).GetProperty("DefaultIgnoreCondition");
                if (ignoreConditionProperty != null)
                {
                    // .NET 5+
                    var ignoreConditionEnum = Type.GetType("System.Text.Json.Serialization.JsonIgnoreCondition, System.Text.Json");
                    var whenWritingNullValue = Enum.Parse(ignoreConditionEnum, "WhenWritingNull");
                    ignoreConditionProperty.SetValue(options, whenWritingNullValue);
                }
                else
                {
                    // .NET Core 3.1
                    var nullValuesProperty = typeof(JsonSerializerOptions).GetProperty("IgnoreNullValues");
                    if (nullValuesProperty != null)
                    {
                        nullValuesProperty.SetValue(options, true);
                    }
                }
            }
            catch
            {
                // Fallback - just use the options as is
            }
            
                            
            string json = JsonSerializer.Serialize(deviceInfo, options);
            File.WriteAllText(filePath, json);
        }
        
        // Load device information from a JSON file
        public static DeviceInfo LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Device info file not found: {filePath}");
                
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<DeviceInfo>(json);
        }
    }
}