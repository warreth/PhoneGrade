import { DeviceTest } from './DeviceTest.js';

export class DigitizerTest extends DeviceTest {
    constructor() {
        super('digitizer', 'Digitizer Edge Test', 'Trace the outer edges of the screen');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Initializing canvas...');

        container.innerHTML = `
            <div id="digitizer-wrap" style="position: fixed; inset: 0; width: 100vw; height: 100vh; height: 100dvh; background: #0f172a; z-index: 10000; touch-action: none; user-select: none; overflow: hidden;">
                <canvas id="digitizer-canvas" style="display: block; width: 100%; height: 100%; touch-action: none;"></canvas>
                <div id="digitizer-center-text" style="position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); text-align: center; pointer-events: none; width: 80%;">
                    <div style="font-size: 16px; font-weight: bold; margin-bottom: 8px; color: #ffffff;">Teken over de rode randen</div>
                    <div id="digitizer-status" style="font-size: 28px; font-weight: bold; color: #38bdf8;">0%</div>
                </div>
            </div>
        `;

        const wrap = container.querySelector('#digitizer-wrap');
        const canvas = container.querySelector('#digitizer-canvas');
        const statusDisplay = container.querySelector('#digitizer-status');
        
        const dpr = window.devicePixelRatio || 1;
        await new Promise(r => requestAnimationFrame(r));
        
        const rect = wrap.getBoundingClientRect();
        canvas.width = rect.width * dpr;
        canvas.height = rect.height * dpr;
        
        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        const pathWidth = 40; 
        let isTracing = false;
        
        const blocks = [];
        const blockSize = 24;
        
        // Top and bottom edges
        for (let x = 0; x < rect.width; x += blockSize) blocks.push({x, y: 0, w: Math.min(blockSize, rect.width - x), h: pathWidth, hit: false});
        for (let x = 0; x < rect.width; x += blockSize) blocks.push({x, y: rect.height - pathWidth, w: Math.min(blockSize, rect.width - x), h: pathWidth, hit: false});
        // Left and right edges
        for (let y = pathWidth; y < rect.height - pathWidth; y += blockSize) blocks.push({x: 0, y, w: pathWidth, h: Math.min(blockSize, rect.height - pathWidth - y), hit: false});
        for (let y = pathWidth; y < rect.height - pathWidth; y += blockSize) blocks.push({x: rect.width - pathWidth, y, w: pathWidth, h: Math.min(blockSize, rect.height - pathWidth - y), hit: false});

        const totalBlocks = blocks.length;

        const drawGrid = () => {
            ctx.clearRect(0, 0, rect.width, rect.height);
            
            blocks.forEach(b => {
                ctx.fillStyle = b.hit ? 'rgba(34, 197, 94, 0.9)' : 'rgba(239, 68, 68, 0.85)';
                ctx.fillRect(b.x, b.y, b.w, b.h);
                ctx.strokeStyle = '#0f172a';
                ctx.lineWidth = 1;
                ctx.strokeRect(b.x, b.y, b.w, b.h);
            });
        };

        drawGrid();

        const checkHit = (clientX, clientY) => {
            const x = clientX - rect.left;
            const y = clientY - rect.top;
            
            let newHit = false;
            blocks.forEach(b => {
                if (!b.hit && x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h) {
                    b.hit = true;
                    newHit = true;
                }
            });

            if (newHit) {
                drawGrid();
                const hits = blocks.filter(b => b.hit).length;
                const pct = Math.round((hits / totalBlocks) * 100);
                statusDisplay.textContent = pct + '%';
                this.reportProgress(wsClient, pct, 'Randdekking: ' + pct + '%');
                
                if (this.haptic) this.haptic.tap();
            }
        };

        const onTouchStart = (e) => {
            e.preventDefault();
            isTracing = true;
            for (let i = 0; i < e.touches.length; i++) {
                checkHit(e.touches[i].clientX, e.touches[i].clientY);
            }
        };

        const onTouchMove = (e) => {
            e.preventDefault();
            if (!isTracing) return;
            for (let i = 0; i < e.touches.length; i++) {
                checkHit(e.touches[i].clientX, e.touches[i].clientY);
            }
        };

        const onTouchEnd = (e) => {
            e.preventDefault();
            if (e.touches.length === 0) isTracing = false;
        };

        canvas.addEventListener('touchstart', onTouchStart, { passive: false });
        canvas.addEventListener('touchmove', onTouchMove, { passive: false });
        canvas.addEventListener('touchend', onTouchEnd, { passive: false });
        canvas.addEventListener('touchcancel', onTouchEnd, { passive: false });

        canvas.addEventListener('mousedown', (e) => {
            isTracing = true;
            checkHit(e.clientX, e.clientY);
        });
        window.addEventListener('mousemove', (e) => {
            if (isTracing) checkHit(e.clientX, e.clientY);
        });
        window.addEventListener('mouseup', () => { isTracing = false; });

        return new Promise((resolve) => {
            const timeout = 60000;
            const start = Date.now();
            
            const interval = setInterval(() => {
                const hits = blocks.filter(b => b.hit).length;
                const pct = Math.round((hits / totalBlocks) * 100);
                
                if (pct >= 95 || hits >= totalBlocks) {
                    clearInterval(interval);
                    this.pass('Digitizer randen 100% responsief');
                    this.details.coverage = pct;
                    resolve();
                } else if (Date.now() - start > timeout) {
                    clearInterval(interval);
                    if (pct >= 85) {
                        this.pass('Digitizer randen voldoende responsief (' + pct + '%)');
                    } else {
                        this.fail('Digitizer randen niet responsief (' + pct + '%)');
                    }
                    this.details.coverage = pct;
                    resolve();
                }
            }, 100);
        });
    }
}
