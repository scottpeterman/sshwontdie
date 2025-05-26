

# Go SSH Utility

A Go-based utility for running SSH commands on remote machines. This utility provides flexibility for both executing a command (`exec` mode) and invoking a shell (`shell` mode) on the remote system. Current build is gsshpass.exe for windows.

## Usage

The basic usage of the utility is as follows:

```bash
gsshpass.exe -h [HOST:PORT] -u [USERNAME] -p [PASSWORD] -c [COMMANDS] [FLAGS]
```

## Flags

Here is an explanation of the flags:

### Required Flags

- `-h` : SSH Host (ip:port)  
  Specifies the hostname and port of the remote machine. Example: `-h "192.168.1.1:22"`

- `-u` : SSH Username  
  Specifies the username for the SSH connection. Example: `-u "root"`

- `-p` : SSH Password  
  Specifies the password for the SSH connection. Example: `-p "password123"`

- `-c` : Commands to Run  
  Specifies the command or series of commands to be run on the remote machine. Multiple commands should be separated by commas. Example: `-c "ls -al,df -h"`

### Optional Flags

- `--invoke-shell` : Invoke Shell  
  When set, this flag invokes a shell session on the remote system before executing the command(s). Default is `false`.

- `--prompt` : Prompt  
  Specifies the shell prompt to look for before breaking the shell in `shell` mode. Useful for matching the shell prompt of different systems. Example: `--prompt "#"`

- `--prompt-count` : Prompt Count  
  Specifies the number of times the prompt should be seen before breaking the shell in `shell` mode. Default is `1`. Example: `--prompt-count 3`

- `-t` : Timeout Duration  
  Sets the timeout duration for running the command(s) in `shell` mode, in seconds. Default is `5`. Example: `-t 10`

## Examples

Execute a command without invoking a shell:
```bash
gsshpass -h "192.168.1.1:22" -u "root" -p "password123" -c "lsb_release -a"
```

Execute a series of commands by first invoking a shell, and break when seeing the `#` prompt three times:
```bash
gsshpass -h "192.168.1.1:22" -u "root" -p "password123" -c "ls -al,df -h" --invoke-shell --prompt "#" --prompt-count 3
```

---
