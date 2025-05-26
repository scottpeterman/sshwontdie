using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSHPassCSharp.Fingerprint
{
    public class DeviceCredential
    {
        public string DeviceType { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool IsDefault { get; set; }
    }

    public class DeviceCredentialsStore
    {
        private List<DeviceCredential> _credentials;
        private readonly string _credentialsFile;

        public DeviceCredentialsStore(string credentialsFile = null)
        {
            _credentials = new List<DeviceCredential>();
            _credentialsFile = credentialsFile ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sshfingerprint",
                "credentials.json");
        }

        public void LoadCredentials()
        {
            if (!File.Exists(_credentialsFile))
            {
                _credentials = new List<DeviceCredential>();
                return;
            }

            try
            {
                string json = File.ReadAllText(_credentialsFile);
                _credentials = JsonSerializer.Deserialize<List<DeviceCredential>>(json) ?? new List<DeviceCredential>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading credentials: {ex.Message}");
                _credentials = new List<DeviceCredential>();
            }
        }

        public void SaveCredentials()
        {
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(_credentialsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string json = JsonSerializer.Serialize(_credentials, options);
                File.WriteAllText(_credentialsFile, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error saving credentials: {ex.Message}");
            }
        }

        public void AddCredential(DeviceCredential credential)
        {
            // If this is a default credential, remove any existing defaults for this device type
            if (credential.IsDefault)
            {
                _credentials.RemoveAll(c => c.DeviceType == credential.DeviceType && c.IsDefault);
            }

            _credentials.Add(credential);
            SaveCredentials();
        }

        public DeviceCredential GetCredential(string deviceType)
        {
            return _credentials.Find(c => c.DeviceType == deviceType && c.IsDefault) ??
                   _credentials.Find(c => c.DeviceType == "default" && c.IsDefault);
        }

        public List<DeviceCredential> GetAllCredentials()
        {
            return _credentials;
        }

        public bool RemoveCredential(string deviceType, string username)
        {
            int count = _credentials.RemoveAll(c => c.DeviceType == deviceType && c.Username == username);
            if (count > 0)
            {
                SaveCredentials();
                return true;
            }
            return false;
        }
    }
}