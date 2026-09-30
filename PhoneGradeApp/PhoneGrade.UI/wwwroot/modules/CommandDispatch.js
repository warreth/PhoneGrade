/**
 * Runs the one command the desktop can ask the phone to do.
 *
 * The phone polls the desktop for status every two seconds and the answer
 * carries a command when there is one. That poll is the whole channel: the
 * desktop publishes the address before this page has been opened, so a push
 * would have nowhere to go.
 *
 * Returns the name of what ran, or null when there was nothing to do, which is
 * what lets a caller tell "no command" from "a command it did not understand".
 */
export function runCommand(command, runner) {
    if (!command || typeof command !== 'object') return null;

    switch (command.type) {
        case 'auto_start_suite':
            if (!runner) return null;
            runner.startSuite();
            return command.type;

        case 'test_start':
            if (!runner || !command.testId) return null;
            runner.startTest(command.testId);
            return command.type;

        case 'stop_suite':
            if (!runner) return null;
            runner.stopSuite();
            return command.type;

        default:
            return null;
    }
}
