Sure, here's a more detailed `README.md` that includes additional information about the app internals, including how prompts and commands are handled:

```markdown
# sshpassj

sshpassj is a Java-based SSH client application that connects to a specified host, executes commands, and logs the output. It supports both command execution and interactive shell sessions.

## Features

- Connects to SSH hosts using username and password authentication.
- Executes a series of commands and logs the output.
- Supports invoking an interactive shell session.
- Configurable command prompts and timeouts.
- Logs output to a specified file.

## Requirements

- Java 8 or higher
- Visual Studio Code or IntelliJ IDEA (optional, for development)
- GraalVM for building native images

## Setup

### Visual Studio Code

1. **Install Extensions**:
    - Java Extension Pack
    - GraalVM Extension Pack (optional, for native image support)

2. **Configure Java Environment**:
    - Ensure that Java is installed and configured in your PATH.
    - Optionally, configure GraalVM if you plan to build a native image.

### Project Structure

```
sshpassj/
│
├── src/
│   └── main/
│       └── java/
│           └── com/
│               └── scottpeterman/
│                   └── sshpassj/
│                       └── sshpassj.java
│                       └── CustomConfig.java
│                       └── RejectAllHostKeyVerifier.java
├── log/
│   └── ssh_output.log
├── META-INF/
│   └── native-image/
│       └── reflect-config.json
│       └── resource-config.json
│       └── proxy-config.json
│       └── jni-config.json
└── README.md
```

### Clone the Repository

```bash
git clone <repository-url>
cd sshpassj
```

## Build and Run

### Compile the Application

```bash
javac -d out src/main/java/com/scottpeterman/sshpassj/*.java
```

### Run the Application

```bash
java -cp out com.scottpeterman.sshpassj.sshpassjostname <host> -u <user> -p <password> -c "your,commands,here"
```

Replace `<host>`, `<user>`, `<password>`, and `"your,commands,here"` with appropriate values.

### Building a Native Image with GraalVM

1. **Install GraalVM**:
    - Download and install GraalVM from [GraalVM Releases](https://www.graalvm.org/downloads/).
    - Set up GraalVM as your Java environment:

    ```bash
    export JAVA_HOME=/path/to/graalvm
    export PATH=$JAVA_HOME/bin:$PATH
    ```

2. **Run with Native Image Agent**:

    ```bash
    java -agentlib:native-image-agent=config-output-dir=./META-INF/native-image -cp out com.scottpeterman.sshpassj.sshpassjostname <host> -u <user> -p <password> -c "your,commands,here"
    ```

3. **Build Native Image**:

    ```bash
    native-image --no-fallback --initialize-at-build-time --no-server --enable-http --enable-https -H:ConfigurationFileDirectories=META-INF/native-image -cp out com.scottpeterman.sshpassj.sshpassj ```

4. **Run Native Image**:

    ```bash
    ./com.scottpeterman.sshpassj.sshpassj --hostname <host> -u <user> -p <password> -c "your,commands,here"
    ```

## Application Internals

### Command Line Arguments

- `--hostname <host>`: SSH Host (ip:port)
- `-u, --user <user>`: SSH Username
- `-p, --password <password>`: SSH Password
- `-c, --cmds <commands>`: Commands to run, separated by comma
- `--invoke-shell`: Invoke shell before running the command [default=False]
- `--prompt <prompt>`: Prompt to look for before breaking the shell [default=#]
- `--prompt-count <count>`: Number of prompts to look for before breaking the shell [default=1]
- `-t, --timeout <seconds>`: Command timeout duration in seconds [default=360]
- `--disable-auto-add-policy`: Disable automatically adding the host key [default=False]
- `--look-for-keys`: Look for local SSH key [default=False]
- `-i, --inter-command-time`: Inter-command time in seconds [default=1]

### Prompt and Command Handling

#### Command Execution

The application can execute commands either in a direct session or by invoking an interactive shell.

1. **Direct Session Command Execution**:
    - Commands are executed directly within the session.
    - The output is read line-by-line and added to the output buffer.
    - Each line is logged to a file specified (`ssh_output.log`).

2. **Interactive Shell Session**:
    - The shell is invoked, and commands are written to the shell.
    - Output is read in a separate thread and synchronized with a blocking queue to handle prompts.
    - The prompt and prompt count are used to determine when to exit the shell.

#### Example Code Snippet for Command Execution

```java
if (invokeShell) {
    try (Session session = client.startSession()) {
        Session.Shell shell = session.startShell();
        OutputStreamWriter writer = new OutputStreamWriter(shell.getOutputStream());
        BufferedReader reader = new BufferedReader(new InputStreamReader(shell.getInputStream()));

        final BlockingQueue<String> outputQueue = new LinkedBlockingQueue<>();
        final Thread readThread = new Thread(() -> readOutput(reader, outputQueue, prompt, promptCount, fileWriter));
        readThread.start();

        final String[] commandArray = cmds.split(",", -1);
        for (final String cmd : commandArray) {
            writer.write((cmd.trim().isEmpty() ? "" : cmd.trim()) + "\n");
            writer.flush();
            Thread.sleep(interCommandTime * 1000);
        }

        final String reason = outputQueue.poll(timeout, TimeUnit.SECONDS);
        if (reason != null) {
            log("INFO: Exiting: " + reason, fileWriter);
        } else {
            log("INFO: Exiting due to timeout.", fileWriter);
        }

        shell.close();
    }
} else {
    final String[] commandArray = cmds.split(",", -1);
    for (final String cmd : commandArray) {
        if (!cmd.trim().isEmpty()) {
            try (Session session = client.startSession()) {
                Session.Command command = session.exec(cmd.trim());
                BufferedReader reader = new BufferedReader(new InputStreamReader(command.getInputStream()));

                reader.lines().forEach(line -> {
                    outputBuffer.add(line);
                    try {
                        fileWriter.write(line + System.lineSeparator());
                        fileWriter.flush();
                    } catch (IOException e) {
                        log("SEVERE: Error writing to file: " + e.getMessage(), fileWriter);
                    }
                });
            }
        }
    }
}
```

### Logging

- The application logs output to `./log/ssh_output.log`.
- Ensure the `log` directory exists before running the application.

### Configuration Handling

The `CustomConfig` class allows for additional SSH configuration options. The `RejectAllHostKeyVerifier` class is used to handle host key verification if `disableAutoAddPolicy` is set to `true`.

### Example

```bash
java -cp out com.scottpeterman.sshpassj.sshpassjostname 192.168.1.1 -u admin -p admin -c "show version,show ip interface brief"
```

## Troubleshooting

- **Connection Issues**: Ensure the hostname, username, and password are correct.
- **Timeouts**: Increase the timeout value if commands take longer to execute.
- **GraalVM Issues**: Ensure all necessary configurations are included in the `META-INF/native-image` directory.

## License

This project is licensed under the MIT License.

## Acknowledgments

- [SSHJ Library](https://github.com/hierynomus/sshj) for SSH connections.
- [GraalVM](https://www.graalvm.org/) for native image support.
```

This `README.md` includes detailed instructions for setting up, building, and running your SSH client application, along with a more comprehensive explanation of the application internals, specifically how commands and prompts are handled.