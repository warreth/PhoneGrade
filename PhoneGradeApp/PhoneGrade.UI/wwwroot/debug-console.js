(function() {
    // Check if debug parameter is present in URL or localStorage
    const params = new URLSearchParams(window.location.search);
    const isDebug = params.get('debug') === 'true' || localStorage.getItem('pg_debug') === 'true';
    if (!isDebug) return;

    let overlay = document.getElementById('debug-overlay');
    if (overlay) return;

    overlay = document.createElement('div');
    overlay.id = 'debug-overlay';
    overlay.style.cssText = 'position:fixed;bottom:0;left:0;right:0;height:35%;background:rgba(15,23,42,0.95);color:#4ade80;font-family:ui-monospace,SFMono-Regular,Consolas,monospace;font-size:11px;padding:8px 12px;overflow-y:auto;z-index:99999;border-top:2px solid #2563eb;box-shadow:0 -4px 12px rgba(0,0,0,0.3);';

    const header = document.createElement('div');
    header.style.cssText = 'display:flex;justify-content:space-between;align-items:center;padding-bottom:6px;border-bottom:1px solid #334155;margin-bottom:6px;font-weight:700;color:#f8fafc;';
    header.innerHTML = '<span>PWA Debug Console</span><span style="cursor:pointer;color:#94a3b8;font-size:14px;" onclick="document.getElementById(\'debug-overlay\').style.display=\'none\'">✕ Sluiten</span>';
    overlay.appendChild(header);

    const logContainer = document.createElement('div');
    logContainer.id = 'debug-log-container';
    overlay.appendChild(logContainer);

    function appendLog(msg, color = '#4ade80') {
        const line = document.createElement('div');
        line.style.cssText = 'word-break:break-all;margin-bottom:3px;color:' + color + ';';
        const time = new Date().toISOString().split('T')[1].slice(0, 8);
        line.textContent = '[' + time + '] ' + msg;
        logContainer.appendChild(line);
        overlay.scrollTop = overlay.scrollHeight;
    }

    // Capture global unhandled errors
    window.addEventListener('error', function(e) {
        appendLog('ERROR: ' + e.message + ' at ' + e.filename + ':' + e.lineno, '#f87171');
    });

    window.addEventListener('unhandledrejection', function(e) {
        appendLog('PROMISE REJECTION: ' + (e.reason?.message || e.reason), '#f87171');
    });

    // Intercept console.log and console.error
    const origLog = console.log;
    const origWarn = console.warn;
    const origError = console.error;

    console.log = function(...args) {
        appendLog(args.map(a => typeof a === 'object' ? JSON.stringify(a) : a).join(' '), '#cbd5e1');
        origLog.apply(console, args);
    };

    console.warn = function(...args) {
        appendLog('WARN: ' + args.map(a => typeof a === 'object' ? JSON.stringify(a) : a).join(' '), '#facc15');
        origWarn.apply(console, args);
    };

    console.error = function(...args) {
        appendLog('ERR: ' + args.map(a => typeof a === 'object' ? JSON.stringify(a) : a).join(' '), '#f87171');
        origError.apply(console, args);
    };

    // Intercept window.fetch
    const origFetch = window.fetch;
    window.fetch = async function(...args) {
        const url = typeof args[0] === 'string' ? args[0] : (args[0]?.url || 'unknown');
        const method = (args[1] && args[1].method) || 'GET';
        let body = (args[1] && args[1].body) || '';
        if (typeof body === 'string' && body.length > 80) body = body.slice(0, 80) + '...';

        appendLog('-> ' + method + ' ' + url + (body ? ' ' + body : ''), '#38bdf8');
        try {
            const resp = await origFetch.apply(this, args);
            const statusColor = resp.ok ? '#4ade80' : '#f87171';
            appendLog('<- ' + resp.status + ' ' + method + ' ' + url, statusColor);
            return resp;
        } catch (err) {
            appendLog('<! FAILED ' + method + ' ' + url + ': ' + err.message, '#f87171');
            throw err;
        }
    };

    document.addEventListener('DOMContentLoaded', function() {
        if (!document.body.contains(overlay)) {
            document.body.appendChild(overlay);
        }
    });
    if (document.body) {
        document.body.appendChild(overlay);
    }

    appendLog('Debug console initialized. SessionId: ' + (params.get('sessionId') || 'none'));
})();
