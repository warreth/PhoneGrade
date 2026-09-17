import { DeviceTest } from './DeviceTest.js';

/**
 * TouchTest: Grid cell sweep requiring finger drag across all screen sectors
 * to detect dead zones on the touchscreen.
 */
export class TouchTest extends DeviceTest {
    constructor() {
        super('touch', 'Touchscreen Test', 'Veeg over alle cellen om dode zones te detecteren');
        this.gridCols = 4;
        this.gridRows = 6;
        this.touchedCells = new Set();
        this.totalCells = this.gridCols * this.gridRows;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starten...');

        // Create the touch grid UI
        const grid = document.createElement('div');
        grid.className = 'touch-grid';
        grid.style.cssText = `display: grid; grid-template-columns: repeat(${this.gridCols}, 1fr); grid-template-rows: repeat(${this.gridRows}, 1fr); gap: 2px; width: 100%; height: 350px; margin: 16px 0;`;

        for (let i = 0; i < this.totalCells; i++) {
            const cell = document.createElement('div');
            cell.className = 'touch-cell';
            cell.dataset.index = i;
            cell.style.cssText = 'background: #3a3a3a; border: 1px solid #1a1a1a; transition: background 0.2s;';
            grid.appendChild(cell);
        }

        container.innerHTML = '';
        container.appendChild(grid);

        // Add instructions
        const instructions = document.createElement('p');
        instructions.className = 'test-instructions';
        instructions.textContent = 'Veeg met je vinger over alle cellen. Alle cellen moeten groen worden.';
        container.insertBefore(instructions, grid);

        // Track touches
        const handleTouch = (e) => {
            e.preventDefault();
            for (const touch of e.touches) {
                const element = document.elementFromPoint(touch.clientX, touch.clientY);
                if (element && element.classList.contains('touch-cell')) {
                    const index = element.dataset.index;
                    if (!this.touchedCells.has(index)) {
                        this.touchedCells.add(index);
                        element.style.background = '#00ff88';
                        
                        // Update progress
                        const progress = (this.touchedCells.size / this.totalCells) * 100;
                        this.reportProgress(wsClient, progress, `${this.touchedCells.size}/${this.totalCells} cellen geraakt`);
                    }
                }
            }
        };

        grid.addEventListener('touchstart', handleTouch, { passive: false });
        grid.addEventListener('touchmove', handleTouch, { passive: false });

        // Wait for all cells to be touched or timeout
        const timeout = 60000; // 60 seconds max
        const startTime = Date.now();

        return new Promise((resolve) => {
            const checkComplete = () => {
                if (this.touchedCells.size >= this.totalCells) {
                    this.pass('Alle cellen zijn succesvol geraakt');
                    this.details.coverage = 100;
                    this.details.touchedCount = this.touchedCells.size;
                    this.details.totalCells = this.totalCells;
                    resolve();
                } else if (Date.now() - startTime > timeout) {
                    const coverage = Math.round((this.touchedCells.size / this.totalCells) * 100);
                    if (coverage >= 90) {
                        this.pass(`${coverage}% dekking - acceptabel`);
                    } else {
                        this.fail(`Slechts ${coverage}% van het scherm reageert`);
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