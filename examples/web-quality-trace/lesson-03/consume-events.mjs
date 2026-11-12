import { DatabaseSync } from "node:sqlite";
import { pathToFileURL } from "node:url";

export async function consumeInspectionEvents(origin, checkpointFile, limit = 20, mesOrigin) {
  const source = new URL(origin);
  if (source.protocol !== "http:" || source.hostname !== "127.0.0.1") {
    throw new Error("Use the lesson API on http://127.0.0.1.");
  }
  if (!Number.isInteger(limit) || limit < 1 || limit > 100) throw new Error("Invalid page size.");

  let destination = "";
  if (mesOrigin) {
    const target = new URL(mesOrigin);
    if (target.protocol !== "http:" || target.hostname !== "127.0.0.1" ||
        target.username || target.password || target.pathname !== "/" || target.search || target.hash) {
      throw new Error("Use a local MES origin such as http://127.0.0.1:43115.");
    }
    destination = target.origin;
  }

  const db = new DatabaseSync(checkpointFile);
  try {
    db.exec(`
      CREATE TABLE IF NOT EXISTS Checkpoint (
        Id INTEGER PRIMARY KEY CHECK (Id = 1),
        StreamId TEXT NOT NULL,
        After TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS HttpDeliveryReceipts (
        EventKey TEXT PRIMARY KEY NOT NULL,
        ReceiptId TEXT NOT NULL,
        AcceptedAt TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS DeliveryTarget (
        Id INTEGER PRIMARY KEY CHECK (Id = 1),
        Origin TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS Receipts (
        InspectionId TEXT PRIMARY KEY NOT NULL,
        Sequence TEXT UNIQUE NOT NULL,
        Result TEXT NOT NULL
      );
    `);

    const readCheckpoint = db.prepare("SELECT StreamId, After FROM Checkpoint WHERE Id = 1");
    const writeCheckpoint = db.prepare(`
      INSERT INTO Checkpoint (Id, StreamId, After) VALUES (1, ?, ?)
      ON CONFLICT(Id) DO UPDATE SET After = excluded.After
    `);
    const writeReceipt = db.prepare("INSERT INTO Receipts (InspectionId, Sequence, Result) VALUES (?, ?, ?)");
    let processed = 0;
    let checkpoint;
    db.exec("BEGIN IMMEDIATE");
    try {
      checkpoint = readCheckpoint.get();
      let target = db.prepare("SELECT Origin FROM DeliveryTarget WHERE Id = 1").get();
      if (!target) {
        if (destination && checkpoint && checkpoint.After !== "0") {
          throw new Error("Use a new checkpoint file for HTTP delivery.");
        }
        db.prepare("INSERT INTO DeliveryTarget VALUES (1, ?)").run(destination);
        target = { Origin: destination };
      }
      if (target.Origin !== destination) throw new Error("Checkpoint delivery target does not match.");
      db.exec("COMMIT");
    } catch (error) {
      db.exec("ROLLBACK");
      throw error;
    }

    for (;;) {
      const after = checkpoint?.After ?? "0";
      const url = new URL("/api/inspection-events", source);
      url.search = new URLSearchParams({ after, limit: String(limit) }).toString();
      const response = await fetch(url, { signal: AbortSignal.timeout(5000), redirect: "error" });
      if (!response.ok) throw new Error(`Feed request failed (HTTP ${response.status}).`);

      const page = await response.json();
      if (typeof page.streamId !== "string" || !/^[0-9a-f]{32}$/.test(page.streamId)
        || !Array.isArray(page.items) || page.items.length > limit || typeof page.hasMore !== "boolean") {
        throw new Error("Invalid feed response.");
      }
      if (checkpoint && page.streamId !== checkpoint.StreamId) {
        throw new Error("This checkpoint belongs to a different event stream.");
      }

      let next = after;
      for (const event of page.items) {
        if (typeof event.sequence !== "string" || !/^[1-9]\d{0,18}$/.test(event.sequence)
          || BigInt(event.sequence) <= BigInt(next) || typeof event.inspectionId !== "string"
          || (event.result !== "OK" && event.result !== "NG")) {
          throw new Error("Invalid inspection event.");
        }
        next = event.sequence;
      }
      if (page.nextAfter !== next || (page.hasMore && next === after)) {
        throw new Error("Feed cursor did not match the batch.");
      }

      const delivered = [];
      if (destination) {
        for (const event of page.items) {
          const key = page.streamId + ":" + event.sequence;
          const response = await fetch(new URL("/results", destination), {
            method: "POST",
            headers: { "Content-Type": "application/json", "Idempotency-Key": key },
            body: JSON.stringify({
              streamId: page.streamId, sequence: event.sequence,
              inspectionId: event.inspectionId, result: event.result,
            }),
            signal: AbortSignal.timeout(5000), redirect: "error",
          });
          if (!response.ok) throw new Error(`MES delivery failed (HTTP ${response.status}).`);
          const receipt = await response.json();
          if (receipt.EventKey !== key || receipt.InspectionId !== event.inspectionId ||
              receipt.Result !== event.result || typeof receipt.ReceiptId !== "string" ||
              !/^[0-9a-f-]{36}$/.test(receipt.ReceiptId) || typeof receipt.AcceptedAt !== "string" ||
              !Number.isFinite(Date.parse(receipt.AcceptedAt))) {
            throw new Error("MES receipt does not match the delivered event.");
          }
          delivered.push(receipt);
        }
      }

      db.exec("BEGIN IMMEDIATE");
      try {
        const current = readCheckpoint.get();
        if (current?.After !== checkpoint?.After || current?.StreamId !== checkpoint?.StreamId) {
          throw new Error("Checkpoint changed while the batch was in flight. Run the consumer again.");
        }
        for (const event of page.items) writeReceipt.run(event.inspectionId, event.sequence, event.result);
        const saveDelivery = db.prepare("INSERT INTO HttpDeliveryReceipts VALUES (?, ?, ?)");
        for (const receipt of delivered) saveDelivery.run(receipt.EventKey, receipt.ReceiptId, receipt.AcceptedAt);
        writeCheckpoint.run(page.streamId, next);
        db.exec("COMMIT");
      } catch (error) {
        db.exec("ROLLBACK");
        throw error;
      }

      processed += page.items.length;
      checkpoint = { StreamId: page.streamId, After: next };
      if (!page.hasMore) return { processed, after: next };
    }
  } finally {
    db.close();
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [origin, checkpointFile, mesOrigin] = process.argv.slice(2);
  if (!origin || !checkpointFile) {
    console.error("Usage: node consume-events.mjs http://127.0.0.1:43111 <consumer.db> [http://127.0.0.1:43115]");
    process.exitCode = 2;
  } else {
    try {
      console.log(await consumeInspectionEvents(origin, checkpointFile, 20, mesOrigin));
    } catch (error) {
      console.error(error.message);
      process.exitCode = 1;
    }
  }
}
