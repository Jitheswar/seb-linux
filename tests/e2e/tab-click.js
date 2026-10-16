// Drives a real (CDP-level, i.e. trusted) mouse click on the "open tab" link of the
// exam page. Headless Chrome blocks window.open() without a user gesture, so the
// new-tab part of the e2e test needs a genuine input event.
//
// Usage: node tab-click.js <profile-directory> <exam-base-url>
//
// The profile directory must be the one used by the running browser (the client
// logs it as "Launching browser '...' with profile '<dir>'"). The script connects
// to the exam page target with its own DevTools session and exits after the click.

const fs = require("fs");

const [profileDir, examBaseUrl] = process.argv.slice(2, 4);

if (!profileDir || !examBaseUrl) {
    console.error("usage: node tab-click.js <profile-directory> <exam-base-url>");
    process.exit(2);
}

let nextId = 1;
const pending = new Map();

function makeWs(url) {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(url);
        ws.onmessage = (m) => {
            const msg = JSON.parse(m.data);
            if (msg.id && pending.has(msg.id)) {
                const p = pending.get(msg.id);
                pending.delete(msg.id);
                (msg.error
                    ? (res, rej) => rej(new Error(JSON.stringify(msg.error)))
                    : (res, rej) => res(msg.result))(p.resolve, p.reject);
            }
        };
        ws.onopen = () => resolve({
            close: () => { try { ws.close(); } catch (e) { } },
            call: (method, params) => new Promise((resolve2, reject2) => {
                const id = nextId++;
                ws.send(JSON.stringify({ id, method, params: params || {} }));
                pending.set(id, { resolve: resolve2, reject: reject2 });
            })
        });
        ws.onerror = () => reject(new Error("websocket error"));
    });
}

async function findExamTarget() {
    for (let i = 0; i < 300; i++) {
        try {
            const port = fs.readFileSync(`${profileDir}/DevToolsActivePort`, "utf8").trim().split("\n")[0];
            const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
            const page = targets.find((t) => t.type === "page" && t.url.startsWith(examBaseUrl));

            if (page) {
                return page;
            }
        } catch (e) {
            // The browser is not up yet.
        }

        await new Promise((r) => setTimeout(r, 300));
    }

    throw new Error("the exam page target was not found");
}

(async () => {
    const target = await findExamTarget();
    const client = await makeWs(target.webSocketDebuggerUrl);

    // Wait until the tab link exists and has a position.
    let point = null;

    for (let i = 0; i < 200; i++) {
        try {
            const result = await client.call("Runtime.evaluate", {
                expression: "(() => { " +
                    "const a = document.getElementById('e2e-tab'); " +
                    "if (!a) { return null; } " +
                    "const b = a.getBoundingClientRect(); " +
                    "return JSON.stringify({ x: b.x + b.width / 2, y: b.y + b.height / 2 }); " +
                    "})()",
                returnByValue: true
            });

            if (result.result && result.result.value) {
                point = JSON.parse(result.result.value);
                break;
            }
        } catch (e) {
            // The page is not ready yet.
        }

        await new Promise((r) => setTimeout(r, 300));
    }

    if (!point) {
        client.close();
        console.error("the tab link never appeared on the exam page");
        process.exit(1);
    }

    await client.call("Input.dispatchMouseEvent", { type: "mousePressed", x: point.x, y: point.y, button: "left", clickCount: 1 });
    await client.call("Input.dispatchMouseEvent", { type: "mouseReleased", x: point.x, y: point.y, button: "left", clickCount: 1 });

    console.log(`clicked the tab link at ${JSON.stringify(point)}`);

    setTimeout(() => {
        client.close();
        process.exit(0);
    }, 2000);
})().catch((e) => {
    console.error(`tab-click: ${e.message}`);
    process.exit(1);
});
