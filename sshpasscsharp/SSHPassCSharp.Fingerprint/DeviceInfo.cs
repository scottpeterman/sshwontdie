using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SSHPassCSharp.Fingerprint
{
    public class DeviceInfo
    {
        // Basic connection information
        public string Host { get; set; }
        public int Port { get; set; }
        
        [JsonIgnore] // Don't include credentials in JSON output
        public string Username { get; set; }
        
        // Device identification
        public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
        public string DetectedPrompt { get; set; }
        public string DisablePagingCommand { get; set; }
        
        // Device details
        public string Hostname { get; set; }
        public string Password { get; set; }

        public string Model { get; set; }
        public string Version { get; set; }
        public string SerialNumber { get; set; }
        
        // Additional properties for extended information
        public bool IsVirtualDevice { get; set; }
        public string Platform { get; set; }
        public string UpTime { get; set; }
        public Dictionary<string, string> AdditionalInfo { get; set; } = new Dictionary<string, string>();
        
        // Network information
        public Dictionary<string, string> Interfaces { get; set; } = new Dictionary<string, string>();
        public List<string> IPAddresses { get; set; } = new List<string>();
        
        // Hardware information
        public string CPUInfo { get; set; }
        public string MemoryInfo { get; set; }
        public string StorageInfo { get; set; }
        
        // Raw output and command results
        [JsonIgnore] // Optional: Exclude raw output from JSON if it's too verbose
        public string RawOutput { get; set; }
        
        public Dictionary<string, string> CommandOutputs { get; set; } = new Dictionary<string, string>();
        
        // Timestamp of when the fingerprint was created
        public DateTime FingerprintTime { get; set; } = DateTime.Now;
        
        // Success status
        public bool Success => DeviceType != DeviceType.Unknown && !string.IsNullOrEmpty(DetectedPrompt);
        
        // Get interface/IP information as a formatted string
        public string GetInterfaceSummary()
        {
            if (Interfaces.Count == 0)
                return "No interface information available";
                
            var result = new System.Text.StringBuilder();
            result.AppendLine("Interface Information:");
            
            foreach (var iface in Interfaces)
            {
                result.AppendLine($"  {iface.Key}: {iface.Value}");
            }
            
            return result.ToString();
        }
        
        // Get a summary of the device
        public string GetSummary()
        {
            var result = new System.Text.StringBuilder();
            
            result.AppendLine($"Device: {Host}:{Port}");
            result.AppendLine($"Type: {DeviceType}");
            
            if (!string.IsNullOrEmpty(Hostname))
                result.AppendLine($"Hostname: {Hostname}");
                
            if (!string.IsNullOrEmpty(Model))
                result.AppendLine($"Model: {Model}");
                
            if (!string.IsNullOrEmpty(Version))
                result.AppendLine($"Version: {Version}");
                
            if (!string.IsNullOrEmpty(SerialNumber))
                result.AppendLine($"Serial Number: {SerialNumber}");
                
            if (!string.IsNullOrEmpty(DisablePagingCommand))
                result.AppendLine($"Disable Paging Command: {DisablePagingCommand}");
                
            if (IPAddresses.Count > 0)
            {
                result.AppendLine("IP Addresses:");
                foreach (var ip in IPAddresses)
                {
                    result.AppendLine($"  {ip}");
                }
            }
            
            result.AppendLine($"Fingerprint Time: {FingerprintTime}");
            
            return result.ToString();
        }
    }
}