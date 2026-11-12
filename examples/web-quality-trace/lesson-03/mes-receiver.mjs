import { createServer } from "node:http";
import { DatabaseSync } from "node:sqlite";
import { randomUUID } from "node:crypto";
import { pathToFileURL } from "node:url";

function readBody(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    request.on("data", chunk => {
      size += chunk.length;
      if (size <= 4096) chunks.push(chunk);
    });
    request.on("end", () => {
      if (size > 4096) reject(new Error("Request exceeds 4096 bytes."));
      else {
        try { resolve(JSON.parse(Buffer.concat(chunks).toString("utf8"))); }
        catch { reject(new Error("Invalid JSON.")); }
      }
    });
    request.on("error", reject);
  });
}

export function createMesReceiver(database, { afterCommit } = {}) {
  const db = new DatabaseSync(database);
  db.exec(`
    CREATE TABLE IF NOT EXISTS AcceptedResults (
      EventKey TEXT PRIMARY KEY NOT NULL,
      InspectionId TEXT NOT NULL,
      Result TEXT NOT NULL,
      ReceiptId TEXT UNIQUE NOT NULL,
      AcceptedAt TEXT NOT NULL
    );
  `);
  const read = db.prepare("SELECT * FROM AcceptedResults WHERE EventKey = ?");
  const insert = db.prepare("INSERT INTO AcceptedResults VALUES (?, ?, ?, ?, ?)");

  const server = createServer({ requestTimeout: 5000, headersTimeout: 5000 }, async (request, response) => {
    response.setHeader("Content-Type", "application/json");
    response.setHeader("Cache-Control", "no-store");
    function reply(status, body) {
      response.writeHead(status);
      response.end(JSON.stringify(body));
    }

    if (request.method !== "POST" || request.url !== "/results") {
      reply(404, { error: "Unknown endpoint." });
      return;
    }

    try {
      const input = await readBody(request);
      if (!input || typeof input.streamId !== "string" || !/^[0-9a-f]{32}$/.test(input.streamId)
        || typeof input.sequence !== "string" || !/^[1-9]\d{0,18}$/.test(input.sequence)
        || typeof input.inspectionId !== "string" || !/^[0-9a-f]{32}-\d{10,19}$/.test(input.inspectionId)
        || !["OK", "NG"].includes(input.result)) {
        reply(400, { error: "Invalid result payload." });
        return;
      }
      const key = input.streamId + ":" + input.sequence;
      if (request.headers["idempotency-key"] !== key) {
        reply(400, { error: "Idempotency key does not match the event." });
        return;
      }

      let receipt;
      let created = false;
      db.exec("BEGIN IMMEDIATE");
      try {
        receipt = read.get(key);
        if (receipt && (receipt.InspectionId !== input.inspectionId || receipt.Result !== input.result)) {
          db.exec("ROLLBACK");
          reply(409, { error: "Existing key has a different result payload." });
          return;
        }
        if (!receipt) {
          insert.run(key, input.inspectionId, input.result, randomUUID(), new Date().toISOString());
          receipt = read.get(key);
          created = true;
        }
        db.exec("COMMIT");
      } catch (error) {
        db.exec("ROLLBACK");
        throw error;
      }

      // The test can cut the connection after persistence and before the reply.
      if (afterCommit?.({ request, response, receipt, created })) return;
      reply(created ? 201 : 200, receipt);
    } catch (error) {
      reply(error.code ? 503 : 400, { error: error.message });
    }
  });
  server.on("close", () => db.close());
  return server;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [database] = process.argv.slice(2);
  if (!database) {
    console.error("Usage: node mes-receiver.mjs <mes.db>");
    process.exitCode = 2;
  } else {
    const server = createMesReceiver(database);
    server.on("error", error => { console.error(error.message); process.exitCode = 1; });
    server.listen(43115, "127.0.0.1", () => console.log("MES receiver: http://127.0.0.1:43115/results"));
  }
}
