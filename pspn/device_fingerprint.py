import os
import re
import time
import json
import traceback
from enum import Enum, auto
from device_info import DeviceInfo, DeviceType
from ssh_client import SSHClient, SSHClientOptions


class DeviceFingerprint:
    def __init__(self, host, port, username, password, output_callback=None,
                 debug=False, verbose=False, connection_timeout=5000):
        self._device_info = DeviceInfo(
            host=host,
            port=port,
            username=username
        )
        self._device_info.password = password  # Store password for reporting if needed
        self._output_buffer = []
        self._is_connected = False
        self._paging_disabled = False
        self._verbose = verbose
        self._debug = debug
        self._connection_timeout = connection_timeout

        # Configure SSH client for fingerprinting with broader compatibility
        ssh_options = SSHClientOptions(
            host=host,
            port=port,
            username=username,
            password=password,
            invoke_shell=True,
            # Start with a very broad prompt pattern
            prompt="[#>$\\]\\):]",
            expect_prompt=None,
            prompt_count=1,
            shell_timeout=2,
            inter_command_time=0,
            expect_prompt_timeout=5000,
            debug=debug
        )

        # Set up output capture
        def buffer_callback(output):
            self._output_buffer.append(output)

        if output_callback:
            ssh_options.output_callback = lambda output: (
                output_callback(output),
                buffer_callback(output)
            )
        else:
            ssh_options.output_callback = buffer_callback

        self._ssh_client = SSHClient(ssh_options)

    def fingerprint(self):
        """Perform fingerprinting of the device"""
        try:
            # Connect to the device
            if self._debug:
                print("Connecting to {}:{}...".format(self._device_info.host, self._device_info.port))
            self._ssh_client.connect()
            self._is_connected = True

            # Detect prompt
            self._device_info.detected_prompt = self.detect_prompt()
            if self._debug:
                print("Detected prompt: {}".format(self._device_info.detected_prompt))

            # Disable paging
            if self._device_info.detected_prompt:
                # Try to identify the device type first
                if self._debug:
                    print("Trying to identify device type from initial connection...")

                # Check for device type indicators in the initial output
                initial_output = ''.join(self._output_buffer)
                initial_device_type = self.identify_vendor_from_output(initial_output)

                if initial_device_type != DeviceType.Unknown:
                    self._device_info.device_type = initial_device_type
                    if self._debug:
                        print("Initial device type detection: {}".format(initial_device_type.name))

                # Get and apply the disable paging command
                disable_paging_cmd = self._device_info.device_type.get_disable_paging_command()
                if disable_paging_cmd:
                    if self._debug:
                        print("Disabling paging with command: {}".format(disable_paging_cmd))

                    self._device_info.disable_paging_command = disable_paging_cmd
                    self.safe_execute_command(disable_paging_cmd)
                    self._paging_disabled = True

            # Run identification commands based on device type
            identification_commands = []

            if self._device_info.device_type != DeviceType.Unknown:
                identification_commands = self._device_info.device_type.get_identification_commands()
            else:
                # If device type is unknown, try some generic commands that work on many devices
                identification_commands = [
                    "show version",
                    "show system info",
                ]

            # Execute identification commands and collect output
            for cmd in identification_commands:
                if self._debug:
                    print("Executing identification command: {}".format(cmd))

                output = self.safe_execute_command(cmd)

                # Store the command output
                self._device_info.command_outputs[cmd] = output

                # Try to identify device type from command output if still unknown
                if self._device_info.device_type == DeviceType.Unknown:
                    detected_type = self.identify_vendor_from_output(output)
                    if detected_type != DeviceType.Unknown:
                        self._device_info.device_type = detected_type
                        if self._debug:
                            print("Detected device type: {}".format(detected_type.name))

                        # If we've now identified the device, try to disable paging if not done yet
                        if not self._paging_disabled:
                            disable_paging_cmd = self._device_info.device_type.get_disable_paging_command()
                            if disable_paging_cmd:
                                if self._debug:
                                    print("Disabling paging with command: {}".format(disable_paging_cmd))

                                self._device_info.disable_paging_command = disable_paging_cmd
                                self.safe_execute_command(disable_paging_cmd)
                                self._paging_disabled = True

            # Extract device details from accumulated output
            self.extract_device_details()

            return self._device_info

        except Exception as e:
            # traceback.print_exc()

            if self._debug:
                print("Error during fingerprinting: {}".format(str(e)))

            # Ensure we mark the fingerprinting as unsuccessful
            self._device_info.device_type = DeviceType.Unknown
            self._device_info.detected_prompt = None

            return self._device_info
        finally:
            # Always disconnect
            if self._is_connected:
                self._ssh_client.disconnect()
                self._is_connected = False

    def detect_prompt(self):
        """Detect the device prompt by sending a newline"""
        if self._debug:
            print("Starting improved prompt detection...")

        # First, check the current content of the buffer
        current_buffer = ''.join(self._output_buffer)
        if self._debug:
            print("Current buffer length: {} bytes".format(len(current_buffer)))

        # Look at the last few lines of the existing buffer for a prompt
        existing_lines = re.split(r'[\r\n]+', current_buffer)
        existing_lines = [line for line in existing_lines if line.strip()]

        if existing_lines:
            # Get the last line which is likely to be a prompt
            last_line = existing_lines[-1].strip() if existing_lines else ""
            if self._debug:
                print("Last line from existing buffer: '{}'".format(last_line))

            # Check if this looks like a valid prompt
            if last_line and (last_line.endswith('#') or last_line.endswith('>') or
                              last_line.endswith('$') or last_line.endswith(':') or
                              last_line.endswith(']') or last_line.endswith(')')):
                if self._debug:
                    print("Detected prompt from existing buffer: '{}'".format(last_line))

                # Set the expect prompt on the SSH client
                try:
                    self._ssh_client.set_expect_prompt(last_line)
                    if self._debug:
                        print("Set expect prompt on SSH client to: '{}'".format(last_line))
                except Exception as e:
                    traceback.print_exc()

                    if self._debug:
                        print("Error setting expect prompt: {}".format(str(e)))

                return last_line

        # If we don't have a valid prompt from existing buffer, try sending a newline
        if self._debug:
            print("No valid prompt found in buffer, sending newline...")

        # Mark the current length so we can extract only new content
        previous_length = len(''.join(self._output_buffer))

        # Send a newline and wait for response
        self._ssh_client.execute_command("\n")

        # Give it time to receive the response
        time.sleep(1)

        # Get only the new content received after our command
        new_content = ""
        current_buffer = ''.join(self._output_buffer)
        if len(current_buffer) > previous_length:
            new_content = current_buffer[previous_length:]
            if self._debug:
                print("New content after newline ({} bytes): '{}'".format(len(new_content), new_content))
        else:
            if self._debug:
                print("No new content received after newline command")

        # Parse the new content for a prompt
        new_lines = re.split(r'[\r\n]+', new_content)
        new_lines = [line for line in new_lines if line.strip()]
        prompt_line = new_lines[-1].strip() if new_lines else ""

        if prompt_line:
            if self._debug:
                print("Detected prompt after newline: '{}'".format(prompt_line))

            # Set the expect prompt on the SSH client
            try:
                self._ssh_client.set_expect_prompt(prompt_line)
                if self._debug:
                    print("Set expect prompt on SSH client to: '{}'".format(prompt_line))
            except Exception as e:
                traceback.print_exc()
                if self._debug:
                    print("Error setting expect prompt: {}".format(str(e)))

            return prompt_line

        # If still not successful, try a different approach - send a harmless command
        if self._debug:
            print("Trying with a harmless command...")
        previous_length = len(''.join(self._output_buffer))

        # Send a harmless command that works on most devices
        self._ssh_client.execute_command("?")

        # Give it time to receive the response
        time.sleep(1)

        current_buffer = ''.join(self._output_buffer)
        if len(current_buffer) > previous_length:
            new_content = current_buffer[previous_length:]
            if self._debug:
                print("New content after ? command ({} bytes): '{}'".format(len(new_content), new_content))

            # Get the last line which should include the prompt
            new_lines = re.split(r'[\r\n]+', new_content)
            new_lines = [line for line in new_lines if line.strip()]
            prompt_line = new_lines[-1].strip() if new_lines else ""

            if prompt_line:
                if self._debug:
                    print("Detected prompt after ? command: '{}'".format(prompt_line))

                # Set the expect prompt on the SSH client
                try:
                    self._ssh_client.set_expect_prompt(prompt_line)
                    if self._debug:
                        print("Set expect prompt on SSH client to: '{}'".format(prompt_line))
                except Exception as e:
                    traceback.print_exc()

                    if self._debug:
                        print("Error setting expect prompt: {}".format(str(e)))

                return prompt_line

        # Fall back to a default pattern
        if self._debug:
            print("Failed to detect prompt through all methods, using default pattern")
        return "[#>$]"

    def safe_execute_command(self, command, timeout_ms=3000, retries=1):
        """Execute command safely with timeout and retry logic"""
        for attempt in range(retries + 1):
            try:
                # Record the current buffer length to track only new output
                start_position = len(''.join(self._output_buffer))

                if self._debug:
                    print("Executing command (attempt {}/{}): '{}'".format(attempt + 1, retries + 1, command))
                    print("Buffer position before command: {}".format(start_position))

                # Execute the command
                self._ssh_client.execute_command(command)

                # Wait for initial response
                time.sleep(0.3)

                # Get current position
                current_buffer = ''.join(self._output_buffer)
                current_position = len(current_buffer)
                if self._debug:
                    print("Buffer position after initial wait: {}".format(current_position))

                # Only wait longer if we need to - up to max timeout
                start_time = time.time()
                end_time = start_time + (timeout_ms / 1000)

                # Track buffer changes
                last_known_length = current_position
                last_change_time = time.time()

                while time.time() < end_time:
                    # Check if buffer has changed
                    current_buffer = ''.join(self._output_buffer)
                    current_position = len(current_buffer)

                    if current_position > last_known_length:
                        # Buffer has grown, update last change time
                        last_known_length = current_position
                        last_change_time = time.time()
                    elif (time.time() - last_change_time > 0.3 and
                          time.time() - start_time > 0.5):
                        # Buffer hasn't changed for 300ms and we've waited at least 500ms total
                        if self._debug:
                            print("Command appears complete (no buffer change)")
                        break

                    # Check if output ends with the prompt (if we know it)
                    if self._device_info.detected_prompt:
                        if current_buffer.rstrip().endswith(self._device_info.detected_prompt):
                            if self._debug:
                                print("Command appears complete (prompt detected)")
                            break

                    # Short sleep to prevent CPU spinning
                    time.sleep(0.05)

                # Extract only the new output
                result = ""
                current_buffer = ''.join(self._output_buffer)
                if len(current_buffer) > start_position:
                    result = current_buffer[start_position:]

                if self._debug:
                    print("Command complete, received {} bytes of output".format(len(result)))
                return result

            except Exception as e:
                traceback.print_exc()

                if self._debug:
                    print("Error executing command: {}".format(str(e)))

                # If we've reached the maximum number of retries, return the error
                if attempt == retries:
                    return "ERROR: {}".format(str(e))

                # Otherwise wait and try again
                time.sleep(1)

                # Try to recover if this is a channel issue
                if "channel" in str(e).lower() and attempt < retries:
                    if self._debug:
                        print("Detected channel issue, attempting to reconnect...")
                    try:
                        self._ssh_client.disconnect()
                        time.sleep(1)
                        self._ssh_client.connect()
                    except Exception as reconnect_ex:
                        traceback.print_exc()

                        if self._debug:
                            print("Reconnection attempt failed: {}".format(str(reconnect_ex)))

        return "ERROR: Max retries exceeded"

    def identify_vendor_from_output(self, output):
        """Identify device type from command output"""
        lower_output = output.lower()

        # Identify based on explicit vendor/OS mentions
        # Cisco product family
        if "cisco ios" in lower_output or "cisco internetwork operating system" in lower_output:
            return DeviceType.CiscoIOS

        if "ios-xe" in lower_output:
            return DeviceType.CiscoIOS

        if "nx-os" in lower_output or "nexus" in lower_output:
            return DeviceType.CiscoNXOS

        if "adaptive security appliance" in lower_output or "asa" in lower_output:
            return DeviceType.CiscoASA

        # Arista
        if "arista" in lower_output or ("eos" in lower_output and "cisco" not in lower_output):
            return DeviceType.AristaEOS

        # Juniper
        if "junos" in lower_output or "juniper" in lower_output:
            return DeviceType.JuniperJunOS

        # HPE/Aruba products
        if ("hp" in lower_output or "hewlett-packard" in lower_output) and "procurve" in lower_output:
            return DeviceType.HPProCurve

        if "aruba" in lower_output:
            return DeviceType.HPProCurve  # Use HPProCurve for Aruba switches

        # Fortinet
        if "fortigate" in lower_output or "fortios" in lower_output:
            return DeviceType.FortiOS

        # Palo Alto
        if "pan-os" in lower_output or "palo alto" in lower_output:
            return DeviceType.PaloAltoOS

        # Generic OS types
        if any(os in lower_output for os in ["linux", "ubuntu", "centos", "debian", "redhat", "fedora"]):
            return DeviceType.Linux

        if "freebsd" in lower_output:
            return DeviceType.FreeBSD

        if "windows" in lower_output or "microsoft" in lower_output:
            return DeviceType.Windows

        # Identify based on product model mentions that imply vendor
        if re.search(r'\bws-c\d{4}\b', lower_output) or re.search(r'\bc\d{4}\b', lower_output):
            return DeviceType.CiscoIOS

        if re.search(r'\bn\d{4}\b', lower_output) or "nexus" in lower_output:
            return DeviceType.CiscoNXOS

        return DeviceType.Unknown

    def extract_device_details(self):
        """Extract detailed information from command outputs"""
        # Get the full output buffer content
        output = ''.join(self._output_buffer)

        # Extract hostname
        hostname_patterns = {
            DeviceType.CiscoIOS: r'hostname\s+([^\s\r\n]+)',
            DeviceType.CiscoNXOS: r'hostname\s+([^\s\r\n]+)',
            DeviceType.CiscoASA: r'hostname\s+([^\s\r\n]+)',
            DeviceType.AristaEOS: r'hostname\s+([^\s\r\n]+)',
            DeviceType.JuniperJunOS: r'host-name\s+([^\s\r\n;]+)',
            DeviceType.Linux: r'Hostname:[^\n]*(\S+)[\r\n]',
            DeviceType.GenericUnix: r'([A-Za-z0-9\-]+)[@][^:]+:'
        }

        if self._device_info.device_type in hostname_patterns:
            pattern = hostname_patterns[self._device_info.device_type]
            match = re.search(pattern, output, re.IGNORECASE)
            if match and match.group(1):
                self._device_info.hostname = match.group(1)

        # If we couldn't extract a hostname, use the prompt as a fallback
        if not self._device_info.hostname and self._device_info.detected_prompt:
            # Extract hostname from prompt (typical format username@hostname or hostname#)
            prompt_hostname_match = re.match(
                r'^([A-Za-z0-9\-._]+)(?:[>#]|$)',
                self._device_info.detected_prompt
            )
            if prompt_hostname_match and prompt_hostname_match.group(1):
                self._device_info.hostname = prompt_hostname_match.group(1)

        # Extract serial number - common pattern across many devices
        serial_match = re.search(r'[Ss]erial\s*[Nn]umber\s*:?\s*([A-Za-z0-9\-]+)', output, re.IGNORECASE)
        if serial_match and serial_match.group(1):
            self._device_info.serial_number = serial_match.group(1).strip()

        # Extract more details based on device type
        if self._device_info.device_type == DeviceType.CiscoIOS:
            # Extract version from "show version" output
            version_match = re.search(r'(?:IOS|Software).+?Version\s+([^,\s\r\n]+)', output, re.IGNORECASE)
            if version_match and version_match.group(1):
                self._device_info.version = version_match.group(1).strip()

            # Extract model information
            model_match = re.search(r'[Cc]isco\s+([A-Za-z0-9\-]+)(?:\s+[^\n]*?)(?:processor|chassis|router|switch)',
                                    output,
                                    re.DOTALL)
            if model_match and model_match.group(1):
                self._device_info.model = model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.CiscoNXOS:
            # Extract version for NX-OS
            nxos_version_match = re.search(r'NXOS:\s+version\s+([^,\s\r\n]+)', output, re.IGNORECASE)
            if nxos_version_match and nxos_version_match.group(1):
                self._device_info.version = nxos_version_match.group(1).strip()

            # Extract model for Nexus
            nxos_model_match = re.search(r'cisco\s+Nexus\s+([^\s]+)', output, re.IGNORECASE)
            if nxos_model_match and nxos_model_match.group(1):
                self._device_info.model = "Nexus " + nxos_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.CiscoASA:
            # Extract version for ASA
            asa_version_match = re.search(r'Adaptive Security Appliance.*?Version\s+([^,\s\r\n]+)', output,
                                          re.IGNORECASE)
            if asa_version_match and asa_version_match.group(1):
                self._device_info.version = asa_version_match.group(1).strip()

            # Extract model for ASA
            asa_model_match = re.search(r'Hardware:\s+([^,\r\n]+)', output, re.IGNORECASE)
            if asa_model_match and asa_model_match.group(1):
                self._device_info.model = asa_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.AristaEOS:
            # Extract version for Arista EOS
            arista_version_match = re.search(r'EOS\s+version\s+([^,\s\r\n]+)', output, re.IGNORECASE)
            if arista_version_match and arista_version_match.group(1):
                self._device_info.version = arista_version_match.group(1).strip()

            # Extract model for Arista switches
            arista_model_match = re.search(r'Arista\s+([A-Za-z0-9\-]+)', output, re.IGNORECASE)
            if arista_model_match and arista_model_match.group(1):
                self._device_info.model = arista_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.JuniperJunOS:
            # Extract version for JunOS
            junos_version_match = re.search(r'JUNOS\s+([^,\s\r\n\]]+)', output, re.IGNORECASE)
            if junos_version_match and junos_version_match.group(1):
                self._device_info.version = junos_version_match.group(1).strip()

            # Extract model for Juniper
            junos_model_match = re.search(r'Model:\s*([^\r\n]+)', output, re.IGNORECASE)
            if junos_model_match and junos_model_match.group(1):
                self._device_info.model = junos_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.HPProCurve:
            # Extract version for HP ProCurve
            hp_version_match = re.search(r'Software\s+revision\s*:?\s*([^\r\n]+)', output, re.IGNORECASE)
            if hp_version_match and hp_version_match.group(1):
                self._device_info.version = hp_version_match.group(1).strip()

            # Extract model for HP
            hp_model_match = re.search(r'[Ss]witch\s+([A-Za-z0-9\-]+)', output)
            if hp_model_match and hp_model_match.group(1):
                self._device_info.model = hp_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.FortiOS:
            # Extract version for FortiOS
            forti_version_match = re.search(r'Version:\s*([^\r\n]+)', output, re.IGNORECASE)
            if forti_version_match and forti_version_match.group(1):
                self._device_info.version = forti_version_match.group(1).strip()

            # Extract model for FortiGate
            forti_model_match = re.search(r'FortiGate-([A-Za-z0-9\-]+)', output, re.IGNORECASE)
            if forti_model_match and forti_model_match.group(1):
                self._device_info.model = "FortiGate-" + forti_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.PaloAltoOS:
            # Extract version for PAN-OS
            palo_version_match = re.search(r'sw-version:\s*([^\r\n]+)', output, re.IGNORECASE)
            if palo_version_match and palo_version_match.group(1):
                self._device_info.version = palo_version_match.group(1).strip()

            # Extract model for Palo Alto
            palo_model_match = re.search(r'model:\s*([^\r\n]+)', output, re.IGNORECASE)
            if palo_model_match and palo_model_match.group(1):
                self._device_info.model = palo_model_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.Linux:
            # Extract Linux distribution and version
            linux_version_match = re.search(r'PRETTY_NAME="([^"]+)"', output, re.IGNORECASE)
            if linux_version_match and linux_version_match.group(1):
                self._device_info.version = linux_version_match.group(1).strip()
            else:
                # Try uname output
                uname_match = re.search(r'Linux\s+\S+\s+([^\s]+)', output)
                if uname_match and uname_match.group(1):
                    self._device_info.version = uname_match.group(1).strip()

            # For Linux, we might extract CPU information
            cpu_info_match = re.search(r'model name\s*:\s*([^\r\n]+)', output, re.IGNORECASE)
            if cpu_info_match and cpu_info_match.group(1):
                self._device_info.cpu_info = cpu_info_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.FreeBSD:
            # Extract FreeBSD version
            freebsd_version_match = re.search(r'FreeBSD\s+\S+\s+([^\s]+)', output)
            if freebsd_version_match and freebsd_version_match.group(1):
                self._device_info.version = freebsd_version_match.group(1).strip()

        elif self._device_info.device_type == DeviceType.Windows:
            # Extract Windows version
            windows_version_match = re.search(r'OS Name:\s*([^\r\n]+)', output, re.IGNORECASE)
            if windows_version_match and windows_version_match.group(1):
                self._device_info.version = windows_version_match.group(1).strip()

            # Extract Windows model
            windows_model_match = re.search(r'System Model:\s*([^\r\n]+)', output, re.IGNORECASE)
            if windows_model_match and windows_model_match.group(1):
                self._device_info.model = windows_model_match.group(1).strip()

        # Extract IP address information from outputs if available
        ip_addresses = []

        # Look for IP addresses in output
        ip_pattern = r'\b(?:\d{1,3}\.){3}\d{1,3}(?:/\d{1,2})?\b'
        ip_matches = re.finditer(ip_pattern, output)

        # Filter to get likely management IPs (not every IP in the output)
        for match in ip_matches:
            ip = match.group(0)

            # Skip obviously invalid IPs
            if ip.startswith('0.') or ip.startswith('255.'):
                continue

            # Check context - look for lines with "ip address" or similar
            line_start = max(0, match.start() - 50)
            line_end = min(len(output), match.end() + 50)
            context = output[line_start:line_end].lower()

            if any(term in context for term in ['ip address', 'management', 'vlan', 'interface']):
                if ip not in ip_addresses:
                    ip_addresses.append(ip)

        # Add up to 5 most likely IPs, but don't overload with too many
        self._device_info.ip_addresses.extend(ip_addresses[:5])

        # Extract interface information for network devices
        if self._device_info.device_type in [DeviceType.CiscoIOS, DeviceType.CiscoNXOS,
                                             DeviceType.CiscoASA, DeviceType.AristaEOS,
                                             DeviceType.JuniperJunOS]:
            # Look for interface patterns like "GigabitEthernet0/0 is up, line protocol is up"
            interface_matches = re.finditer(r'([A-Za-z0-9/\-\.]+)\s+is\s+(up|down|administratively down)', output)
            for match in interface_matches:
                interface_name = match.group(1)
                interface_status = match.group(2)

                # Get IP if available (for this interface)
                ip_match = re.search(f'{re.escape(interface_name)}.*?({ip_pattern})', output, re.DOTALL)
                interface_info = "Status: {}".format(interface_status)

                if ip_match:
                    interface_info += ", IP: {}".format(ip_match.group(1))

                self._device_info.interfaces[interface_name] = interface_info