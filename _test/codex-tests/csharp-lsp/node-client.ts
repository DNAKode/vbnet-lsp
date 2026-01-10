import { spawn } from "child_process";
import * as fs from "fs";
import * as net from "net";
import * as path from "path";
import { TextDecoder } from "util";
import {
    createMessageConnection,
    SocketMessageReader,
    SocketMessageWriter,
} from "vscode-jsonrpc/node";
import * as RoslynProtocol from "../../../_external/vscode-csharp/src/lsptoolshost/server/roslynProtocol";

type Options = {
    serverPath: string;
    dotnetPath: string;
    logDirectory: string;
    rootPath?: string;
    solutionPath?: string;
    timeoutSeconds: number;
    noop: boolean;
    raw: boolean;
};

function parseArgs(argv: string[]): Options {
    const options: Options = {
        serverPath: "",
        dotnetPath: "dotnet",
        logDirectory: path.join(process.cwd(), "_test", "codex-tests", "csharp-lsp", "logs"),
        timeoutSeconds: 30,
        noop: false,
        raw: false,
    };

    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--serverPath" && argv[i + 1]) {
            options.serverPath = argv[++i];
        } else if (arg === "--dotnetPath" && argv[i + 1]) {
            options.dotnetPath = argv[++i];
        } else if (arg === "--logDirectory" && argv[i + 1]) {
            options.logDirectory = argv[++i];
        } else if (arg === "--rootPath" && argv[i + 1]) {
            options.rootPath = argv[++i];
        } else if (arg === "--solutionPath" && argv[i + 1]) {
            options.solutionPath = argv[++i];
        } else if (arg === "--timeoutSeconds" && argv[i + 1]) {
            options.timeoutSeconds = Number(argv[++i]);
        } else if (arg === "--noop") {
            options.noop = true;
        } else if (arg === "--raw") {
            options.raw = true;
        }
    }

    if (!options.serverPath) {
        throw new Error("Missing required --serverPath argument.");
    }

    return options;
}

async function main() {
    const options = parseArgs(process.argv.slice(2));

    const serverArgs = [
        options.serverPath,
        "--logLevel",
        "Information",
        "--extensionLogDirectory",
        path.resolve(options.logDirectory),
    ];

    fs.mkdirSync(options.logDirectory, { recursive: true });

    const server = spawn(options.dotnetPath, serverArgs, {
        stdio: ["ignore", "pipe", "pipe"],
        windowsHide: true,
    });

    server.stderr.on("data", (data) => {
        process.stderr.write(`[server stderr] ${data}`);
    });
    server.on("exit", (code, signal) => {
        process.stderr.write(`Server exited (code=${code}, signal=${signal ?? "none"}).\n`);
    });

    const pipeName = await readPipeName(server.stdout!);
    process.stdout.write(`pipeName: ${pipeName}\n`);
    const socket = net.createConnection(pipeName);
    socket.setNoDelay(true);

    await new Promise<void>((resolve, reject) => {
        socket.once("connect", () => resolve());
        socket.once("error", (err) => reject(err));
    });
    socket.on("end", () => {
        process.stderr.write("Socket ended by server.\n");
    });
    socket.on("close", () => {
        process.stderr.write("Socket closed.\n");
    });
    socket.on("error", (err) => {
        process.stderr.write(`Socket error: ${err.message}\n`);
    });

    if (options.raw) {
        await runRawHandshake(socket, options);
        socket.end();
        server.kill();
        return;
    }

    const connection = createMessageConnection(
        new SocketMessageReader(socket, "utf-8"),
        new SocketMessageWriter(socket, "utf-8")
    );
    connection.onNotification("window/logMessage", (params) => {
        if (params?.message) {
            process.stderr.write(`[server log] ${params.message}\n`);
        }
    });
    connection.onNotification("window/showMessage", (params) => {
        if (params?.message) {
            process.stderr.write(`[server message] ${params.message}\n`);
        }
    });
    connection.onRequest("workspace/configuration", (params) => {
        const items = Array.isArray(params?.items) ? params.items : [];
        return items.map(() => ({}));
    });
    connection.onRequest("client/registerCapability", () => null);
    connection.onRequest("client/unregisterCapability", () => null);
    connection.onRequest("window/workDoneProgress/create", () => null);
    connection.onClose(() => {
        process.stderr.write("LSP connection closed.\n");
    });
    connection.onError((err) => {
        process.stderr.write(`LSP connection error: ${err instanceof Error ? err.message : String(err)}\n`);
    });
    connection.listen();
    let socketClosed = false;
    socket.on("close", () => {
        socketClosed = true;
    });

    if (!options.noop) {
        const rootUri = options.rootPath ? toFileUri(options.rootPath) : null;
        const initParams = {
            processId: process.pid,
            rootUri,
            capabilities: {},
            clientInfo: { name: "CodexCSharpLspNodeClient", version: "0.1" },
        };

        process.stderr.write("Sending initialize...\n");
        await withTimeout(connection.sendRequest("initialize", initParams), options.timeoutSeconds);
        process.stderr.write("Initialize completed.\n");

        await Promise.resolve(connection.sendNotification("initialized", {}));

        if (options.solutionPath) {
            const solutionUri = toFileUri(options.solutionPath);
            await Promise.resolve(
                connection.sendNotification(RoslynProtocol.OpenSolutionNotification.type.method, {
                    solution: solutionUri,
                })
            );
        }

        try {
            process.stderr.write("Sending shutdown...\n");
            await withTimeout(connection.sendRequest("shutdown", {}), options.timeoutSeconds);
            process.stderr.write("Shutdown completed.\n");

            if (!socketClosed && !socket.destroyed && !socket.writableEnded) {
                try {
                    await Promise.resolve(connection.sendNotification("exit", {}));
                } catch (err) {
                    process.stderr.write(`Exit notification failed: ${err instanceof Error ? err.message : String(err)}\n`);
                }
            }
        } catch (err) {
            process.stderr.write(`Shutdown failed: ${err instanceof Error ? err.message : String(err)}\n`);
        }
    }

    if (!socketClosed) {
        await waitForSocketClose(socket, Math.min(options.timeoutSeconds, 5));
    }
    if (!socket.destroyed) {
        socket.end();
    }
    server.kill();
}

async function readPipeName(stdout: NodeJS.ReadableStream): Promise<string> {
    const decoder = new TextDecoder("utf-8");
    const regex = /\{"pipeName":"[^"]+"\}/;
    let buffer = "";

    return await new Promise((resolve, reject) => {
        stdout.on("data", (chunk) => {
            buffer += decoder.decode(chunk as Buffer, { stream: true });
            const match = buffer.match(regex);
            if (match) {
                const json = JSON.parse(match[0]);
                resolve(json.pipeName as string);
            }
        });

        stdout.on("end", () => reject(new Error("Server stdout closed before pipe name was received.")));
        stdout.on("error", (err) => reject(err));
    });
}

function toFileUri(filePath: string): string {
    const full = path.resolve(filePath);
    const uri = new URL(`file:///${full.replace(/\\/g, "/")}`);
    return uri.toString();
}

async function withTimeout<T>(promise: Thenable<T>, seconds: number): Promise<T> {
    let timeoutHandle: NodeJS.Timeout | undefined;
    const timeout = new Promise<never>((_, reject) => {
        timeoutHandle = setTimeout(() => reject(new Error("Operation timed out.")), seconds * 1000);
    });

    try {
        return await Promise.race([promise, timeout]);
    } finally {
        if (timeoutHandle) {
            clearTimeout(timeoutHandle);
        }
    }
}

async function runRawHandshake(socket: net.Socket, options: Options): Promise<void> {
    const rootUri = options.rootPath ? toFileUri(options.rootPath) : null;
    const initParams = {
        processId: process.pid,
        rootUri,
        capabilities: {},
        clientInfo: { name: "CodexCSharpLspNodeClient", version: "0.1" },
    };

    await sendRequestRaw(socket, 1, "initialize", initParams, options.timeoutSeconds);
    sendNotificationRaw(socket, "initialized", {});

    if (options.solutionPath) {
        const solutionUri = toFileUri(options.solutionPath);
        sendNotificationRaw(socket, RoslynProtocol.OpenSolutionNotification.type.method, {
            solution: solutionUri,
        });
    }

    await sendRequestRaw(socket, 2, "shutdown", {}, options.timeoutSeconds);
    sendNotificationRaw(socket, "exit", {});
}

async function waitForSocketClose(socket: net.Socket, timeoutSeconds: number): Promise<void> {
    if (socket.destroyed) {
        return;
    }

    await new Promise<void>((resolve) => {
        const timeout = setTimeout(resolve, timeoutSeconds * 1000);
        const onClose = () => {
            clearTimeout(timeout);
            resolve();
        };
        socket.once("close", onClose);
    });
}

function sendNotificationRaw(socket: net.Socket, method: string, params: unknown) {
    const payload = JSON.stringify({ jsonrpc: "2.0", method, params });
    const header = `Content-Length: ${Buffer.byteLength(payload, "utf8")}\r\n\r\n`;
    socket.write(Buffer.from(header, "utf8"));
    socket.write(Buffer.from(payload, "utf8"));
}

async function sendRequestRaw(
    socket: net.Socket,
    id: number,
    method: string,
    params: unknown,
    timeoutSeconds: number
): Promise<void> {
    const payload = JSON.stringify({ jsonrpc: "2.0", id, method, params });
    const header = `Content-Length: ${Buffer.byteLength(payload, "utf8")}\r\n\r\n`;
    socket.write(Buffer.from(header, "utf8"));
    socket.write(Buffer.from(payload, "utf8"));

    await waitForResponse(socket, id, timeoutSeconds);
}

async function waitForResponse(socket: net.Socket, id: number, timeoutSeconds: number): Promise<void> {
    let buffer = Buffer.alloc(0);

    return await new Promise<void>((resolve, reject) => {
        const timeout = setTimeout(() => {
            cleanup();
            reject(new Error("Timed out waiting for response."));
        }, timeoutSeconds * 1000);

        const onData = (chunk: Buffer) => {
            buffer = Buffer.concat([buffer, chunk]);

            while (true) {
                const headerEnd = buffer.indexOf("\r\n\r\n");
                if (headerEnd === -1) {
                    return;
                }

                const header = buffer.slice(0, headerEnd).toString("utf8");
                const match = header.match(/Content-Length: (\d+)/i);
                if (!match) {
                    return;
                }

                const length = Number(match[1]);
                const bodyStart = headerEnd + 4;
                const bodyEnd = bodyStart + length;
                if (buffer.length < bodyEnd) {
                    return;
                }

                const body = buffer.slice(bodyStart, bodyEnd).toString("utf8");
                buffer = buffer.slice(bodyEnd);

                const json = JSON.parse(body);
                if (json.id === id) {
                    cleanup();
                    resolve();
                    return;
                }
            }
        };

        const onError = (err: Error) => {
            cleanup();
            reject(err);
        };

        const onClose = () => {
            cleanup();
            reject(new Error("Socket closed before response."));
        };

        const cleanup = () => {
            clearTimeout(timeout);
            socket.off("data", onData);
            socket.off("error", onError);
            socket.off("close", onClose);
        };

        socket.on("data", onData);
        socket.on("error", onError);
        socket.on("close", onClose);
    });
}

main().catch((err) => {
    console.error(err instanceof Error ? err.message : String(err));
    process.exit(1);
});
