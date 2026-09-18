/**
 * VisualFeedback - Helper for smooth animations, transitions, and visual cues.
 */
export class VisualFeedback {
    /**
     * Create a smooth fade-in animation.
     * @param {HTMLElement} element
     * @param {number} duration - Duration in ms
     */
    static fadeIn(element, duration = 300) {
        element.style.opacity = '0';
        element.style.transition = `opacity ${duration}ms ease-in-out`;
        
        requestAnimationFrame(() => {
            element.style.opacity = '1';
        });
    }

    /**
     * Create a smooth fade-out animation.
     * @param {HTMLElement} element
     * @param {number} duration - Duration in ms
     */
    static fadeOut(element, duration = 300) {
        element.style.transition = `opacity ${duration}ms ease-in-out`;
        element.style.opacity = '0';
        
        return new Promise(resolve => {
            setTimeout(() => resolve(), duration);
        });
    }

    /**
     * Create a pulse animation effect.
     * @param {HTMLElement} element
     * @param {string} color - CSS color
     */
    static pulse(element, color = 'rgba(74, 222, 128, 0.5)') {
        const originalBg = element.style.backgroundColor;
        element.style.transition = 'background-color 150ms ease-in-out';
        element.style.backgroundColor = color;
        
        setTimeout(() => {
            element.style.backgroundColor = originalBg;
        }, 150);
    }

    /**
     * Create a ripple effect from a touch/click point.
     * @param {HTMLElement} container
     * @param {number} x - X coordinate
     * @param {number} y - Y coordinate
     * @param {string} color - CSS color
     */
    static ripple(container, x, y, color = 'rgba(255, 255, 255, 0.6)') {
        const ripple = document.createElement('div');
        ripple.style.cssText = `
            position: absolute;
            left: ${x}px;
            top: ${y}px;
            width: 20px;
            height: 20px;
            border-radius: 50%;
            background: ${color};
            transform: translate(-50%, -50%) scale(0);
            animation: ripple-expand 600ms ease-out forwards;
            pointer-events: none;
            z-index: 9999;
        `;
        
        container.appendChild(ripple);
        
        setTimeout(() => {
            ripple.remove();
        }, 600);
    }

    /**
     * Create a smooth progress bar update.
     * @param {HTMLElement} progressBar
     * @param {number} percent - 0 to 100
     */
    static updateProgress(progressBar, percent) {
        progressBar.style.transition = 'width 300ms ease-out';
        progressBar.style.width = `${Math.min(100, Math.max(0, percent))}%`;
    }

    /**
     * Show a status message with auto-fade.
     * @param {HTMLElement} container
     * @param {string} message
     * @param {string} type - 'success', 'error', 'info', 'warning'
     * @param {number} duration - Display duration in ms
     */
    static showStatus(container, message, type = 'info', duration = 3000) {
        const statusEl = document.createElement('div');
        const colors = {
            success: 'var(--color-success)',
            error: 'var(--color-error)',
            info: 'var(--color-accent)',
            warning: 'var(--color-warning)'
        };
        
        statusEl.style.cssText = `
            position: fixed;
            top: 20px;
            left: 50%;
            transform: translateX(-50%) translateY(-20px);
            background: ${colors[type] || colors.info};
            color: #fff;
            padding: 12px 24px;
            border-radius: 8px;
            font-weight: 600;
            font-size: 14px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.3);
            z-index: 10000;
            opacity: 0;
            transition: opacity 300ms ease-in-out, transform 300ms ease-in-out;
        `;
        statusEl.textContent = message;
        
        document.body.appendChild(statusEl);
        
        requestAnimationFrame(() => {
            statusEl.style.opacity = '1';
            statusEl.style.transform = 'translateX(-50%) translateY(0)';
        });
        
        setTimeout(() => {
            statusEl.style.opacity = '0';
            statusEl.style.transform = 'translateX(-50%) translateY(-20px)';
            setTimeout(() => statusEl.remove(), 300);
        }, duration);
    }

    /**
     * Create a visual checkmark animation.
     * @param {HTMLElement} container
     */
    static showCheckmark(container) {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 52 52');
        svg.style.cssText = `
            width: 80px;
            height: 80px;
            margin: 20px auto;
            display: block;
        `;
        
        const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        circle.setAttribute('cx', '26');
        circle.setAttribute('cy', '26');
        circle.setAttribute('r', '25');
        circle.setAttribute('fill', 'none');
        circle.setAttribute('stroke', '#4ade80');
        circle.setAttribute('stroke-width', '2');
        circle.style.strokeDasharray = '166';
        circle.style.strokeDashoffset = '166';
        circle.style.animation = 'dash 600ms ease-in-out forwards';
        
        const check = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        check.setAttribute('fill', 'none');
        check.setAttribute('stroke', '#4ade80');
        check.setAttribute('stroke-width', '3');
        check.setAttribute('d', 'M14.1 27.2l7.1 7.2 16.7-16.8');
        check.style.strokeDasharray = '48';
        check.style.strokeDashoffset = '48';
        check.style.animation = 'dash 300ms 400ms ease-in-out forwards';
        
        svg.appendChild(circle);
        svg.appendChild(check);
        container.appendChild(svg);
        
        return svg;
    }
}

// Inject CSS animations for ripple and checkmark
if (!document.getElementById('visual-feedback-styles')) {
    const style = document.createElement('style');
    style.id = 'visual-feedback-styles';
    style.textContent = `
        @keyframes ripple-expand {
            to {
                transform: translate(-50%, -50%) scale(4);
                opacity: 0;
            }
        }
        @keyframes dash {
            to {
                stroke-dashoffset: 0;
            }
        }
    `;
    document.head.appendChild(style);
}
