import sys
import time
import re
import logging
import os
import paramiko
from io import StringIO
from datetime import datetime


class SSHClientOptions:
    def __init__(self, host, username, password, port=22, invoke_shell=False,
                 expect_prompt=None, prompt=None, prompt_count=1, timeout=360,
                 shell_timeout=5, inter_command_time=1, log_file=None, debug=False,
                 expect_prompt_timeout=30000):
        self.host = host
        self.port = port
        self.username = username
        self.password = password
        self.invoke_shell = invoke_shell
        self.expect_prompt = expect_prompt
        self.prompt = prompt
        self.prompt_count = prompt_count
        self.timeout = timeout
        self.shell_timeout = shell_timeout
        self.inter_command_time = inter_command_time
        self.log_file = log_file
        self.debug = debug
        self.expect_prompt_timeout = expect_prompt_timeout

        # Default callbacks if none provided
        self.output_callback = print
        self.error_callback = lambda msg: print("ERROR: {}".format(msg), file=sys.stderr)


class SSHClient:
    def __init__(self, options):
        self._options = options
        self._ssh_client = None
        self._shell = None
        self._output_buffer = StringIO()
        self._prompt_detected = False

        # Validate required options
        if not options.host:
            raise ValueError("Host is required")
        if not options.username:
            raise ValueError("Username is required")
        if not options.password:
            raise ValueError("Password is required")

    def _log_with_timestamp(self, message, always_print=False):
        """Helper method to log with timestamp"""
        # Use datetime instead of time.strftime for microsecond support
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]
        timestamped_message = "[{}] {}".format(timestamp, message)

        if self._options.debug or always_print:
            print(timestamped_message)

        self._log_message(timestamped_message)

    def connect(self):
        """Connect to the remote device"""
        self._log_with_timestamp("Connecting to {}:{}...".format(self._options.host, self._options.port), True)

        # Create SSH client
        self._ssh_client = paramiko.SSHClient()
        self._ssh_client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

        try:
            self._ssh_client.connect(
                hostname=self._options.host,
                port=self._options.port,
                username=self._options.username,
                password=self._options.password,
                timeout=self._options.timeout,
                allow_agent=False,
                look_for_keys=False
            )

            self._log_with_timestamp("Connected to {}:{}".format(self._options.host, self._options.port), True)

            # Create shell if we're using shell mode
            if self._options.invoke_shell:
                self._create_shell_stream()

                # Check if a prompt pattern is defined
                if not self._options.prompt and not self._options.expect_prompt:
                    self._log_with_timestamp(
                        "WARNING: No prompt pattern or expect prompt defined. Shell commands may not work correctly!",
                        True)
                    self._log_with_timestamp("Consider setting a prompt pattern for proper command handling.", True)
        except Exception as e:
            self._log_with_timestamp("Connection error: {}".format(str(e)), True)
            raise

    def find_prompt(self, attempt_count=5, timeout=5):
        """
        Auto-detect the command prompt pattern using a refined approach that handles
        multiple prompt repetitions.

        Args:
            attempt_count (int): Number of attempts to detect the prompt
            timeout (float): Timeout in seconds for each attempt

        Returns:
            str: The detected prompt string or None if detection fails
        """
        if not self._shell:
            raise RuntimeError("Shell not initialized")

        self._log_with_timestamp("Attempting to auto-detect command prompt pattern...", True)

        # Clear any existing data in the buffer
        self._output_buffer = StringIO()
        buffer = ""

        # First clear any pending data
        while self._shell.recv_ready():
            try:
                data = self._shell.recv(4096).decode('utf-8', errors='replace')
                buffer += data
            except Exception as e:
                self._log_with_timestamp(f"Error clearing buffer: {str(e)}")

        # Send a single newline to trigger prompt (avoid multiple newlines)
        self._log_with_timestamp("Sending single newline to trigger prompt")
        self._shell.send("\n")

        # Wait a bit for the device to respond
        time.sleep(3)  # Increase wait time to ensure full response

        # Collect all available output
        buffer = ""
        start_time = time.time()
        while time.time() - start_time < 3:  # Short timeout for initial response
            if self._shell.recv_ready():
                try:
                    data = self._shell.recv(4096).decode('utf-8', errors='replace')
                    buffer += data
                    self._output_buffer.write(data)
                except Exception as e:
                    self._log_with_timestamp(f"Error reading from shell: {str(e)}")
            else:
                time.sleep(0.1)

        # Process the buffer to extract a clean prompt
        prompt = self._extract_clean_prompt(buffer)
        if prompt:
            self._log_with_timestamp(f"Detected prompt from initial attempt: '{prompt}'", True)
            return prompt

        # If that didn't work, try a more methodical approach
        for i in range(attempt_count):
            self._log_with_timestamp(f"Sending single newline (attempt {i + 1}/{attempt_count})")

            # Clear buffer for this attempt
            buffer = ""

            # Send just one newline to avoid multiple prompt repetitions
            self._shell.send("\n")

            # Collect response
            start_time = time.time()
            while time.time() - start_time < timeout:
                if self._shell.recv_ready():
                    try:
                        data = self._shell.recv(4096).decode('utf-8', errors='replace')
                        buffer += data
                        self._output_buffer.write(data)
                        self._options.output_callback(data)
                    except Exception as e:
                        self._log_with_timestamp(f"Error reading from shell: {str(e)}")
                        continue
                else:
                    # If we got some data and no more is coming, process it
                    if buffer:
                        prompt = self._extract_clean_prompt(buffer)
                        if prompt:
                            self._log_with_timestamp(f"Detected prompt: '{prompt}'", True)
                            return prompt

                    # Small pause to prevent CPU spinning
                    time.sleep(0.1)

            # If timeout occurred but we have buffer data, try to extract prompt
            if buffer:
                prompt = self._extract_clean_prompt(buffer)
                if prompt:
                    self._log_with_timestamp(f"Extracted prompt (timeout): '{prompt}'", True)
                    return prompt

        # Last resort: try sending a specific command that will force an output with hostname
        self._log_with_timestamp("Trying hostname command as last resort")

        # Try a common command that shows hostname
        self._shell.send("hostname\n")
        time.sleep(2)

        # Collect response
        buffer = ""
        start_time = time.time()
        while time.time() - start_time < 5:  # Longer timeout for command
            if self._shell.recv_ready():
                try:
                    data = self._shell.recv(4096).decode('utf-8', errors='replace')
                    buffer += data
                    self._output_buffer.write(data)
                except Exception as e:
                    self._log_with_timestamp(f"Error reading from shell: {str(e)}")
            else:
                time.sleep(0.1)

        # Extract prompt from command output
        prompt = self._extract_clean_prompt(buffer)
        if prompt:
            self._log_with_timestamp(f"Detected prompt from hostname command: '{prompt}'", True)
            return prompt

        # Absolute last resort
        self._log_with_timestamp("Could not detect prompt, falling back to default")
        return '#'  # Default fallback

    def _extract_clean_prompt(self, buffer):
        """
        Extract a clean prompt from buffer, handling cases where the prompt is repeated.

        Args:
            buffer (str): The buffer containing potential prompts

        Returns:
            str: A clean, single instance of the prompt
        """
        if not buffer or not buffer.strip():
            return None

        # Remove ANSI escape sequences
        import re
        ansi_escape = re.compile(r'\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])')
        clean_buffer = ansi_escape.sub('', buffer)

        # Get non-empty lines
        lines = [line.strip() for line in clean_buffer.split('\n') if line.strip()]
        if not lines:
            return None

        # Look for repeated patterns in the last line
        last_line = lines[-1]

        # Common prompt ending characters
        common_endings = ['#', '>', '$', '%', ':', '~]', ']', '}', ')', '|']

        # First check if the last line is a simple prompt (no repetition)
        if any(last_line.endswith(char) for char in common_endings) and len(last_line) < 30:
            if not self._is_repeated_prompt(last_line):
                return last_line

        # Check for repetitions (like 'device# device# device#')
        base_prompt = self._extract_base_prompt(last_line)
        if base_prompt:
            self._log_with_timestamp(f"Extracted base prompt from repeated pattern: '{base_prompt}'")
            return base_prompt

        # If the last line doesn't have repetitions but looks like a prompt
        for line in reversed(lines):
            if any(line.endswith(char) for char in common_endings):
                base_prompt = self._extract_base_prompt(line)
                if base_prompt:
                    return base_prompt
                return line

        # Last resort - try to find anything that looks like a prompt in any line
        for line in reversed(lines):
            # Check if line looks like a hostname or path with prompt char
            if len(line) < 50:  # Not too long
                for ending in common_endings:
                    if ending in line:
                        parts = line.split(ending)
                        # If there are multiple parts and the last isn't empty (like in 'device#')
                        if len(parts) > 1 and not parts[-1].strip():
                            # Get the part before the last prompt char
                            base = parts[0].strip()
                            for i in range(1, len(parts) - 1):
                                base += ending + parts[i].strip()
                            return base + ending

        # If all else fails, just use the last line
        return lines[-1]

    def _is_repeated_prompt(self, text):
        """Check if text contains repeated prompt patterns."""
        parts = re.split(r'[#>$%:]', text)
        # If there are multiple parts with similar text, it's likely a repeated prompt
        if len(parts) > 2:
            base_parts = [part.strip() for part in parts if part.strip()]
            if len(base_parts) > 1 and len(set(base_parts)) == 1:
                return True
        return False

    def _extract_base_prompt(self, text):
        """
        Extract a base prompt from text that might contain repetitions.
        Example: 'device# device# device#' -> 'device#'
        """
        # Find common ending characters
        for char in ['#', '>', '$', '%', ':', '~]', ']', '}', ')', '|']:
            if char in text:
                # Split by the prompt character
                parts = text.split(char)
                if len(parts) > 1:
                    # Check if the parts before the character look similar
                    base_parts = [part.strip() for part in parts[:-1]]
                    if base_parts and all(part == base_parts[0] for part in base_parts):
                        # Found a repetition pattern, return just one instance
                        return base_parts[0] + char

        # Look for repeated whitespace-separated patterns
        parts = text.split()
        if len(parts) > 1:
            # Check for repeating segments
            potential_prompts = []
            for part in parts:
                if any(part.endswith(char) for char in ['#', '>', '$', '%', ':', '~]', ']', '}', ')', '|']):
                    potential_prompts.append(part)

            # If we found multiple segments that look like prompts and they're identical
            if len(potential_prompts) > 1 and len(set(potential_prompts)) == 1:
                return potential_prompts[0]

        return None
    def _create_shell_stream(self):
        """Create interactive shell stream"""
        self._log_with_timestamp("Creating shell stream")

        if self._shell:
            self._log_with_timestamp("Shell stream already exists, reusing")
            return

        self._shell = self._ssh_client.invoke_shell()
        self._shell.settimeout(self._options.timeout)

        # Wait for the shell to initialize properly
        self._log_with_timestamp("SSHClient Message: Waiting for shell initialization (2000ms)")
        time.sleep(2)

        # Read initial shell output
        if self._shell.recv_ready():
            data = self._shell.recv(4096).decode('utf-8', errors='replace')
            self._output_buffer.write(data)
            self._options.output_callback(data)

    def execute_command(self, command):
        """Execute command on the remote device"""
        if not self._ssh_client or not self._ssh_client.get_transport() or not self._ssh_client.get_transport().is_active():
            raise RuntimeError("SSH client is not connected")

        # Only warn if using shell mode with no prompt information
        if self._options.invoke_shell and not self._options.prompt and not self._options.expect_prompt:
            self._log_with_timestamp(
                "WARNING: Executing shell command with no prompt pattern or expect prompt defined!", True)

        self._log_with_timestamp("SSHClient Message: Executing command: '{}'".format(command), True)
        start_time = time.time()

        if self._options.invoke_shell:
            # Handle multiple comma-separated commands for shell mode
            commands = command.split(',')
            result = self._execute_shell_commands(commands)
        else:
            result = self._execute_direct_command(command)

        # Wait between commands if specified
        if self._options.inter_command_time > 0:
            self._log_with_timestamp(
                "SSHClient Message: Waiting between commands: {}s".format(self._options.inter_command_time))
            time.sleep(self._options.inter_command_time)

        duration = time.time() - start_time
        self._log_with_timestamp("SSHClient Message: Command execution completed in {:.2f}ms".format(duration * 1000), True)

        return result

    def _execute_direct_command(self, command):
        """Execute command directly (non-interactive)"""
        self._log_with_timestamp("Using direct command execution mode")
        start_time = time.time()

        stdin, stdout, stderr = self._ssh_client.exec_command(
            command,
            timeout=self._options.timeout
        )

        result = stdout.read().decode('utf-8', errors='replace')
        error = stderr.read().decode('utf-8', errors='replace')

        execution_time = time.time() - start_time
        self._log_with_timestamp("Command execution took {:.2f}ms".format(execution_time * 1000))

        self._options.output_callback(result)

        if error:
            self._log_with_timestamp("Command produced error output: {}".format(error), True)
            self._options.error_callback(error)

        self._log_message(result)
        if error:
            self._log_message(error)

        return result

    def _scrub_prompt(self, raw_prompt):
        """
        Clean up a detected prompt to get just the prompt pattern without command outputs or extra whitespace.

        Args:
            raw_prompt (str): The raw detected prompt string that may contain command output

        Returns:
            str: The cleaned prompt string
        """
        self._log_with_timestamp(f"Raw detected prompt: '{raw_prompt}'")

        # Multiple approaches to extract the actual prompt:

        # 1. Try to find the last line with a prompt character
        lines = raw_prompt.strip().split('\n')
        cleaned_lines = [line.strip() for line in lines if line.strip()]

        # Look through lines in reverse to find the first one that looks like a prompt
        for line in reversed(cleaned_lines):
            # Common prompt ending characters
            if line.endswith('#') or line.endswith('>') or line.endswith('$') or line.endswith('%'):
                # Check if this is a simple prompt or contains a command
                if ' ' in line:
                    # This might be a line with both command and prompt
                    # Try to extract just the prompt part
                    parts = line.split()
                    # If the last part ends with a prompt character, it might be the prompt
                    if parts[-1][-1] in '#>$%':
                        self._log_with_timestamp(f"Extracted prompt from command line: '{parts[-1]}'")
                        return parts[-1]

                    # Otherwise, try to find the last occurrence of the prompt pattern
                    prompt_chars = ['#', '>', '$', '%']
                    for char in prompt_chars:
                        if char in line:
                            # Split by the prompt character and take the first part + the character
                            prompt_parts = line.split(char)
                            if len(prompt_parts) > 1:
                                potential_prompt = prompt_parts[0] + char
                                # Check if this looks like a valid prompt (not too long, no spaces at specific positions)
                                if len(potential_prompt) < 30 and ' ' not in potential_prompt[-15:]:
                                    self._log_with_timestamp(
                                        f"Extracted prompt by character split: '{potential_prompt}'")
                                    return potential_prompt
                else:
                    # This looks like a clean prompt
                    self._log_with_timestamp(f"Found clean prompt line: '{line}'")
                    return line

        # 2. Fallback: Try regex extraction on the whole string
        prompt_patterns = [
            r'(\S+[#>$%])\s*$',  # Basic prompt at the end of the string
            r'((?:[A-Za-z0-9_\-]+(?:\([^\)]+\))?)?[#>$%])\s*$',  # Handle context in parentheses like router(config)#
            r'(\S+@\S+[#>$%])\s*$'  # username@host style prompts
        ]

        for pattern in prompt_patterns:
            match = re.search(pattern, raw_prompt)
            if match:
                extracted = match.group(1)
                self._log_with_timestamp(f"Extracted prompt via regex: '{extracted}'")
                return extracted

        # 3. Last resort: just return the last line if it's not too long
        if cleaned_lines and len(cleaned_lines[-1]) < 50:  # Arbitrary length limit for sanity
            self._log_with_timestamp(f"Using last line as prompt: '{cleaned_lines[-1]}'")
            return cleaned_lines[-1]

        # If all else fails, return the original but warn
        self._log_with_timestamp(f"WARNING: Could not scrub prompt, using as-is: '{raw_prompt}'", True)
        return raw_prompt
    def _execute_shell_commands(self, commands):
        """Execute commands in interactive shell mode"""
        self._log_with_timestamp("Using shell mode for command execution")
        start_time = time.time()

        if not self._shell:
            self._log_with_timestamp("Shell stream not initialized, creating now")
            self._create_shell_stream()

        # Clear buffer and reset prompt detection flag
        self._output_buffer = StringIO()
        self._prompt_detected = False

        try:
            # Only process commands if there are meaningful commands to send
            has_commands = any(cmd.strip() for cmd in commands)

            if has_commands:
                # Process each command with appropriate timing
                for i, cmd in enumerate(commands):
                    # Skip empty commands to prevent unnecessary prompts
                    if not cmd.strip():
                        self._shell.send('\n')
                        continue

                    self._log_with_timestamp("Sending command {}/{}: '{}'".format(i + 1, len(commands), cmd))

                    # Send command with newline
                    self._shell.send(cmd + '\n')

                    # Wait between commands
                    if self._options.inter_command_time > 0 and i < len(commands) - 1:
                        self._log_with_timestamp("Waiting between sub-commands: {}s".format(self._options.inter_command_time))
                        time.sleep(self._options.inter_command_time)

                # If an expect prompt is set, wait for it with timeout
                if self._options.expect_prompt:
                    self._log_with_timestamp("Waiting for expect prompt: '{}'".format(self._options.expect_prompt))
                    timeout_ms = self._options.expect_prompt_timeout
                    timeout_time = time.time() + timeout_ms / 1000

                    # Read all available output
                    buffer = ""
                    prompt_detected = False

                    while time.time() < timeout_time and not prompt_detected:
                        if self._shell.recv_ready():
                            data = self._shell.recv(4096).decode('utf-8', errors='replace')
                            buffer += data
                            self._output_buffer.write(data)
                            self._options.output_callback(data)

                            if self._options.expect_prompt in buffer:
                                prompt_detected = True
                                self._log_with_timestamp("Expected prompt detected, command complete")
                                break
                        else:
                            # Small sleep to prevent CPU spinning
                            time.sleep(0.05)

                    if not prompt_detected:
                        self._log_with_timestamp("Timed out waiting for expect prompt after {}ms".format(timeout_ms), True)
                else:
                    # Fall back to the old timeout-based approach if no expect prompt
                    self._log_with_timestamp(
                        "No expect prompt defined, waiting shell timeout: {}s".format(self._options.shell_timeout))
                    time.sleep(self._options.shell_timeout)

                    # Read any remaining data
                    while self._shell.recv_ready():
                        data = self._shell.recv(4096).decode('utf-8', errors='replace')
                        self._output_buffer.write(data)
                        self._options.output_callback(data)

                self._log_with_timestamp("Shell command execution completed")
            else:
                self._log_with_timestamp("No commands to execute, skipping shell command execution")

        except Exception as e:
            error_message = "Error during shell execution: {}".format(str(e))
            self._log_with_timestamp(error_message, True)

            self._log_message(error_message)
            self._options.error_callback(error_message)

        total_time = time.time() - start_time
        self._log_with_timestamp("Total shell command execution time: {:.2f}ms".format(total_time * 1000))

        # Return the accumulated output buffer content
        return self._output_buffer.getvalue()

    def set_expect_prompt(self, prompt_string):
        """Set the expected prompt string"""
        if prompt_string:
            self._options.expect_prompt = prompt_string
            self._log_with_timestamp("Expect prompt set to: '{}'".format(prompt_string), True)

    def disconnect(self):
        """Disconnect from the remote device"""
        self._log_with_timestamp("Disconnecting from device")

        try:
            if self._shell:
                self._shell.close()
                self._shell = None

            if self._ssh_client:
                self._ssh_client.close()

            self._log_with_timestamp("Successfully disconnected")
        except Exception as e:
            self._log_with_timestamp("Error during disconnect: {}".format(str(e)), True)

    def _log_message(self, message):
        """Log message to file if log file is specified"""
        if not self._options.log_file:
            return

        try:
            # Ensure directory exists
            log_dir = os.path.dirname(self._options.log_file)
            if log_dir and not os.path.exists(log_dir):
                os.makedirs(log_dir)

            with open(self._options.log_file, 'a') as f:
                f.write(message + '\n')
                f.flush()
        except Exception as e:
            self._options.error_callback("Error writing to log file: {}".format(str(e)))