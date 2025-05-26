using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSHPassCSharp.Fingerprint
{
    public class DeviceProfile
    {
        public string Host { get; set; }
        public int Port { get; set; } = 22;
        public string DeviceType { get; set; }
        public string Prompt { get; set; }
        public string DisablePagingCommand { get; set; }
        public string Hostname { get; set; }
        public string Username { get; set; }
        public string Model { get; set; }
        public string Version { get; set; }
        public string SerialNumber { get; set; }
        public DateTime LastFingerprinted { get; set; }
        
        [JsonIgnore]
        public string Password { get; set; }
        
        public Dictionary<string, string> AdditionalInfo { get; set; } = new Dictionary<string, string>();
        
        // Create a profile from device info
        public static DeviceProfile FromDeviceInfo(DeviceInfo info)
        {
            return new DeviceProfile
            {
                Host = info.Host,
                Port = info.Port,
                DeviceType = info.DeviceType.ToString(),
                Prompt = info.DetectedPrompt,
                DisablePagingCommand = info.DisablePagingCommand,
                Hostname = info.Hostname,
                Username = info.Username,
                Model = info.Model,
                Version = info.Version,
                SerialNumber = info.SerialNumber,
                AdditionalInfo = info.AdditionalInfo,
                LastFingerprinted = DateTime.Now
            };
        }
    }
    
    public class DeviceProfileStore
    {
        private List<DeviceProfile> _profiles;
        private readonly string _profilesFile;
        
        public DeviceProfileStore(string profilesFile = null)
        {
            _profiles = new List<DeviceProfile>();
            _profilesFile = profilesFile ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sshfingerprint",
                "profiles.json");
        }
        
        public void LoadProfiles()
        {
            if (!File.Exists(_profilesFile))
            {
                _profiles = new List<DeviceProfile>();
                return;
            }
            
            try
            {
                string json = File.ReadAllText(_profilesFile);
                _profiles = JsonSerializer.Deserialize<List<DeviceProfile>>(json) ?? new List<DeviceProfile>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading profiles: {ex.Message}");
                _profiles = new List<DeviceProfile>();
            }
        }
        
        public void SaveProfiles()
        {
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(_profilesFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                
                string json = JsonSerializer.Serialize(_profiles, options);
                File.WriteAllText(_profilesFile, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error saving profiles: {ex.Message}");
            }
        }
        
        public void AddOrUpdateProfile(DeviceProfile profile)
        {
            // Remove any existing profile for this host:port
            _profiles.RemoveAll(p => p.Host == profile.Host && p.Port == profile.Port);
            
            // Add the new profile
            _profiles.Add(profile);
            
            // Save changes
            SaveProfiles();
        }
        
        public DeviceProfile GetProfile(string host, int port = 22)
        {
            return _profiles.FirstOrDefault(p => p.Host == host && p.Port == port);
        }
        
        public List<DeviceProfile> GetAllProfiles()
        {
            return _profiles;
        }
        
        public List<DeviceProfile> GetProfilesByDeviceType(string deviceType)
        {
            return _profiles.Where(p => p.DeviceType == deviceType).ToList();
        }
        
        public bool RemoveProfile(string host, int port = 22)
        {
            int count = _profiles.RemoveAll(p => p.Host == host && p.Port == port);
            if (count > 0)
            {
                SaveProfiles();
                return true;
            }
            return false;
        }
    }
}