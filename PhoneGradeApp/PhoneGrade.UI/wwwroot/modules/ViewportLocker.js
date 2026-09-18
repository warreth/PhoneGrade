/**
 * ViewportLocker - Prevents zoom, pan, scroll, and accidental gestures during hardware tests.
 * Locks the viewport globally and restores normal behavior when unlocked.
 */
export class ViewportLocker {
    constructor() {
        this.isLocked = false;
        this.originalOverflow = null;
        this.originalTouchAction = null;
        this.preventGestureHandler = null;
    }

    /**
     * Lock the viewport to prevent zoom, pan, scroll.
     */
    lock() {
        if (this.isLocked) return;
        
        this.isLocked = true;
        
        // Store original body styles
        this.originalOverflow = document.body.style.overflow;
        this.originalTouchAction = document.body.style.touchAction;
        
        // Lock body scroll and touch
        document.body.style.overflow = 'hidden';
        document.body.style.touchAction = 'none';
        document.body.style.position = 'fixed';
        document.body.style.width = '100%';
        document.body.style.height = '100%';
        
        // Prevent default touch gestures globally
        this.preventGestureHandler = (e) => {
            if (e.touches && e.touches.length > 1) {
                e.preventDefault();
            }
        };
        
        document.addEventListener('gesturestart', this.preventGesture, { passive: false });
        document.addEventListener('gesturechange', this.preventGesture, { passive: false });
        document.addEventListener('gestureend', this.preventGesture, { passive: false });
        document.addEventListener('touchmove', this.preventGestureHandler, { passive: false });
        
        // Prevent double-tap zoom on iOS
        let lastTouchEnd = 0;
        this.doubleTapHandler = (e) => {
            const now = Date.now();
            if (now - lastTouchEnd < 300) {
                e.preventDefault();
            }
            lastTouchEnd = now;
        };
        document.addEventListener('touchend', this.doubleTapHandler, { passive: false });
    }

    /**
     * Unlock the viewport and restore normal behavior.
     */
    unlock() {
        if (!this.isLocked) return;
        
        this.isLocked = false;
        
        // Restore original body styles
        document.body.style.overflow = this.originalOverflow || '';
        document.body.style.touchAction = this.originalTouchAction || '';
        document.body.style.position = '';
        document.body.style.width = '';
        document.body.style.height = '';
        
        // Remove gesture prevention listeners
        document.removeEventListener('gesturestart', this.preventGesture);
        document.removeEventListener('gesturechange', this.preventGesture);
        document.removeEventListener('gestureend', this.preventGesture);
        
        if (this.preventGestureHandler) {
            document.removeEventListener('touchmove', this.preventGestureHandler);
        }
        if (this.doubleTapHandler) {
            document.removeEventListener('touchend', this.doubleTapHandler);
        }
    }

    preventGesture(e) {
        e.preventDefault();
    }
}
