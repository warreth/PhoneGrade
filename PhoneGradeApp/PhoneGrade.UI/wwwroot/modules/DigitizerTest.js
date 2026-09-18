import { DeviceTest } from './DeviceTest.js';

export class DigitizerTest extends DeviceTest {
    constructor() {
        super('digitizer', 'Digitizer Edge Test', 'Trace the outer edges of the screen');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Initializing canvas...');

        container.innerHTML = `
            <div id="canvas-container" style="position: relative; width: 100%; height: 65vh; max-height: 550px; background: var(--color-bg-secondary); border-radius: var(--radius-lg); overflow: hidden; touch-action: none; box-shadow: inset 0 0 0 2px var(--color-border);">
                <canvas id="digitizer-canvas" style="display: block; width: 100%; height: 100%; touch-action: none;"></canvas>
                <div id="digitizer-center-text" style="position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); text-align: center; pointer-events: none; width: 80%;">
                    <div style="font-size: 16px; font-weight: bold; margin-bottom: 8px; color: var(--color-text);">Trace the Red Border</div>
                    <div id="digitizer-status" style="font-size: 24px; font-weight: bold; color: var(--color-accent);">0%</div>
                </div>
            </div>
        `;

        const canvasContainer = container.querySelector('#canvas-container');
        const canvas = container.querySelector('#digitizer-canvas');
        const statusDisplay = container.querySelector('#digitizer-status');
        
        const dpr = window.devicePixelRatio || 1;
        // Wait a frame for layout to settle
        await new Promise(r => requestAnimationFrame(r));
        
        const rect = canvasContainer.getBoundingClientRect();
        canvas.width = rect.width * dpr;
        canvas.height = rect.height * dpr;
        
        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        const pathWidth = 35; 
        let isTracing = false;
        
        const blocks = [];
        const blockSize = 20; // smaller blocks for higher precision requirements
        
        // Generate edge blocks
        for (let x = 0; x < rect.width; x += blockSize) blocks.push({x, y: 0, w: blockSize, h: pathWidth, hit: false});
        for (let x = 0; x < rect.width; x += blockSize) blocks.push({x, y: rect.height - pathWidth, w: blockSize, h: pathWidth, hit: false});
        for (let y = pathWidth; y < rect.height - pathWidth; y += blockSize) blocks.push({x: 0, y, w: pathWidth, h: blockSize, hit: false});
        for (let y = pathWidth; y < rect.height - pathWidth; y += blockSize) blocks.push({x: rect.width - pathWidth, y, w: pathWidth, h: blockSize, hit: false});

        const totalBlocks = blocks.length;

        const drawGrid = () => {
            ctx.clearRect(0, 0, rect.width, rect.height);
            
            blocks.forEach(b => {
                ctx.fillStyle = b.hit ? 'rgba(74, 222, 128, 0.8)' : 'rgba(248, 113, 113, 0.8)';
                ctx.fillRect(b.x, b.y, b.w, b.h);
                
                // Draw inner borders for the blocks
                ctx.strokeStyle = 'rgba(0,0,0,0.2)';
                ctx.lineWidth = 1;
                ctx.strokeRect(b.x, b.y, b.w, b.h);
            });
        };

        const updateBlocks = (x, y) => {
            let hitAny = false;
            // Increase hit radius significantly to make it easier on high-DPI screens without being too loose
            const hitRadius = 30; 
            
            blocks.forEach(b => {
                if (!b.hit) {
                    // Check if point is inside or near the block
                    const closestX = Math.max(b.x, Math.min(x, b.x + b.w));
                    const closestY = Math.max(b.y, Math.min(y, b.y + b.h));
                    
                    const distanceX = x - closestX;
                    const distanceY = y - closestY;
                    
                    if ((distanceX * distanceX + distanceY * distanceY) < (hitRadius * hitRadius)) {
                        b.hit = true;
                        hitAny = true;
                    }
                }
            });
            return hitAny;
        };

        return new Promise((resolve) => {
            let lastPos = null;
            let lastPercent = 0;

            const getPointerPos = (e) => {
                const clientX = e.touches ? e.touches[0].clientX : e.clientX;
                const clientY = e.touches ? e.touches[0].clientY : e.clientY;
                const rect = canvas.getBoundingClientRect();
                return {
                    x: clientX - rect.left,
                    y: clientY - rect.top
                };
            };

            const handleStart = (e) => {
                e.preventDefault();
                isTracing = true;
                const pos = getPointerPos(e);
                lastPos = pos;
                if(updateBlocks(pos.x, pos.y)) drawGrid();
            };

            const handleMove = (e) => {
                e.preventDefault();
                if (!isTracing) return;
                
                const pos = getPointerPos(e);
                
                // Draw bright trail line over the blocks
                if (lastPos) {
                    ctx.strokeStyle = '#ffffff';
                    ctx.lineWidth = 8;
                    ctx.lineCap = 'round';
                    ctx.beginPath();
                    ctx.moveTo(lastPos.x, lastPos.y);
                    ctx.lineTo(pos.x, pos.y);
                    ctx.stroke();
                }
                
                lastPos = pos;

                if (updateBlocks(pos.x, pos.y)) {
                    // Redraw grid (wipes the white line, but the hit blocks turn green)
                    drawGrid();
                    const hitCount = blocks.filter(b => b.hit).length;
                    const percent = Math.floor((hitCount / totalBlocks) * 100);
                    
                    if (percent > lastPercent) {
                        lastPercent = percent;
                        statusDisplay.textContent = `${percent}%`;
                        statusDisplay.style.color = percent > 85 ? 'var(--color-success)' : 'var(--color-accent)';
                        this.reportProgress(wsClient, percent, `Tracing: ${percent}%`);
                        
                        // Haptic feedback every 10%
                        if (percent % 10 === 0) {
                            this.haptic.progress();
                        }
                    }

                    // 92% is acceptable passing threshold for edges which can be tricky with cases
                    if (percent >= 92) {
                        isTracing = false;
                        cleanup();
                        
                        this.pass('Screen edge digitizer functions normally');
                        this.details.completionPercent = percent;
                        resolve();
                    }
                }
            };

            const handleEnd = (e) => {
                e.preventDefault();
                isTracing = false;
                lastPos = null;
                // Redraw to remove white trail segment
                drawGrid(); 
            };

            const cleanup = () => {
                canvas.removeEventListener('touchstart', handleStart);
                canvas.removeEventListener('touchmove', handleMove);
                canvas.removeEventListener('touchend', handleEnd);
                canvas.removeEventListener('touchcancel', handleEnd);
                canvas.removeEventListener('mousedown', handleStart);
                canvas.removeEventListener('mousemove', handleMoveMouse);
                canvas.removeEventListener('mouseup', handleEnd);
            };

            const handleMoveMouse = (e) => {
                if (e.buttons > 0) handleMove(e);
            };

            canvas.addEventListener('touchstart', handleStart, {passive: false});
            canvas.addEventListener('touchmove', handleMove, {passive: false});
            canvas.addEventListener('touchend', handleEnd, {passive: false});
            canvas.addEventListener('touchcancel', handleEnd, {passive: false});

            canvas.addEventListener('mousedown', handleStart);
            canvas.addEventListener('mousemove', handleMoveMouse, {passive: false});
            canvas.addEventListener('mouseup', handleEnd);

            drawGrid();

            setTimeout(() => {
                if (isTracing !== null) {
                    isTracing = null;
                    cleanup();
                    const hitCount = blocks.filter(b => b.hit).length;
                    const percent = Math.floor((hitCount / totalBlocks) * 100);
                    
                    if (percent >= 85) {
                        this.pass(`Passed with acceptable margin (${percent}%)`);
                    } else {
                        this.fail(`Only ${percent}% of screen edge was responsive within time limit.`);
                    }
                    this.details.completionPercent = percent;
                    resolve();
                }
            }, 45000); // 45 seconds to trace edges
        });
    }
}
