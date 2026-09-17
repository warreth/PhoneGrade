import { DeviceTest } from './DeviceTest.js';

export class DigitizerTest extends DeviceTest {
    constructor() {
        super('digitizer', 'Digitizer & Edges', 'Trace the screen edges to verify full digitizer functionality');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Initializing canvas...');

        container.innerHTML = `
            <h3 style="color: var(--color-accent); margin-bottom: 16px;">Digitizer Test</h3>
            <p class="test-instructions">Trace the red border completely around the screen edge.</p>
            <div id="canvas-container" style="position: relative; width: 100%; height: 350px; background: #000; border: 2px solid var(--color-border); border-radius: var(--radius-lg); overflow: hidden; touch-action: none;">
                <canvas id="digitizer-canvas" style="display: block; width: 100%; height: 100%;"></canvas>
            </div>
            <div id="digitizer-status" style="margin-top: 16px; text-align: center; color: var(--color-text-secondary);">0% Traced</div>
        `;

        const canvasContainer = container.querySelector('#canvas-container');
        const canvas = container.querySelector('#digitizer-canvas');
        const statusDisplay = container.querySelector('#digitizer-status');
        
        // Ensure high-DPI scaling
        const rect = canvasContainer.getBoundingClientRect();
        canvas.width = rect.width;
        canvas.height = rect.height;
        const ctx = canvas.getContext('2d');

        // Path settings
        const pathWidth = 30; // Pixel width from edge that needs to be touched
        let isTracing = false;
        
        // Divide perimeter into blocks to check completion
        const blocks = [];
        const blockSize = 20;
        
        // Top edge
        for (let x = 0; x < canvas.width; x += blockSize) blocks.push({x, y: 0, w: blockSize, h: pathWidth, hit: false});
        // Bottom edge
        for (let x = 0; x < canvas.width; x += blockSize) blocks.push({x, y: canvas.height - pathWidth, w: blockSize, h: pathWidth, hit: false});
        // Left edge
        for (let y = pathWidth; y < canvas.height - pathWidth; y += blockSize) blocks.push({x: 0, y, w: pathWidth, h: blockSize, hit: false});
        // Right edge
        for (let y = pathWidth; y < canvas.height - pathWidth; y += blockSize) blocks.push({x: canvas.width - pathWidth, y, w: pathWidth, h: blockSize, hit: false});

        const totalBlocks = blocks.length;

        const drawGrid = () => {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            
            // Draw background blocks (red = untouched, green = touched)
            blocks.forEach(b => {
                ctx.fillStyle = b.hit ? 'rgba(74, 222, 128, 0.5)' : 'rgba(248, 113, 113, 0.5)';
                ctx.fillRect(b.x, b.y, b.w, b.h);
            });
            
            // Draw center hint
            ctx.fillStyle = '#fff';
            ctx.font = '14px sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText('Trace the border', canvas.width/2, canvas.height/2);
        };

        const updateBlocks = (x, y) => {
            let hitAny = false;
            blocks.forEach(b => {
                if (!b.hit && x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h) {
                    b.hit = true;
                    hitAny = true;
                }
            });
            return hitAny;
        };

        return new Promise((resolve) => {
            const getPointerPos = (e) => {
                const rect = canvas.getBoundingClientRect();
                const clientX = e.touches ? e.touches[0].clientX : e.clientX;
                const clientY = e.touches ? e.touches[0].clientY : e.clientY;
                return {
                    x: clientX - rect.left,
                    y: clientY - rect.top
                };
            };

            const handleStart = (e) => {
                e.preventDefault();
                isTracing = true;
                const pos = getPointerPos(e);
                if(updateBlocks(pos.x, pos.y)) drawGrid();
            };

            const handleMove = (e) => {
                e.preventDefault();
                if (!isTracing) return;
                const pos = getPointerPos(e);
                
                // Draw line for trace
                ctx.fillStyle = 'white';
                ctx.beginPath();
                ctx.arc(pos.x, pos.y, 10, 0, Math.PI * 2);
                ctx.fill();

                if (updateBlocks(pos.x, pos.y)) {
                    drawGrid();
                    const hitCount = blocks.filter(b => b.hit).length;
                    const percent = Math.floor((hitCount / totalBlocks) * 100);
                    
                    statusDisplay.textContent = `${percent}% Traced`;
                    this.reportProgress(wsClient, percent, `Tracing: ${percent}%`);

                    if (percent >= 95) { // Allow slight margin of error
                        isTracing = false;
                        canvas.removeEventListener('touchstart', handleStart);
                        canvas.removeEventListener('touchmove', handleMove);
                        canvas.removeEventListener('touchend', handleEnd);
                        
                        this.pass('Screen edge digitizer functions normally');
                        this.details.completionPercent = percent;
                        resolve();
                    }
                }
            };

            const handleEnd = (e) => {
                e.preventDefault();
                isTracing = false;
            };

            canvas.addEventListener('touchstart', handleStart, {passive: false});
            canvas.addEventListener('touchmove', handleMove, {passive: false});
            canvas.addEventListener('touchend', handleEnd);
            canvas.addEventListener('touchcancel', handleEnd);

            drawGrid();

            // Set timeout for 60 seconds
            setTimeout(() => {
                if (isTracing !== null) { // if still active
                    isTracing = false;
                    const hitCount = blocks.filter(b => b.hit).length;
                    const percent = Math.floor((hitCount / totalBlocks) * 100);
                    
                    if (percent >= 90) {
                        this.pass(`Almost complete (${percent}%)`);
                    } else {
                        this.fail(`Only ${percent}% of screen edge was responsive within time limit.`);
                    }
                    this.details.completionPercent = percent;
                    resolve();
                }
            }, 60000);
        });
    }
}