import { DeviceTest } from './DeviceTest.js';

export class TouchTest extends DeviceTest {
    constructor() {
        super('touch', 'Touchscreen Test', 'Swipe across all cells to detect dead zones');
        this.gridCols = 8;
        this.gridRows = 12;
        this.touchedCells = new Set();
        this.totalCells = this.gridCols * this.gridRows;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting...');

        const grid = document.createElement('div');
        grid.className = 'touch-grid';
        grid.style.cssText = `display: grid; grid-template-columns: repeat(${this.gridCols}, 1fr); grid-template-rows: repeat(${this.gridRows}, 1fr); gap: 1px; width: 100%; height: 60vh; max-height: 500px; margin: 16px 0; touch-action: none;`;

        for (let i = 0; i < this.totalCells; i++) {
            const cell = document.createElement('div');
            cell.className = 'touch-cell';
            cell.dataset.index = i;
            cell.style.cssText = 'background: var(--color-bg-tertiary); transition: background 0.1s;';
            grid.appendChild(cell);
        }

        container.innerHTML = '';
        container.appendChild(grid);

        const instructions = document.createElement('p');
        instructions.className = 'test-instructions';
        instructions.textContent = 'Swipe your finger across all cells. They should all turn green.';
        container.insertBefore(instructions, grid);

        const handleTouch = (e) => {
            e.preventDefault();
            for (const touch of (e.touches || [e])) {
                const element = document.elementFromPoint(touch.clientX, touch.clientY);
                if (element && element.classList.contains('touch-cell')) {
                    const index = element.dataset.index;
                    if (!this.touchedCells.has(index)) {
                        this.touchedCells.add(index);
                        element.style.background = 'var(--color-success)';
                        
                        const progress = (this.touchedCells.size / this.totalCells) * 100;
                        this.reportProgress(wsClient, progress, `${this.touchedCells.size}/${this.totalCells} cells touched`);
                    }
                }
            }
        };

        grid.addEventListener('touchstart', handleTouch, { passive: false });
        grid.addEventListener('touchmove', handleTouch, { passive: false });
        grid.addEventListener('mousedown', handleTouch, { passive: false });
        grid.addEventListener('mousemove', (e) => {
            if (e.buttons > 0) handleTouch(e);
        }, { passive: false });

        const timeout = 60000;
        const startTime = Date.now();

        return new Promise((resolve) => {
            const checkComplete = () => {
                if (this.touchedCells.size >= this.totalCells) {
                    this.pass('All cells touched successfully');
                    this.details.coverage = 100;
                    this.details.touchedCount = this.touchedCells.size;
                    this.details.totalCells = this.totalCells;
                    resolve();
                } else if (Date.now() - startTime > timeout) {
                    const coverage = Math.round((this.touchedCells.size / this.totalCells) * 100);
                    if (coverage >= 90) {
                        this.pass(`${coverage}% coverage - acceptable`);
                    } else {
                        this.fail(`Only ${coverage}% of screen responsive`);
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