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
            <div id="canvas-container" style="position: relative; width: 100%; height: 60vh; max-height: 500px; background: #000; border: 2px solid var(--color-border); border-radius: var(--radius-lg); overflow: hidden; touch-action: none;">
                <canvas id="digitizer-canvas" style="display: block; width: 100%; height: 100%; touch-action: none;"></canvas>
            </div>
            <div id="digitizer-status" style="margin-top: 16px; text-align: center; color: var(--color-text-secondary); font-weight: bold;">0% Traced</div>
        `;

        const canvasContainer = container.querySelector('#canvas-container');
        const canvas = container.querySelector('#digitizer-canvas');
        const statusDisplay = container.querySelector('#digitizer-status');
        
        const dpr = window.devicePixelRatio || 1;
        const rect = canvasContainer.getBoundingClientRect();
        canvas.width = rect.width * dpr;
        canvas.height = rect.height * dpr;
        
        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        const pathWidth = 40; 
        let isTracing = false;
        
        const blocks = [];
        const blockSize = 20;
        
        for (let x = 0; x < rect.width; x += blockSize) blocks.push({x, y: 0, w: blockSize, h: pathWidth, hit: false});
        for (let x = 0; x < rect.width; x += blockSize) blocks.push({x, y: rect.height - pathWidth, w: blockSize, h: pathWidth, hit: false});
        for (let y = pathWidth; y < rect.height - pathWidth; y += blockSize) blocks.push({x: 0, y, w: pathWidth, h: blockSize, hit: false});
        for (let y = pathWidth; y < rect.height - pathWidth; y += blockSize) blocks.push({x: rect.width - pathWidth, y, w: pathWidth, h: blockSize, hit: false});

        const totalBlocks = blocks.length;

        const drawGrid = () => {
            ctx.clearRect(0, 0, rect.width, rect.height);
            
            blocks.forEach(b => {
                ctx.fillStyle = b.hit ? 'rgba(74, 222, 128, 0.6)' : 'rgba(248, 113, 113, 0.6)';
                ctx.fillRect(b.x, b.y, b.w, b.h);
            });
            
            ctx.fillStyle = '#ffffff';
            ctx.font = '16px sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText('Trace the red border', rect.width/2, rect.height/2);
        };

        const updateBlocks = (x, y) => {
            let hitAny = false;
            const hitRadius = 15;
            
            blocks.forEach(b => {
                if (!b.hit) {
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
                
                if (lastPos) {
                    ctx.strokeStyle = 'rgba(255,255,255,0.8)';
                    ctx.lineWidth = 6;
                    ctx.lineCap = 'round';
                    ctx.beginPath();
                    ctx.moveTo(lastPos.x, lastPos.y);
                    ctx.lineTo(pos.x, pos.y);
                    ctx.stroke();
                }
                
                lastPos = pos;

                if (updateBlocks(pos.x, pos.y)) {
                    drawGrid();
                    const hitCount = blocks.filter(b => b.hit).length;
                    const percent = Math.floor((hitCount / totalBlocks) * 100);
                    
                    statusDisplay.textContent = `${percent}% Traced`;
                    statusDisplay.style.color = percent > 80 ? 'var(--color-success)' : 'var(--color-text-secondary)';
                    
                    this.reportProgress(wsClient, percent, `Tracing: ${percent}%`);

                    if (percent >= 92) {
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
                lastPos = null;
            };

            canvas.addEventListener('touchstart', handleStart, {passive: false});
            canvas.addEventListener('touchmove', handleMove, {passive: false});
            canvas.addEventListener('touchend', handleEnd, {passive: false});
            canvas.addEventListener('touchcancel', handleEnd, {passive: false});

            canvas.addEventListener('mousedown', handleStart);
            canvas.addEventListener('mousemove', (e) => { if (e.buttons > 0) handleMove(e); });
            canvas.addEventListener('mouseup', handleEnd);

            drawGrid();

            setTimeout(() => {
                if (isTracing !== null) {
                    isTracing = null;
                    const hitCount = blocks.filter(b => b.hit).length;
                    const percent = Math.floor((hitCount / totalBlocks) * 100);
                    
                    if (percent >= 85) {
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