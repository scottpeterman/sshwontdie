using System;
using System.Collections.Generic;

namespace SSHPassCSharp.Fingerprint
{
    public enum DeviceType
    {
        Unknown,
        CiscoIOS,
        CiscoNXOS,
        CiscoASA,
        JuniperJunOS,
        HPProCurve,
        AristaEOS,
        Linux,
        FreeBSD,
        Windows,
        FortiOS,
        PaloAltoOS,
        GenericUnix
    }
    
    public static class DeviceTypeExtensions
    {
        private static readonly Dictionary<DeviceType, string> DisablePagingCommands = new Dictionary<DeviceType, string>
        {
            { DeviceType.CiscoIOS, "terminal length 0" },
            { DeviceType.CiscoNXOS, "terminal length 0" },
            { DeviceType.CiscoASA, "terminal pager 0" },
            { DeviceType.JuniperJunOS, "set cli screen-length 0" },
            { DeviceType.HPProCurve, "no page" },
            { DeviceType.AristaEOS, "terminal length 0" },
            { DeviceType.Linux, "export TERM=xterm; stty rows 1000" },
            { DeviceType.FreeBSD, "export TERM=xterm; stty rows 1000" },
            { DeviceType.FortiOS, "config system console\nset output standard\nend" },
            { DeviceType.PaloAltoOS, "set cli pager off" },
            { DeviceType.GenericUnix, "export TERM=xterm; stty rows 1000" },
            { DeviceType.Windows, "" }, // Windows doesn't typically need paging disabled
            { DeviceType.Unknown, "" }
        };

        private static readonly Dictionary<DeviceType, string[]> IdentificationCommands = new Dictionary<DeviceType, string[]>
        {
            { DeviceType.CiscoIOS, new[] { "show version", "show inventory" } },
            { DeviceType.CiscoNXOS, new[] { "show version", "show inventory" } },
            { DeviceType.CiscoASA, new[] { "show version", "show inventory" } },
            { DeviceType.JuniperJunOS, new[] { "show version", "show chassis hardware" } },
            { DeviceType.HPProCurve, new[] { "show system", "show system-information" } },
            { DeviceType.AristaEOS, new[] { "show version", "show inventory" } },
            { DeviceType.Linux, new[] { "uname -a", "cat /etc/os-release", "hostnamectl" } },
            { DeviceType.FreeBSD, new[] { "uname -a", "freebsd-version" } },
            { DeviceType.FortiOS, new[] { "get system status", "get hardware status" } },
            { DeviceType.PaloAltoOS, new[] { "show system info" } },
            { DeviceType.GenericUnix, new[] { "uname -a" } },
            { DeviceType.Windows, new[] { "ver", "systeminfo" } },
            { DeviceType.Unknown, new[] { "help", "?" } }
        };

        public static string GetDisablePagingCommand(this DeviceType deviceType)
        {
            return DisablePagingCommands.TryGetValue(deviceType, out string command) 
                ? command 
                : string.Empty;
        }

        public static string[] GetIdentificationCommands(this DeviceType deviceType)
        {
            return IdentificationCommands.TryGetValue(deviceType, out string[] commands) 
                ? commands 
                : Array.Empty<string>();
        }
    }
}