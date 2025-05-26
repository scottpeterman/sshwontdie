const { Client } = require('ssh2');

function handleShellPrompt(stream, commands, prompt, promptCount, conn) {
    let buffer = '';
    let commandIndex = 0;
    let promptSeenCount = 0;
    let allCommandsSent = false;
    let isClosingConnection = false; // New flag to prevent multiple closure attempts

    function sendNextCommand() {
        if (commandIndex < commands.length) {
            const commandToSend = commands[commandIndex].trim();
            if (commandToSend) {
                // console.log(`Sending command: ${commandToSend}`);
                stream.write(commandToSend + '\n');
            } else {
                // console.log(`Sending newline for empty command`);
                stream.write('\n');
            }
            commandIndex++;
            if (commandIndex >= commands.length) {
                allCommandsSent = true;
            }
        }
    }

    stream.on('data', (data) => {
        if (isClosingConnection) return; // Skip processing if closing connection

        const dataStr = data.toString();
        // console.log(`Received data: ${dataStr}`);
        buffer += dataStr;

        if (buffer.includes(prompt)) {
            // console.log(`Prompt detected: ${prompt}`);
            promptSeenCount++;
            buffer = '';

            if (!allCommandsSent) {
                sendNextCommand();
            } else if (allCommandsSent && promptSeenCount >= promptCount && !isClosingConnection) {
                console.log('Closing connection, expected prompt count reached');
                isClosingConnection = true;
                stream.end();
                conn.end();
            }
        }
    });

    // Send the first command immediately
    sendNextCommand();
}


async function runSSHCommand({ host, user, password, cmds, invokeShell, prompt, promptCount, timeoutDuration }) {
    return new Promise((resolve, reject) => {
        const conn = new Client();
        let output = '';

        conn.on('ready', () => {
            console.log('Client :: ready');
            if (invokeShell) {
                conn.shell((err, stream) => {
                    if (err) {
                        console.error('Shell error:', err);
                        reject(err);
                    }

                    handleShellPrompt(stream, cmds.split(','), prompt, promptCount, conn);

                    stream.on('close', () => {
                        // console.log('Stream :: close');
                        clearTimeout(timeout);
                        resolve(output);
                    }).on('data', (data) => {
                        output += data;
                    });
                });
            } else {
                // Exec mode
                conn.exec(cmds, (err, stream) => {
                    if (err) {
                        console.error('Exec error:', err);
                        reject(err);
                    }

                    stream.on('close', () => {
                        // console.log('Stream :: close');
                        clearTimeout(timeout);
                        resolve(output);
                    }).on('data', (data) => {
                        output += data;
                    });
                });
            }
        }).on('error', (err) => {
            console.error('SSH Connection Error:', err);
            reject(err);
        }).connect({
            host,
            port: 22,
            username: user,
            password,
            algorithms: {
                                kex: [
                                    'ecdh-sha2-nistp256', // More secure, modern algorithm
                                    'diffie-hellman-group1-sha1', // Older, less secure algorithm
                                    // ... other algorithms
                                ],
                                cipher: [
                                    'aes128-ctr', // More secure, modern cipher
                                    'aes128-cbc', // Older, less secure cipher
                                    // ... other ciphers
                                ],
                                // ... other categories
                            },
        });

        const timeout = setTimeout(() => {
            console.log('Disconnected due to timeout, consider adjusting prompt count');
            conn.end(); // End the SSH connection
            //reject(new Error('Disconnected due to timeout, consider adjusting prompt count'));
        }, timeoutDuration * 1000);
    });
}

module.exports = runSSHCommand;
