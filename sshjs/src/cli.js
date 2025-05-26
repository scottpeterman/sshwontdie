const yargs = require('yargs');
const runSSHCommand = require('./sshClient');

yargs
  .scriptName("ssh-client")
  .usage('$0 <cmd> [args]')
  .command('run [options]', 'Run SSH command', (yargs) => {
    yargs
      .option('host', {
        describe: 'SSH Host (ip:port)',
        type: 'string',
        demandOption: true
      })
      .option('user', {
        describe: 'SSH Username',
        type: 'string',
        demandOption: true
      })
      .option('password', {
        describe: 'SSH Password',
        type: 'string',
        demandOption: true
      })
      .option('cmds', {
        describe: 'Commands to run, separated by comma',
        type: 'string',
        default: ''
      })
      .option('invoke-shell', {
        describe: 'Invoke shell before running the command',
        type: 'boolean',
        default: false
      })
      .option('prompt', {
        describe: 'Prompt to look for before breaking the shell',
        type: 'string',
        default: ''
      })
      .option('prompt-count', {
        describe: 'Number of prompts to look for before breaking the shell',
        type: 'number',
        default: 1
      })
      .option('timeout', {
        describe: 'Command timeout duration in seconds',
        type: 'number',
        default: 5
      });
  }, async (argv) => {
    try {
      const output = await runSSHCommand({
        host: argv.host,
        user: argv.user,
        password: argv.password,
        cmds: argv.cmds,
        invokeShell: argv['invoke-shell'],
        prompt: argv.prompt,
        promptCount: argv['prompt-count'],
        timeoutDuration: argv.timeout
      });
      console.log('SSH Command Output:', output);
    } catch (error) {
      console.error('SSH Command Error:', error);
      process.exit(1);
    }
  })
  .help()
  .argv;
