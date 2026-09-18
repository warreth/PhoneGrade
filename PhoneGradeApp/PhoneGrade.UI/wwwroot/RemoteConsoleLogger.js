// RemoteConsoleLogger.js
// Intercepts client-side console.log/warn/error and unhandled rejections
// Sends them over WebSocket to the desktop Logs tab in real-time

class RemoteConsoleLogger {
    constructor(wsClient) {
        this.wsClient = wsClient;
        this.setupInterceptors();
    }

    setupInterceptors() {
        // Store original console methods
        const originalLog = console.log;
        const originalWarn = console.warn;
        const originalError = console.error;

        // Override console.log
        console.log = (...args) => {
            originalLog.apply(console, args);
            this.sendLog('info', args);
        };

        // Override console.warn
        console.warn = (...args) => {
            originalWarn.apply(console, args);
            this.sendLog('warning', args);
        };

        // Override console.error
        console.error = (...args) => {
            originalError.apply(console, args);
            this.sendLog('error', args);
        };

        // Capture unhandled promise rejections
        window.addEventListener('unhandledrejection', (event) => {
            this.sendLog('error', ['Unhandled Promise Rejection:', event.reason]);
        });

        // Capture uncaught errors
        window.addEventListener('error', (event) => {
            this.sendLog('error', ['Uncaught Error:', event.message, event.error]);
        });
    }

    sendLog(level, args) {
        try {
            const message = this.formatMessage(args);
            if (!this.wsClient.isConnected()) {
                // Queue if not connected (will be sent when connection restored)
                return;
            }

            const logEvent = {
                type: 'log_event',
                sessionId: this.wsClient.sessionId,
                level: level,
                source: 'PwaClient',
                message: message,
                timestamp: new Date().toISOString()
            };

            this.wsClient.send(logEvent);
        } catch (e) {
            // Never crash the logger itself
            originalError.call(console, 'RemoteConsoleLogger error:', e);
        }
    }

    formatMessage(args) {
        return args.map(arg => {
            if (typeof arg === 'object') {
                try {
                    return JSON.stringify(arg);
                } catch {
                    return String(arg);
                }
            }
            return String(arg);
        }).join(' ');
    }
}

export { RemoteConsoleLogger };
