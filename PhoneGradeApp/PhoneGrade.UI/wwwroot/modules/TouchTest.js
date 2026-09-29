import { DeviceTest } from './DeviceTest.js';

/**
 * The touchscreen and the outer edges of the screen, in one step.
 *
 * These were two separate steps. Both are the same physical thing, the digitizer,
 * and the operator had to swipe the whole screen, wait for a verdict, then be
 * asked to trace the edges of the very screen they had just been through. It also
 * cost a full step of the counter and a second verdict to read.
 *
 * Folding them together is only honest if both verdicts still reach the desktop,
 * so this step reports two rows: the grid coverage under `touch`, and the outer
 * edges under `digitizer`. A grading label then says both, and the progress store
 * keeps them apart, so a retry of the edge half is still a retry of the edge half.
 */
export class TouchTest extends DeviceTest {
    /** How long the edge half gets on its own. Same as the grid half. */
    static EDGE_TIMEOUT_MS = 60000;

    /** The id the edge half reports under, kept so the label does not change. */
    static EDGE_ID = 'digitizer';

    static EDGE_NAME = 'Digitizer Edge Test';

    constructor() {
        super('touch', 'Touchscreen Test', 'Swipe across all cells to detect dead zones');
        this.gridCols = 8;
        this.gridRows = 14;
        this.totalCells = this.gridCols * this.gridRows;
        this.trailPoints = [];
        this.maxTrailLength = 20;

        this.touchedCells = new Set();

        // The edge half keeps its own verdict rather than overwriting this one.
        // Two results, two outcomes, and the second must not cost the first.
        this.edge = this.freshEdgeOutcome();
    }

    freshEdgeOutcome() {
        return { status: 'pending', notes: '', details: {}, startedAt: null, endedAt: null };
    }

    getFailsafeMs() {
        // Two phases, each with its own 60 s allowance, plus the transition
        // between them. The runner's 90 s default would cut the edges off at the
        // halfway mark, which is a fail verdict for hardware nobody has finished
        // testing.
        return (TouchTest.EDGE_TIMEOUT_MS * 2) + 15000;
    }

    toResults() {
        const grid = this.toJSON();
        const edge = this.edgeRow();

        // A resumed run has both verdicts stored on the desktop rather than on
        // this object. Reading them back from here is what keeps a resumed row
        // from being reported as still pending, which would have the phone run the
        // edges again the moment the desktop asked for the current state.
        if (Array.isArray(this.storedRows)) {
            const storedEdge = this.storedRows.find(r =>
                String(r.testId).toLowerCase() === TouchTest.EDGE_ID);
            if (storedEdge) {
                edge.status = storedEdge.status || edge.status;
                edge.notes = storedEdge.status === 'pending' ? edge.notes : storedEdge.notes || edge.notes;
            }
        }

        return [grid, edge];
    }

    edgeRow() {
        return {
            id: TouchTest.EDGE_ID,
            name: TouchTest.EDGE_NAME,
            status: this.edge.status,
            notes: this.edge.notes,
            durationMs: this.edge.startedAt && this.edge.endedAt
                ? this.edge.endedAt - this.edge.startedAt
                : 0,
            details: this.edge.details
        };
    }

    resultIds() {
        return [this.id, TouchTest.EDGE_ID];
    }

    reset() {
        super.reset();
        this.touchedCells.clear();
        this.trailPoints = [];
        this.edge = this.freshEdgeOutcome();
        this.storedRows = null;
    }

    async run(wsClient, container) {
        this.start();

        await this.runGridPhase(wsClient, container);

        // A skip settles the step. Carrying on to the edges would ignore the
        // operator's own decision, and it would spend another minute of their
        // time on a test they just stopped. The edges are reported as skipped so
        // the two rows still agree about what happened.
        if (this.status === 'skipped') {
            this.skipEdge('Overgeslagen samen met het scherm');
            return;
        }

        await this.runEdgePhase(wsClient, container);
    }

    /** The grid: swipe every cell to show the surface responds everywhere. */
    async runGridPhase(wsClient, container) {
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

        const onMouseMove = (e) => {
            if (e.buttons > 0) handleTouch(e);
        };

        // Kept in a list so they can all come off again when the phase settles.
        // The second phase replaces this markup entirely.
        const gridHandlers = [
            ['touchstart', handleTouch],
            ['touchmove', handleTouch],
            ['touchend', handleTouchEnd],
            ['touchcancel', handleTouchEnd],
            ['mousedown', handleTouch],
            ['mousemove', onMouseMove],
            ['mouseup', handleTouchEnd],
        ];
        for (const [type, handler] of gridHandlers) {
            grid.addEventListener(type, handler, { passive: false });
        }

        return this.settleGrid(() => {
            for (const [type, handler] of gridHandlers) {
                grid.removeEventListener(type, handler);
            }
        });
    }

    /**
     * Waits for the grid to be finished, and detaches its listeners on the way out.
     *
     * The listeners sat on elements this phase is about to throw away, and a retry
     * runs the whole step again in the same page. Left in place they would keep a
     * 2d canvas and a set of per-cell styles alive and, worse, keep writing to a
     * grid that is no longer on screen.
     */
    settleGrid(detach) {
        return new Promise((resolve) => {
            const startTime = Date.now();
            let done = false;

            const finish = () => {
                if (done) return;
                done = true;
                clearInterval(interval);
                detach();

                const coverage = Math.round((this.touchedCells.size / this.totalCells) * 100);
                if (this.touchedCells.size >= this.totalCells) {
                    this.pass('Alle vakjes succesvol aangeraakt (100% dekking)');
                } else if (coverage >= 90) {
                    this.pass('Voldoende dekking (' + coverage + '%)');
                } else {
                    this.fail('Slechts ' + coverage + '% van scherm responsief (dode zones)');
                }

                this.details.coverage = coverage;
                this.details.touchedCount = this.touchedCells.size;
                this.details.totalCells = this.totalCells;
                resolve();
            };

            const interval = setInterval(() => {
                if (this.touchedCells.size >= this.totalCells) {
                    finish();
                } else if (Date.now() - startTime > 60000) {
                    finish();
                }
            }, 100);
        });
    }

    /** The outer edges: a dead strip along the bezel is the classic digitizer fault. */
    async runEdgePhase(wsClient, container) {
        this.edge.startedAt = Date.now();
        this.reportProgress(wsClient, 0, 'Initializing canvas...', TouchTest.EDGE_ID, TouchTest.EDGE_NAME);

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
                this.reportProgress(wsClient, pct, 'Randdekking: ' + pct + '%', TouchTest.EDGE_ID, TouchTest.EDGE_NAME);

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

        const onMouseDown = (e) => {
            isTracing = true;
            checkHit(e.clientX, e.clientY);
        };
        const onMouseMove = (e) => {
            if (isTracing) checkHit(e.clientX, e.clientY);
        };
        const onMouseUp = () => { isTracing = false; };

        const handlers = [
            ['touchstart', onTouchStart],
            ['touchmove', onTouchMove],
            ['touchend', onTouchEnd],
            ['touchcancel', onTouchEnd],
        ];
        for (const [type, handler] of handlers) {
            canvas.addEventListener(type, handler, { passive: false });
        }

        canvas.addEventListener('mousedown', onMouseDown);
        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);

        await this.settleEdge(() => {
            // Detached on the way out. The mousemove handler is on window, not on
            // the canvas, so it outlives this phase unless it is taken off: a
            // second run would then have two of them writing to a canvas that is
            // no longer on screen.
            for (const [type, handler] of handlers) {
                canvas.removeEventListener(type, handler);
            }
            canvas.removeEventListener('mousedown', onMouseDown);
            window.removeEventListener('mousemove', onMouseMove);
            window.removeEventListener('mouseup', onMouseUp);
        }, () => blocks.filter(b => b.hit).length, totalBlocks, wsClient);
    }

    settleEdge(detach, hitCount, totalBlocks, wsClient) {
        return new Promise((resolve) => {
            const start = Date.now();
            let done = false;

            const finish = () => {
                if (done) return;
                done = true;
                clearInterval(interval);
                detach();

                const pct = Math.round((hitCount() / totalBlocks) * 100);
                this.edge.endedAt = Date.now();
                this.edge.details.coverage = pct;
                this.edge.details.hitCount = hitCount();
                this.edge.details.totalBlocks = totalBlocks;

                if (pct >= 95) {
                    this.edge.status = 'passed';
                    this.edge.notes = 'Digitizer randen 100% responsief';
                } else if (Date.now() - start > TouchTest.EDGE_TIMEOUT_MS) {
                    this.edge.status = pct >= 85 ? 'passed' : 'failed';
                    this.edge.notes = pct >= 85
                        ? 'Digitizer randen voldoende responsief (' + pct + '%)'
                        : 'Digitizer randen niet responsief (' + pct + '%)';
                }

                // Reported under the edge's own id so the desktop shows the edge
                // as settled, not the step it now shares an execution with.
                this.reportProgress(wsClient, 100, this.edge.notes, TouchTest.EDGE_ID, TouchTest.EDGE_NAME);
                resolve();
            };

            const interval = setInterval(() => {
                if (hitCount() >= totalBlocks) {
                    finish();
                } else if (Date.now() - start > TouchTest.EDGE_TIMEOUT_MS) {
                    finish();
                }
            }, 100);
        });
    }

    /** Marks the edge half as skipped without touching the grid's own verdict. */
    skipEdge(reason) {
        this.edge.status = 'skipped';
        this.edge.notes = reason;
        this.edge.endedAt = Date.now();
    }

    /**
     * Settles the edge half when the runner cuts the step short.
     *
     * The runner's failsafe and its skip button act on the step, not on the two
     * halves of it, so without this the edge row would stay pending on a run that
     * has moved on: the desktop would keep counting it as unfinished, and the next
     * reload would offer to resume a step that was never completed. The grid keeps
     * whatever the runner set, so a good screen coverage is not turned into a fail
     * by a skip that happened during the edges.
     */
    settleEdgeFromRunner(status, notes) {
        if (this.edge.status !== 'pending' && this.edge.status !== 'running') return;

        this.edge.status = status;
        this.edge.notes = notes;
        this.edge.endedAt = this.edge.startedAt ? Date.now() : this.edge.endedAt;
    }
}
