package main

import (
	"bufio"
	"flag"
	"fmt"

	"golang.org/x/crypto/ssh"

	// "golang.org/x/term"
	"log"
	"os"
	"strings"
	"time"
)

func main() {
	host := flag.String("h", "", "SSH Host (ip:port)")
	user := flag.String("u", "", "SSH Username")
	password := flag.String("p", "", "SSH Password")
	cmds := flag.String("c", "", "Commands to run, separated by comma")
	invokeShell := flag.Bool("invoke-shell", false, "Invoke shell before running the command")
	prompt := flag.String("prompt", "", "Prompt to look for before breaking the shell")
	promptCount := flag.Int("prompt-count", 1, "Number of prompts to look for before breaking the shell")
	timeoutDuration := flag.Int("t", 5, "Command timeout duration in seconds")

	flag.Parse()

	// add keyboard ineractive support:
	config := &ssh.ClientConfig{
		User: *user,
		Auth: []ssh.AuthMethod{
			// Try password authentication first.
			ssh.Password(*password),
			// Fallback to keyboard-interactive authentication.
			ssh.KeyboardInteractive(func(user, instruction string, questions []string, echos []bool) ([]string, error) {
				fmt.Println("KeyboardInteractive...")
				answers := make([]string, len(questions))
				reader := bufio.NewReader(os.Stdin)

				fmt.Println(instruction) // Print any instruction given by the server.

				for i, question := range questions {
					fmt.Println("Question:")
					fmt.Print(question) // Print the question to the user.

					if !echos[i] {
						// If the echo is false, it's likely asking for a password, so we should not echo the input.
						// answerBytes, err := term.ReadPassword(int(os.Stdin.Fd()))

						if strings.Contains(strings.ToLower(question), "password") {
							answers[i] = *password
						}
					} else {
						// For other inputs, it's okay to echo what the user types.
						answer, err := reader.ReadString('\n')
						if err != nil {
							return nil, err
						}
						answers[i] = strings.TrimSpace(answer)
					}
				}
				// fmt.Println("Answers:")
				// fmt.Println(answers)
				return answers, nil
			}),
		},
		HostKeyCallback: ssh.InsecureIgnoreHostKey(),
		Timeout:         time.Duration(*timeoutDuration) * time.Second,
		ClientVersion:   "SSH-2.0-Go",
		Config: ssh.Config{
			Ciphers: []string{
				"aes128-ctr",
				"aes192-ctr",
				"aes256-ctr",
				"aes128-gcm@openssh.com",
				"aes256-gcm@openssh.com",
				"chacha20-poly1305@openssh.com", // Include this if supported by your Go SSH library
				"aes128-cbc",                    // if this is first, Palo pukes

			},
			KeyExchanges: []string{
				"curve25519-sha256@libssh.org",
				"ecdh-sha2-nistp256",
				"ecdh-sha2-nistp384",
				"ecdh-sha2-nistp521",
				"diffie-hellman-group-exchange-sha256",
				"diffie-hellman-group16-sha512",
				"diffie-hellman-group18-sha512",
				"diffie-hellman-group14-sha256",
				"diffie-hellman-group14-sha1",
				"diffie-hellman-group1-sha1", // add this to allow the insecure algorithm
				// ... (rest of your key exchange algorithms)
			},
		},
		// Other configurations...
	}

	client, err := ssh.Dial("tcp", *host, config)
	if err != nil {
		log.Fatalf("Failed to dial: %s", err)
	}

	session, err := client.NewSession()
	if err != nil {
		log.Fatalf("Failed to create session: %s", err)
	}
	defer session.Close()

	if *invokeShell {
		// Shell-invoking logic here
		if err := session.RequestPty("vt100", 80, 120, ssh.TerminalModes{}); err != nil {
			log.Fatalf("Request for pseudo terminal failed: %s", err)
		}

		stdoutPipe, _ := session.StdoutPipe()
		stdinPipe, _ := session.StdinPipe()
		reader := bufio.NewReader(stdoutPipe)
		done := make(chan bool)
		counter := 0

		go func() {
			for {
				line, _ := reader.ReadString('\n')
				fmt.Print(line)
				if strings.Contains(line, *prompt) {
					counter++
					if counter >= *promptCount {
						done <- true
						break
					}
				}
			}
		}()

		if err := session.Shell(); err != nil {
			log.Fatalf("Failed to start shell: %s", err)
		}

		commands := strings.Split(*cmds, ",")
		for _, command := range commands {
			fmt.Fprintf(stdinPipe, "%s\n", command)
			time.Sleep(1 * time.Second)
		}

		select {
		case <-done:
			fmt.Println("Exiting due to prompt.")
		case <-time.After(time.Duration(*timeoutDuration) * time.Second):
			fmt.Println("Exiting due to seconds timeout.")
		}
	} else {
		// Exec-only logic here
		out, err := session.CombinedOutput(*cmds)
		if err != nil {
			log.Fatalf("Failed to run command: %s", err)
		}
		fmt.Println(string(out))
	}
}
