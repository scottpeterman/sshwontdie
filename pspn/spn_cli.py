#!/usr/bin/env python3
import os
import sys
import json
import time
import argparse
import traceback
from typing import List, Optional, Dict, Any
from pathlib import Path
from datetime import datetime

# Import our modules - Make sure the imports are correctly set
try:
    from device_info import DeviceInfo, DeviceType
    from ssh_client import SSHClient, SSHClientOptions
    from device_fingerprint import DeviceFingerprint
except ImportError:
    # Try relative imports if necessary
    try:
        from .device_info import DeviceInfo, DeviceType
        from .ssh_client import SSHClient, SSHClientOptions
        from .device_fingerprint import DeviceFingerprint
    except ImportError:
        print("Error importing required modules. Make sure they are in the current directory or Python path.")
        sys.exit(1)


class SPN:
    VERSION = "1.0.0"
    COPYRIGHT = "Copyright (C) 2025 SSHPassPython"

    def __init__(self):
        self.args = self.parse_arguments()

        # Parse host and port
        self.host, self.port = self.parse_host_port(self.args.host)

        # Setup logging
        self.log_file = self.setup_logging()

    def parse_arguments(self):
        """Parse command line arguments to match the C# application"""
        # Create parser with add_help=False to avoid the -h conflict
        parser = argparse.ArgumentParser(
            description=f"SSHPassPython {self.VERSION}\n{self.COPYRIGHT}",
            formatter_class=argparse.RawTextHelpFormatter,
            add_help=False  # Disable automatic -h for help
        )

        # Add explicit help argument with a different flag
        parser.add_argument("--help", action="help",
                            help="Show this help message and exit")

        # Required arguments - NOTE: Changed -h to --host only to avoid conflict
        parser.add_argument("--host", required=True, help="SSH Host (ip:port)")
        parser.add_argument("-u", "--user", required=True, help="SSH Username")
        parser.add_argument("-p", "--password", required=True, help="SSH Password")

        # Command options
        parser.add_argument("-c", "--cmds", default="", help="Commands to run, separated by comma")

        # SSH options
        parser.add_argument("--invoke-shell", action="store_true",
                            help="Invoke shell before running the command")
        parser.add_argument("--prompt", default="",
                            help="Prompt to look for before breaking the shell")
        parser.add_argument("--prompt-count", type=int, default=1,
                            help="Number of prompts to look for before breaking the shell")
        parser.add_argument("-t", "--timeout", type=int, default=360,
                            help="Command timeout duration in seconds")
        parser.add_argument("--shell-timeout", type=int, default=10,
                            help="Overall shell session timeout in seconds (default is 10 seconds)")
        parser.add_argument("-i", "--inter-command-time", type=int, default=1,
                            help="Inter-command time in seconds")

        # Logging and output
        parser.add_argument("--log-file", default="",
                            help="Path to log file (default is ./logs/hostname.log)")
        parser.add_argument("--require-hyphen", action="store_true",
                            help="Require hyphen in prompt detection (for Cisco/network devices)")
        parser.add_argument("-d", "--debug", action="store_true", help="Enable debug output")

        # Fingerprinting options
        parser.add_argument("-f", "--fingerprint", action="store_true",
                            help="Fingerprint device before executing commands")
        parser.add_argument("-o", "--fingerprint-output", default="",
                            help="Save fingerprint results to JSON file")
        parser.add_argument("-v", "--verbose", action="store_true", help="Show verbose output")

        # Save output
        parser.add_argument("-s", "--save", default="", help="File path to save command output")

        # Version info
        parser.add_argument("--version", action="version",
                            version=f"SSHPassPython {self.VERSION}\n{self.COPYRIGHT}")

        return parser.parse_args()

    def parse_host_port(self, host_arg: str) -> tuple:
        """Parse host:port format, defaulting to port 22 if not specified"""
        if ":" in host_arg:
            host, port_str = host_arg.split(":", 1)
            try:
                port = int(port_str)
                return host, port
            except ValueError:
                print(f"Invalid port: {port_str}. Using default port 22.")
                return host_arg, 22
        return host_arg, 22

    def setup_logging(self) -> str:
        """Setup logging based on arguments"""
        if self.args.log_file:
            log_file = self.args.log_file
        else:
            log_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
            os.makedirs(log_dir, exist_ok=True)
            log_file = os.path.join(log_dir, f"{self.host}.log")

        return log_file

    def format_fingerprint_json(self, device_info: DeviceInfo) -> Dict[str, Any]:
        """Format the DeviceInfo to match the C# JSON output format"""
        return {
            "Host": device_info.host,
            "Port": device_info.port,
            "DeviceType": device_info.device_type.value,
            "DetectedPrompt": device_info.detected_prompt,
            "DisablePagingCommand": device_info.disable_paging_command,
            "Hostname": device_info.hostname,
            "Password": None,  # Always null for security
            "Model": device_info.model,
            "Version": device_info.version,
            "SerialNumber": device_info.serial_number,
            "IsVirtualDevice": device_info.is_virtual_device,
            "Platform": device_info.platform,
            "UpTime": device_info.uptime,
            "AdditionalInfo": device_info.additional_info,
            "Interfaces": device_info.interfaces,
            "IPAddresses": device_info.ip_addresses,
            "CPUInfo": device_info.cpu_info,
            "MemoryInfo": device_info.memory_info,
            "StorageInfo": device_info.storage_info,
            "CommandOutputs": device_info.command_outputs,
            "FingerprintTime": device_info.fingerprint_time.isoformat(),
            "Success": device_info.success
        }

    def run_fingerprint(self) -> Optional[DeviceInfo]:
        """Run the fingerprinting process"""
        print(f"Starting device fingerprinting on {self.host}:{self.port}...")

        # Create fingerprinter
        fingerprinter = DeviceFingerprint(
            host=self.host,
            port=self.port,
            username=self.args.user,
            password=self.args.password,
            debug=self.args.debug,
            verbose=self.args.verbose
        )

        # Perform fingerprinting
        device_info = fingerprinter.fingerprint()

        if device_info.success:
            print(f"Successfully fingerprinted device: {device_info.device_type.name}")

            if self.args.verbose:
                print(device_info.get_summary())

            # Save fingerprint to file if requested
            if self.args.fingerprint_output:
                try:
                    # Format the JSON to match C# output format
                    formatted_json = self.format_fingerprint_json(device_info)

                    with open(self.args.fingerprint_output, 'w') as f:
                        json.dump(formatted_json, f, indent=2)
                    print(f"Fingerprint saved to {self.args.fingerprint_output}")
                except Exception as e:
                    print(f"Error saving fingerprint to file: {str(e)}")
        else:
            print("Fingerprinting failed.")

        return device_info

    def execute_commands(self, device_info: Optional[DeviceInfo] = None):
        """Execute the provided commands"""
        if not self.args.cmds:
            print("No commands provided for execution.")
            return

        print(f"Executing commands on {self.host}:{self.port}...")

        # Parse commands
        commands = self.args.cmds.split(',')

        # Configure SSH client based on arguments
        ssh_options = SSHClientOptions(
            host=self.host,
            port=self.port,
            username=self.args.user,
            password=self.args.password,
            invoke_shell=self.args.invoke_shell,
            prompt=self.args.prompt,
            prompt_count=self.args.prompt_count,
            timeout=self.args.timeout,
            shell_timeout=self.args.shell_timeout,
            inter_command_time=self.args.inter_command_time,
            log_file=self.log_file,
            debug=self.args.debug
        )

        # If we have device info from fingerprinting, use the detected prompt
        if device_info and device_info.detected_prompt:
            ssh_options.expect_prompt = device_info.detected_prompt

            # Also use the disable paging command if available
            if device_info.disable_paging_command:
                commands.insert(0, device_info.disable_paging_command)

        ssh_client = SSHClient(ssh_options)

        try:
            # Connect to the device
            ssh_client.connect()

            # Execute each command in sequence
            results = []
            for cmd in commands:
                cmd = cmd.strip()
                if not cmd:
                    continue

                if self.args.verbose:
                    print(f"Executing command: {cmd}")

                result = ssh_client.execute_command(cmd)
                results.append(result)

            # Combine all results
            full_output = "\n".join(results)

            # Save output if requested
            if self.args.save:
                try:
                    cleaned_output = full_output.replace("\r","")
                    with open(self.args.save, 'w', encoding='utf-8') as f:
                        f.write(cleaned_output)
                    print(f"Command output saved to {self.args.save}")
                except Exception as e:
                    print(f"Error saving output to file: {str(e)}")

            # Disconnect
            ssh_client.disconnect()

            return full_output

        except Exception as e:
            traceback.print_exc()
            print(f"Error during command execution: {str(e)}")
            sys.exit(1)

    def run(self):
        """Main execution logic"""
        print(f"SSHPassPython {self.VERSION}")
        print(f"Connecting to {self.host}:{self.port} as {self.args.user}...")

        device_info = None

        # Fingerprinting process first if requested
        if self.args.fingerprint:
            device_info = self.run_fingerprint()

        # Execute commands if provided
        if self.args.cmds:
            output = self.execute_commands(device_info)
            if not self.args.save and output:
                # If not saved to file, print to stdout (but only if verbose)
                if self.args.verbose:
                    print("\n--- Command Output ---")
                    print(output)
                    print("---------------------")

        print("Done.")


def main():
    """Entry point for the program"""
    spn = SPN()
    spn.run()


if __name__ == "__main__":
    main()