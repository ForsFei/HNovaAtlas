import { DatabaseSync } from "node:sqlite";
import { pathToFileURL } from "node:url";

function readDatabase(path, read) {
  const db = new DatabaseSync(path, { readOnly: true });
  try {
    db.exec("BEGIN");
    return read(db);
  } finally {
    db.close();
  }
}

export function auditDelivery(sourceFile, consumerFile, mesFile) {
  const { streamId, events } = readDatabase(sourceFile, db => ({
    streamId: db.prepare("SELECT StreamId FROM InspectionEventStream WHERE Id = 1").get().StreamId,
    events: db.prepare(`
      SELECT CAST(e.Sequence AS TEXT) AS Sequence, i.Id, i.Result
      FROM InspectionEvents e JOIN Inspections i ON i.Id = e.InspectionId ORDER BY e.Sequence
    `).all(),
  }));
  const local = readDatabase(consumerFile, db => ({
    checkpoint: db.prepare("SELECT StreamId, After FROM Checkpoint WHERE Id = 1").get(),
    target: db.prepare("SELECT Origin FROM DeliveryTarget WHERE Id = 1").get()?.Origin,
    receipts: db.prepare("SELECT Sequence, InspectionId, Result FROM Receipts").all(),
    deliveries: db.prepare("SELECT EventKey, ReceiptId, AcceptedAt FROM HttpDeliveryReceipts").all(),
  }));

  const after = local.checkpoint?.After ?? "0";
  const head = events.at(-1)?.Sequence ?? "0";
  const counts = { confirmed: 0, pending: 0, awaitingCheckpoint: 0 };
  const issues = [];
  const report = { streamId, after, head, counts, issues };
  if (!local.target) {
    issues.push({ code: "NotHttpConsumer" });
    return report;
  }
  if (local.checkpoint && local.checkpoint.StreamId !== streamId) {
    issues.push({ code: "StreamMismatch", checkpointStreamId: local.checkpoint.StreamId });
    return report;
  }
  if (!/^(0|[1-9]\d*)$/.test(after)) {
    issues.push({ code: "InvalidCheckpoint" });
    return report;
  }
  if (BigInt(after) > BigInt(head)) issues.push({ code: "CheckpointAhead" });
  else if (after !== "0" && !events.some(event => event.Sequence === after)) {
    issues.push({ code: "UnknownCheckpoint" });
  }

  const prefix = streamId + ":";
  const remote = readDatabase(mesFile, db => db.prepare(`
    SELECT EventKey, InspectionId, Result, ReceiptId, AcceptedAt
    FROM AcceptedResults WHERE substr(EventKey, 1, 33) = ?
  `).all(prefix));
  const remoteByKey = new Map(remote.map(row => [row.EventKey, row]));
  const receiptsBySequence = new Map(local.receipts.map(row => [row.Sequence, row]));
  const deliveriesByKey = new Map(local.deliveries.map(row => [row.EventKey, row]));

  for (const event of events) {
    const sequence = event.Sequence;
    const key = prefix + sequence;
    const receipt = receiptsBySequence.get(sequence);
    const delivery = deliveriesByKey.get(key);
    const accepted = remoteByKey.get(key);
    receiptsBySequence.delete(sequence);
    deliveriesByKey.delete(key);
    remoteByKey.delete(key);

    if ((receipt && (receipt.InspectionId !== event.Id || receipt.Result !== event.Result)) ||
        (accepted && (accepted.InspectionId !== event.Id || accepted.Result !== event.Result))) {
      issues.push({ code: "PayloadConflict", sequence });
    } else if (delivery && accepted &&
        (delivery.ReceiptId !== accepted.ReceiptId || delivery.AcceptedAt !== accepted.AcceptedAt)) {
      issues.push({ code: "ReceiptConflict", sequence });
    } else if (BigInt(sequence) <= BigInt(after)) {
      if (receipt && delivery && accepted) counts.confirmed++;
      else issues.push({ code: "CheckpointGap", sequence });
    } else if (receipt || delivery) {
      issues.push({ code: "LocalReceiptAhead", sequence });
    } else if (accepted) {
      counts.awaitingCheckpoint++;
    } else {
      counts.pending++;
    }
  }

  for (const sequence of receiptsBySequence.keys()) issues.push({ code: "UnknownLocalSequence", sequence });
  for (const eventKey of deliveriesByKey.keys()) issues.push({ code: "UnknownLocalDelivery", eventKey });
  for (const eventKey of remoteByKey.keys()) issues.push({ code: "UnknownMesEvent", eventKey });
  return report;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const paths = process.argv.slice(2);
  if (paths.length !== 3) {
    console.error("Usage: node audit-delivery.mjs <trace.db> <consumer.db> <mes.db>");
    process.exitCode = 2;
  } else {
    try {
      const report = auditDelivery(...paths);
      console.log(JSON.stringify(report, null, 2));
      if (report.issues.length) process.exitCode = 3;
    } catch (error) {
      console.error(error.message);
      process.exitCode = 1;
    }
  }
}
