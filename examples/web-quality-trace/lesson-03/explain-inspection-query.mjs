import assert from "node:assert/strict";
import { performance } from "node:perf_hooks";
import { DatabaseSync } from "node:sqlite";

const db = new DatabaseSync(":memory:");
const rowCount = 24000;
const repetitions = 40;
const epochTicks = 639295200000000000n;
try {
  db.exec(`
    CREATE TABLE Inspections (
      Id TEXT PRIMARY KEY NOT NULL,
      Station TEXT NOT NULL,
      ImportedTicks INTEGER NOT NULL,
      Result TEXT NOT NULL,
      RoiMean REAL NOT NULL
    );
    CREATE INDEX IX_Inspections_ImportedTicks_Id ON Inspections (ImportedTicks, Id);
  `);
  db.prepare(`
    WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < ?)
    INSERT INTO Inspections
    SELECT printf('cycle-%06d', n), printf('station-%02d', n % 24),
           ? + (n / 2) * 10000000,
           CASE WHEN (n / 24) % 4 = 0 THEN 'NG' ELSE 'OK' END,
           CASE WHEN (n / 24) % 4 = 0 THEN 75 ELSE 220 END
    FROM seq
  `).run(rowCount, epochTicks);
  db.exec("ANALYZE");

  const filter = "FROM Inspections WHERE Station = ? AND Result = ? AND ImportedTicks >= ? AND ImportedTicks < ?";
  const values = ["station-07", "NG", epochTicks + 10000000000n, epochTicks + 110000000000n];
  const listSql = `SELECT Id, Station, ImportedTicks, Result, RoiMean ${filter}
    ORDER BY ImportedTicks DESC, Id DESC LIMIT ? OFFSET ?`;
  const countSql = "SELECT COUNT(*) AS total " + filter;
  const pageArgs = [...values, 10, 20];
  const unfilteredSql = "SELECT Id FROM Inspections ORDER BY ImportedTicks DESC, Id DESC LIMIT 10";

  function plan(sql, args = []) {
    return db.prepare("EXPLAIN QUERY PLAN " + sql).all(...args).map(step => step.detail);
  }

  function observe() {
    const list = db.prepare(listSql);
    list.setReadBigInts(true);
    const count = db.prepare(countSql);
    const read = () => ({ rows: list.all(...pageArgs), total: count.get(...values).total });
    const result = read();
    const times = [];
    for (let i = 0; i < repetitions; i++) {
      const start = performance.now();
      read();
      times.push(performance.now() - start);
    }
    times.sort((a, b) => a - b);
    const middle = times.length / 2;
    return {
      result,
      listPlan: plan(listSql, pageArgs),
      countPlan: plan(countSql, values),
      unfilteredPlan: plan(unfilteredSql),
      medianMs: Number(((times[middle - 1] + times[middle]) / 2).toFixed(3)),
    };
  }

  const before = observe();
  db.exec(`
    CREATE INDEX IX_Inspections_Station_Result_ImportedTicks_Id
      ON Inspections (Station, Result, ImportedTicks, Id);
    ANALYZE;
  `);
  const after = observe();
  assert.deepEqual(after.result, before.result);
  assert.equal(after.result.rows.length, 10);

  function report({ listPlan, countPlan, unfilteredPlan, medianMs }) {
    return { listPlan, countPlan, unfilteredPlan, medianMs };
  }
  console.log(JSON.stringify({
    sqliteVersion: db.prepare("SELECT sqlite_version() AS version").get().version,
    rowCount, repetitions, matched: after.result.total,
    pageRows: after.result.rows.length, firstId: after.result.rows[0].Id,
    resultsEqual: true, before: report(before), after: report(after),
  }, null, 2));
} finally {
  db.close();
}
