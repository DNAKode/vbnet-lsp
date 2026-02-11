'use strict';

const cp = require('child_process');
const fs = require('fs');

function parseArgs(argv) {
    const args = argv.slice(2);
    const debuggerArgs = [];
    let debuggerPath = '';
    let separatorFound = false;

    for (let i = 0; i < args.length; i++) {
        const token = args[i];
        if (token === '--') {
            separatorFound = true;
            for (let j = i + 1; j < args.length; j++) {
                debuggerArgs.push(args[j]);
            }
            break;
        }

        if (token === '--debugger') {
            if (i + 1 >= args.length) {
                throw new Error('--debugger requires a value');
            }
            debuggerPath = args[i + 1];
            i++;
            continue;
        }
    }

    if (!debuggerPath) {
        throw new Error('Missing required --debugger argument');
    }

    if (!separatorFound) {
        throw new Error('Missing "--" separator before debugger arguments');
    }

    return { debuggerPath, debuggerArgs };
}

class DapStream {
    constructor(readable, onMessage) {
        this.readable = readable;
        this.onMessage = onMessage;
        this.buffer = Buffer.alloc(0);
        this.contentLength = null;
        this.readable.on('data', (chunk) => this.push(chunk));
    }

    push(chunk) {
        this.buffer = Buffer.concat([this.buffer, chunk]);
        while (true) {
            if (this.contentLength === null) {
                const headerEnd = this.buffer.indexOf('\r\n\r\n');
                if (headerEnd === -1) {
                    return;
                }

                const header = this.buffer.slice(0, headerEnd).toString('utf8');
                const match = /Content-Length:\s*(\d+)/i.exec(header);
                if (!match) {
                    throw new Error('Invalid DAP header: missing Content-Length');
                }

                this.contentLength = Number(match[1]);
                this.buffer = this.buffer.slice(headerEnd + 4);
            }

            if (this.buffer.length < this.contentLength) {
                return;
            }

            const body = this.buffer.slice(0, this.contentLength).toString('utf8');
            this.buffer = this.buffer.slice(this.contentLength);
            this.contentLength = null;

            let message;
            try {
                message = JSON.parse(body);
            } catch (error) {
                throw new Error(`Failed to parse DAP payload: ${error}`);
            }

            this.onMessage(message);
        }
    }
}

function writeMessage(stream, message) {
    const body = Buffer.from(JSON.stringify(message), 'utf8');
    const header = Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, 'utf8');
    stream.write(Buffer.concat([header, body]));
}

function isNoInterfaceStackTraceFailure(message) {
    if (!message || message.type !== 'response' || message.command !== 'stackTrace' || message.success !== false) {
        return false;
    }

    if (typeof message.message !== 'string') {
        return false;
    }

    return message.message.includes('0x80004002');
}

function logWarningOnce(state, text) {
    if (state.warned) {
        return;
    }

    state.warned = true;
    const outputEvent = {
        seq: 0,
        type: 'event',
        event: 'output',
        body: {
            category: 'stderr',
            output: `${text}\n`
        }
    };
    writeMessage(process.stdout, outputEvent);
}

function main() {
    const { debuggerPath, debuggerArgs } = parseArgs(process.argv);
    if (!fs.existsSync(debuggerPath)) {
        throw new Error(`Debugger executable not found at ${debuggerPath}`);
    }

    const child = cp.spawn(debuggerPath, debuggerArgs, {
        stdio: ['pipe', 'pipe', 'pipe']
    });

    const requestMap = new Map();
    const workaroundState = { warned: false };

    new DapStream(process.stdin, (message) => {
        if (message && message.type === 'request' && typeof message.seq === 'number') {
            requestMap.set(message.seq, message);
        }
        writeMessage(child.stdin, message);
    });

    new DapStream(child.stdout, (message) => {
        if (message && message.type === 'response' && typeof message.request_seq === 'number') {
            const originalRequest = requestMap.get(message.request_seq);
            if (isNoInterfaceStackTraceFailure(message) && originalRequest?.command === 'stackTrace') {
                logWarningOnce(
                    workaroundState,
                    '[vbnet] netcoredbg stackTrace workaround applied (0x80004002); returning empty stack.'
                );

                message.success = true;
                delete message.message;
                message.body = {
                    stackFrames: [],
                    totalFrames: 0
                };
            }

            requestMap.delete(message.request_seq);
        }

        writeMessage(process.stdout, message);
    });

    child.stderr.on('data', (chunk) => {
        try {
            process.stderr.write(chunk);
        } catch {
            // ignore stderr forwarding failures
        }
    });

    child.on('error', (error) => {
        process.stderr.write(`[vbnet] netcoredbg proxy spawn error: ${String(error)}\n`);
        process.exit(1);
    });

    child.on('exit', (code, signal) => {
        if (signal) {
            process.kill(process.pid, signal);
            return;
        }
        process.exit(code ?? 0);
    });

    process.stdin.on('end', () => {
        child.stdin.end();
    });

    process.on('SIGINT', () => {
        child.kill('SIGINT');
    });

    process.on('SIGTERM', () => {
        child.kill('SIGTERM');
    });
}

try {
    main();
} catch (error) {
    process.stderr.write(`[vbnet] netcoredbg proxy fatal error: ${String(error)}\n`);
    process.exit(1);
}
