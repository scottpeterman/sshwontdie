package com.scottpeterman.sshpassj;

import net.schmizz.sshj.SSHClient;
import net.schmizz.sshj.connection.ConnectionException;
import net.schmizz.sshj.connection.channel.direct.Session;
import net.schmizz.sshj.transport.verification.HostKeyVerifier;
import net.schmizz.sshj.transport.verification.PromiscuousVerifier;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.security.PublicKey;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

public class sshpassj implements Runnable {

    private String host;
    private String user;
    private String password;
    private String cmds = "";
    private boolean invokeShell = false;
    private String prompt = "#";
    private int promptCount = 1;
    private int timeout = 360;
    private boolean disableAutoAddPolicy = false;
    private boolean lookForKeys = false;
    private int interCommandTime = 1;

    private final List<String> outputBuffer = Collections.synchronizedList(new ArrayList<>());

    public static void main(String[] args) {
        sshpassj app = new sshpassj();
        if (app.parseArguments(args)) {
            app.run();
        } else {
            app.printUsage();
        }
    }

    private boolean parseArguments(String[] args) {
        for (int i = 0; i < args.length; i++) {
            switch (args[i]) {
                case "--hostname":
                    if (i + 1 < args.length) {
                        host = args[++i];
                    } else {
                        return false;
                    }
                    break;
                case "-u":
                case "--user":
                    if (i + 1 < args.length) {
                        user = args[++i];
                    } else {
                        return false;
                    }
                    break;
                case "-p":
                case "--password":
                    if (i + 1 < args.length) {
                        password = args[++i];
                    } else {
                        return false;
                    }
                    break;
                case "-c":
                case "--cmds":
                    if (i + 1 < args.length) {
                        cmds = args[++i];
                    } else {
                        return false;
                    }
                    break;
                case "--invoke-shell":
                    invokeShell = true;
                    break;
                case "--prompt":
                    if (i + 1 < args.length) {
                        prompt = args[++i];
                    } else {
                        return false;
                    }
                    break;
                case "--prompt-count":
                    if (i + 1 < args.length) {
                        promptCount = Integer.parseInt(args[++i]);
                    } else {
                        return false;
                    }
                    break;
                case "-t":
                case "--timeout":
                    if (i + 1 < args.length) {
                        timeout = Integer.parseInt(args[++i]);
                    } else {
                        return false;
                    }
                    break;
                case "--disable-auto-add-policy":
                    disableAutoAddPolicy = true;
                    break;
                case "--look-for-keys":
                    lookForKeys = true;
                    break;
                case "-i":
                case "--inter-command-time":
                    if (i + 1 < args.length) {
                        interCommandTime = Integer.parseInt(args[++i]);
                    } else {
                        return false;
                    }
                    break;
                default:
                    System.err.println("Unknown option: " + args[i]);
                    return false;
            }
        }

        return host != null && user != null && password != null;
    }

    private void printUsage() {
        System.out.println("Usage: java -jar sshpassj.jar --hostname <host> -u <user> -p <password> [options]");
        System.out.println("Options:");
        System.out.println("  --hostname <host>          SSH Host (ip:port)");
        System.out.println("  -u, --user <user>          SSH Username");
        System.out.println("  -p, --password <password>  SSH Password");
        System.out.println("  -c, --cmds <commands>      Commands to run, separated by comma");
        System.out.println("  --invoke-shell             Invoke shell before running the command [default=False]");
        System.out.println("  --prompt <prompt>          Prompt to look for before breaking the shell [default=#]");
        System.out.println("  --prompt-count <count>     Number of prompts to look for before breaking the shell [default=1]");
        System.out.println("  -t, --timeout <seconds>    Command timeout duration in seconds [default=360]");
        System.out.println("  --disable-auto-add-policy  Disable automatically adding the host key [default=False]");
        System.out.println("  --look-for-keys            Look for local SSH key [default=False]");
        System.out.println("  -i, --inter-command-time   Inter-command time in seconds [default=1]");
    }

    @Override
    public void run() {
        SSHClient client = new SSHClient(new CustomConfig());
        FileWriter fileWriter = null;

        try {
            File logFile = new File("./log/ssh_output.log");
            fileWriter = new FileWriter(logFile, true);

            if (disableAutoAddPolicy) {
                client.loadKnownHosts();
                client.addHostKeyVerifier(new RejectAllHostKeyVerifier());
            } else {
                client.addHostKeyVerifier(new PromiscuousVerifier());
            }

            System.out.println("Connecting to " + host);
            client.connect(host);
            System.out.println("Authenticating as " + user);
            client.authPassword(user, password);

            if (invokeShell) {
                try (Session session = client.startSession()) {
                    Session.Shell shell = session.startShell();
                    OutputStreamWriter writer = new OutputStreamWriter(shell.getOutputStream());
                    BufferedReader reader = new BufferedReader(new InputStreamReader(shell.getInputStream()));

                    final FileWriter finalFileWriter = fileWriter;  // Make fileWriter effectively final
                    final BlockingQueue<String> outputQueue = new LinkedBlockingQueue<>();
                    final Thread readThread = new Thread(() -> readOutput(reader, outputQueue, prompt, promptCount, finalFileWriter));
                    readThread.start();

                    final String[] commandArray = cmds.split(",", -1);
                    for (final String cmd : commandArray) {
                        writer.write((cmd.trim().isEmpty() ? "" : cmd.trim()) + "\n");
                        writer.flush();
                        System.out.println("Sent command: " + cmd.trim()); // Debug logging
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

                            final FileWriter finalFileWriter = fileWriter;  // Make fileWriter effectively final
                            reader.lines().forEach(line -> {
                                outputBuffer.add(line);
                                try {
                                    finalFileWriter.write(line + System.lineSeparator());
                                    finalFileWriter.flush(); // Ensure output is written immediately
                                    System.out.println(line);  // Print command output
                                } catch (IOException e) {
                                    log("SEVERE: Error writing to file: " + e.getMessage(), finalFileWriter);
                                }
                            });
                        }
                    }
                }
            }

            client.disconnect();
            writeLogFile(fileWriter);
        } catch (ConnectionException e) {
            if (!e.getMessage().contains("EOF")) {
                log("WARNING: SSH Connection error: " + e.getMessage(), fileWriter);
                System.err.println("SSH Connection error: " + e.getMessage());
            }
        } catch (Exception e) {
            log("SEVERE: Error: " + e.getMessage(), fileWriter);
            System.err.println("Error: " + e.getMessage());
        } finally {
            try {
                if (client != null) {
                    client.close();
                }
                if (fileWriter != null) {
                    fileWriter.close();
                }
            } catch (IOException e) {
                log("SEVERE: Error closing SSH client or file writer: " + e.getMessage(), fileWriter);
                System.err.println("Error closing SSH client or file writer: " + e.getMessage());
            }
        }
    }

    private void readOutput(BufferedReader reader, BlockingQueue<String> outputQueue, String prompt, int promptCount, FileWriter fileWriter) {
        int counter = 0;
        try {
            String line;
            while ((line = reader.readLine()) != null) {
                outputBuffer.add(line);
                fileWriter.write(line + System.lineSeparator());
                fileWriter.flush();  // Ensure output is written immediately
                System.out.println("Received line: " + line); // Debug logging

                if (line.contains(prompt)) {
                    counter++;
                    if (counter >= promptCount) {
                        outputQueue.put("Prompt detected.");
                        return;
                    }
                }
            }
            outputQueue.put("Channel closed.");
        } catch (ConnectionException e) {
            if (!e.getMessage().contains("EOF")) {
                log("WARNING: SSH Connection error: " + e.getMessage(), fileWriter);
                System.err.println("SSH Connection error: " + e.getMessage());
            }
        } catch (IOException e) {
            if (!e.getMessage().contains("EOF")) {
                log("SEVERE: Error reading output: " + e.getMessage(), fileWriter);
                System.err.println("Error reading output: " + e.getMessage());
            }
        } catch (Exception e) {
            log("SEVERE: Unexpected error: " + e.getMessage(), fileWriter);
            System.err.println("Unexpected error: " + e.getMessage());
        } finally {
            try {
                reader.close();
            } catch (IOException e) {
                log("SEVERE: Error closing reader: " + e.getMessage(), fileWriter);
                System.err.println("Error closing reader: " + e.getMessage());
            }
        }
    }

    private void log(String message, FileWriter fileWriter) {
        if (!message.contains("EOF")) {
            System.out.println(message);
            try {
                fileWriter.write(message + System.lineSeparator());
                fileWriter.flush(); // Ensure log is written immediately
            } catch (IOException e) {
                System.err.println("SEVERE: Error writing to log file: " + e.getMessage());
            }
        }
    }

    private void writeLogFile(FileWriter fileWriter) {
        synchronized (outputBuffer) {
            try {
                for (String line : outputBuffer) {
                    fileWriter.write(line + System.lineSeparator());
                    fileWriter.flush(); // Ensure log is written immediately
                }
            } catch (IOException e) {
                log("SEVERE: Error writing to log file: " + e.getMessage(), fileWriter);
                System.err.println("Error writing to log file: " + e.getMessage());
            }
        }
    }

    private static class RejectAllHostKeyVerifier implements HostKeyVerifier {
        @Override
        public boolean verify(String hostname, int port, PublicKey key) {
            return false;
        }

        @Override
        public List<String> findExistingAlgorithms(String hostname, int port) {
            return Collections.emptyList();
        }
    }
}
