/**
 * HapticFeedback - Provides haptic feedback using Vibration API with graceful fallback.
 * iOS Safari does not support Vibration API, so feedback is silent on those devices.
 */
export class HapticFeedback {
    constructor() {
        this.isSupported = typeof navigator.vibrate === 'function';
    }

    /**
     * Short tap feedback for button presses and touch events.
     */
    tap() {
        if (this.isSupported) {
            navigator.vibrate(10);
        }
    }

    /**
     * Success feedback - two short pulses.
     */
    success() {
        if (this.isSupported) {
            navigator.vibrate([50, 50, 50]);
        }
    }

    /**
     * Error feedback - one long pulse.
     */
    error() {
        if (this.isSupported) {
            navigator.vibrate(200);
        }
    }

    /**
     * Warning feedback - three short pulses.
     */
    warning() {
        if (this.isSupported) {
            navigator.vibrate([30, 30, 30, 30, 30]);
        }
    }

    /**
     * Progress feedback - single short pulse.
     */
    progress() {
        if (this.isSupported) {
            navigator.vibrate(20);
        }
    }

    /**
     * Custom vibration pattern.
     * @param {number|number[]} pattern - Duration in ms or array of [vibrate, pause, vibrate, ...]
     */
    custom(pattern) {
        if (this.isSupported) {
            navigator.vibrate(pattern);
        }
    }

    /**
     * Cancel ongoing vibration.
     */
    cancel() {
        if (this.isSupported) {
            navigator.vibrate(0);
        }
    }
}
