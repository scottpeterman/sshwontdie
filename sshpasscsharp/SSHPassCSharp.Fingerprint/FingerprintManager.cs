using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.Json;

namespace SSHPassCSharp.Fingerprint
{
    public class FingerprintManager
    {
        private DeviceProfileStore _profileStore;
        private string _profilesDirectory;

        public FingerprintManager(string profilesDirectory = null)
        {
            // If not specified, use default location in user profile
            _profilesDirectory = profilesDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sshfingerprint");
                
            // Ensure directory exists
            if (!Directory.Exists(_profilesDirectory))
            {
                Directory.CreateDirectory(_profilesDirectory);
            }
            
            // Initialize profile store
            string profilesFile = Path.Combine(_profilesDirectory, "profiles.json");
            _profileStore = new DeviceProfileStore(profilesFile);
            _profileStore.LoadProfiles();
        }

        public DeviceInfo FingerprintDevice(string host, int port, string username, string password, bool verbose = false)
        {
            Console.WriteLine($"Fingerprinting device {host}:{port}...");
            
            var fingerprinter = new DeviceFingerprint(host, port, username, password, 
                outputCallback: verbose ? Console.WriteLine : (Action<string>)null, 
                debug: verbose);

            try
            {
                // Run the fingerprinting process
                DeviceInfo deviceInfo = fingerprinter.Fingerprint(verbose);
                
                // If successful, update profile
                if (deviceInfo.DeviceType != DeviceType.Unknown)
                {
                    var profile = DeviceProfile.FromDeviceInfo(deviceInfo);
                    _profileStore.AddOrUpdateProfile(profile);
                    Console.WriteLine($"Saved profile for {host}:{port}");
                }
                
                return deviceInfo;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fingerprinting device: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.ToString());
                }
                
                var deviceInfo = new DeviceInfo
                {
                    Host = host,
                    Port = port,
                    Username = username
                };
                deviceInfo.CommandOutputs["error"] = ex.ToString();
                return deviceInfo;
            }
        }
        
        public List<DeviceProfile> GetAllProfiles()
        {
            return _profileStore.GetAllProfiles();
        }
        
        public DeviceProfile GetProfile(string host, int port = 22)
        {
            return _profileStore.GetProfile(host, port);
        }
        
        public List<DeviceProfile> GetProfilesByDeviceType(DeviceType deviceType)
        {
            return _profileStore.GetProfilesByDeviceType(deviceType.ToString());
        }
        
        public bool RemoveProfile(string host, int port = 22)
        {
            return _profileStore.RemoveProfile(host, port);
        }
    }
}