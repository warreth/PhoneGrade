import { DeviceTest } from './DeviceTest.js';
import { VisualFeedback } from './VisualFeedback.js';

export class TouchTest extends DeviceTest {
    constructor() {
        super('touch', 'Touchscreen Test', 'Swipe across all cells to detect dead zones');
        this.gridCols = 8;
        this.gridRows = 14;
        this.touchedCells = new Set();
        this.totalCells = this.gridCols * this.gridRows;
        this.trailPoints = [];
        this.maxTrailLength = 20;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting touchscreen test...');

        container.innerHTML = `
            <div id="touch-wrap" style="position: fixed; inset: 0; width: 100vw; height: 100vh; height: 100dvh; background: #0f172a; z-index: 10000; touch-action: none; user-select: none; overflow: hidden; display: flex; flex-direction: column;">
                <div id="touch-instruction-card" style="position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); background: rgba(15,23,42,0.92); color: #ffffff; padding: 20px 24px; border-radius: 12px; font-size: 16px; font-weight: 600; text-align: center; pointer-events: none; z-index: 10005; border: 2px solid #2563eb; box-shadow: 0 10px 25px rgba(0,0,0,0.5); transition: opacity 0.3s ease;">
                    <div style="font-size: 20px; font-weight: bold; margin-bottom: 8px; color: #38bdf8;">Touchscreen Test</div>
                    <div>Veeg met je vinger over het hele scherm om alle grijze vakjes groen te kleuren</div>
                </div>

                <div id="touch-info-bar" style="position: absolute; top: 16px; left: 50%; transform: translateX(-50%); background: rgba(15,23,42,0.85); color: #ffffff; padding: 6px 18px; border-radius: 9999px; font-size: 14px; font-weight: 700; pointer-events: none; z-index: 10002; border: 1px solid rgba(255,255,255,0.25); box-shadow: 0 4px 10px rgba(0,0,0,0.3);">
                    Dekking: <span id="touch-progress" style="color: #4ade80;">0%</span>
                </div>

                <div id="touch-grid" style="display: grid; grid-template-columns: repeat(${this.gridCols}, 1fr); grid-template-rows: repeat(${this.gridRows}, 1fr); gap: 2px; width: 100%; height: 100%; padding: 4px; box-sizing: border-box; background: #1e293b;"></div>
                <canvas id="touch-trail-canvas" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; pointer-events: none; z-index: 10001;"></canvas>
            </div>
        `;

        const grid = container.querySelector('#touch-grid');
        const canvas = container.querySelector('#touch-trail-canvas');
        const progressDisplay = container.querySelector('#touch-progress');
        const instructionCard = container.querySelector('#touch-instruction-card');

        const dpr = window.devicePixelRatio || 1;
        await new Promise(r => requestAnimationFrame(r));

        const rect = grid.getBoundingClientRect();
        canvas.width = rect.width * dpr;
        canvas.height = rect.height * dpr;
        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        for (let i = 0; i < this.totalCells; i++) {
            const cell = document.createElement('div');
            cell.className = 'touch-cell';
            cell.dataset.index = i;
            cell.style.cssText = 'background: #334155; border: 1px solid #475569; border-radius: 3px; transition: background 80ms ease, transform 80ms ease;';
            grid.appendChild(cell);
        }

        const drawTrail = () => {
            ctx.clearRect(0, 0, rect.width, rect.height);
            if (this.trailPoints.length > 1) {
                ctx.beginPath();
                ctx.moveTo(this.trailPoints[0].x, this.trailPoints[0].y);
                for (let i = 1; i < this.trailPoints.length; i++) {
                    const xc = (this.trailPoints[i].x + this.trailPoints[i - 1].x) / 2;
                    const yc = (this.trailPoints[i].y + this.trailPoints[i - 1].y) / 2;
                    ctx.quadraticCurveTo(this.trailPoints[i - 1].x, this.trailPoints[i - 1].y, xc, yc);
                }
                ctx.strokeStyle = 'rgba(56, 189, 248, 0.7)';
                ctx.lineWidth = 14;
                ctx.lineCap = 'round';
                ctx.lineJoin = 'round';
                ctx.stroke();
            }
        };

        const handleTouch = (e) => {
            e.preventDefault();
            
            // Hide instruction card as soon as the user starts touching
            if (instructionCard && instructionCard.style.opacity !== '0') {
                instructionCard.style.opacity = '0';
            }

            const touches = e.touches ? Array.from(e.touches) : [e];

            for (const touch of touches) {
                const clientX = touch.clientX;
                const clientY = touch.clientY;

                const gridRect = grid.getBoundingClientRect();
                const relX = clientX - gridRect.left;
                const relY = clientY - gridRect.top;

                if (relX >= 0 && relX <= gridRect.width && relY >= 0 && relY <= gridRect.height) {
                    this.trailPoints.push({ x: relX, y: relY });
                    if (this.trailPoints.length > this.maxTrailLength) {
                        this.trailPoints.shift();
                    }
                    drawTrail();
                }

                const element = document.elementFromPoint(clientX, clientY);
                if (element && element.classList.contains('touch-cell')) {
                    const index = element.dataset.index;
                    if (!this.touchedCells.has(index)) {
                        this.touchedCells.add(index);
                        element.style.background = '#16a34a';
                        element.style.borderColor = '#22c55e';
                        element.style.transform = 'scale(0.96)';
                        setTimeout(() => element.style.transform = 'scale(1)', 100);

                        if (this.haptic) this.haptic.tap();

                        const progress = Math.round((this.touchedCells.size / this.totalCells) * 100);
                        progressDisplay.textContent = progress + '%';
                        this.reportProgress(wsClient, progress, 'Aangeraakt: ' + this.touchedCells.size + '/' + this.totalCells);
                    }
                }
            }
        };

        const handleTouchEnd = () => {
            this.trailPoints = [];
            ctx.clearRect(0, 0, rect.width, rect.height);
        };

        grid.addEventListener('touchstart', handleTouch, { passive: false });
        grid.addEventListener('touchmove', handleTouch, { passive: false });
        grid.addEventListener('touchend', handleTouchEnd, { passive: false });
        grid.addEventListener('touchcancel', handleTouchEnd, { passive: false });

        grid.addEventListener('mousedown', handleTouch, { passive: false });
        grid.addEventListener('mousemove', (e) => {
            if (e.buttons > 0) handleTouch(e);
        }, { passive: false });
        grid.addEventListener('mouseup', handleTouchEnd, { passive: false });

        const timeout = 60000;
        const startTime = Date.now();

        return new Promise((resolve) => {
            const checkComplete = () => {
                if (this.touchedCells.size >= this.totalCells) {
                    this.pass('Alle vakjes succesvol aangeraakt (100% dekking)');
                    this.details.coverage = 100;
                    this.details.touchedCount = this.touchedCells.size;
                    this.details.totalCells = this.totalCells;
                    resolve();
                } else if (Date.now() - startTime > timeout) {
                    const coverage = Math.round((this.touchedCells.size / this.totalCells) * 100);
                    if (coverage >= 90) {
                        this.pass('Voldoende dekking (' + coverage + '%)');
                    } else {
                        this.fail('Slechts ' + coverage + '% van scherm responsief (dode zones)');
                    }
                    this.details.coverage = coverage;
                    this.details.touchedCount = this.touchedCells.size;
                    this.details.totalCells = this.totalCells;
                    resolve();
                } else {
                    setTimeout(checkComplete, 100);
                }
            };
            checkComplete();
        });
    }
}
