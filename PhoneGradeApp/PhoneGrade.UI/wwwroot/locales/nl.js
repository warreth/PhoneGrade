/**
 * Everything the phone says, in Dutch: the language the product ships in and
 * the fallback when a browser asks for a language this app does not carry.
 *
 * Keys are the path to where the text appears, `shell` for the frame around the
 * tests and `results` for the screen after them, so a key missing from one
 * language stands out next to its neighbour in the other.
 */
export default {
    'shell.title': 'PhoneGrade Testsuite',
    'shell.connecting': 'Verbinden...',
    'shell.iosBanner': 'Voor een volledig scherm: tik op Delen en kies "Toevoegen aan startscherm".',
    'shell.welcome': 'Welkom bij PhoneGrade',
    'shell.subtitle': 'Professionele hardwaretest',
    'shell.device': 'Toestel:',
    'shell.browser': 'Browser:',
    'shell.detecting': 'Detecteren...',
    'shell.willVerify': 'Deze testsuite controleert:',
    'shell.featureTouch': 'Touchscreenreactie',
    'shell.featureDisplay': 'Beeldschermkwaliteit',
    'shell.featureAudio': 'Audio',
    'shell.featureCameras': "Camera's",
    'shell.featureSensors': 'Bewegingssensoren',
    'shell.resume': 'Doorgaan',
    'shell.start': 'Testsuite starten',
    'shell.disclaimer': 'Alle tests zijn niet-destructief en veilig uit te voeren.',
    'shell.loadingTest': 'Test laden...',
    'shell.pleaseWait': 'Even geduld...',
    'shell.skipTest': 'Test overslaan',

    'aria.welcomeScreen': 'Welkomstscherm',
    'aria.startTest': 'Testsuite starten',
    'aria.testScreen': 'Testscherm',
    'aria.testContainer': 'Interface van de huidige test',
    'aria.skipTest': 'Deze test overslaan',
    'aria.resultsScreen': 'Testresultaten',
    'aria.restart': 'Testsuite opnieuw draaien',

    'results.title': 'Testsuite voltooid',
    'results.tests': 'Tests',
    'results.passed': 'Geslaagd',
    'results.failed': 'Mislukt',
    'results.skipped': 'Overgeslagen',
    'results.runAgain': 'Opnieuw draaien',
    'results.retry': 'Opnieuw',

    'touch.touchCount': 'Aangeraakt: {touched}/{cells}',
    'location.accuracy': 'Nauwkeurigheid: {metres} m'
};
