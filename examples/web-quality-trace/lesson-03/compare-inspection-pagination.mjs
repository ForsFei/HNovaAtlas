import assert from "node:assert/strict";
import { performance } from "node:perf_hooks";
import { DatabaseSync } from "node:sqlite";

const db = new DatabaseSync(":memory:");
const rowCount = 48000;
const pageSize = 19;
const deepOffset = pageSize * 300;
const epochTicks = 639295200000000000n;
const filter = ["station-a", "NG"];

try {
  db.exec(`
    CREATE TABLE Inspections (
      Id TEXT PRIMARY KEY NOT NULL,
      Station TEXT NOT NULL,
      ImportedTicks INTEGER NOT NULL,
      Result TEXT NOT NULL
    );
    CREATE INDEX IX_Inspections_Station_Result_ImportedTicks_Id
      ON Inspections (Station, Result, ImportedTicks, Id);
  `);
  db.prepare(`
    WITH RECURSIVE seq(n) AS (SELECT 0 UNION ALL SELECT n + 1 FROM seq WHERE n + 1 < ?)
    INSERT INTO Inspections
    SELECT printf('cycle-%06d', n),
           CASE WHEN (n / 2) % 2 = 0 THEN 'station-a' ELSE 'station-b' END,
           ? + (n / 2) * 10000000,
           CASE WHEN (n / 2) % 3 = 0 THEN 'NG' ELSE 'OK' END
    FROM seq
  `).run(rowCount, epochTicks);
  db.exec("ANALYZE");

  const select = "SELECT Id, ImportedTicks FROM Inspections WHERE Station = ? AND Result = ?";
  const order = "ORDER BY ImportedTicks DESC, Id DESC";
  const offsetSql = `${select} ${order} LIMIT ? OFFSET ?`;
  const cursorSql = `${select} AND (ImportedTicks, Id) < (?, ?) ${order} LIMIT ?`;

  function prepareRows(sql) {
    const statement = db.prepare(sql);
    statement.setReadBigInts(true);
    return statement;
  }

  const offsetQuery = prepareRows(offsetSql);
  const cursorQuery = prepareRows(cursorSql);
  const first = offsetQuery.all(...filter, pageSize, 0);
  const boundary = first.at(-1);
  const second = offsetQuery.all(...filter, pageSize, pageSize);
  const cursorArgs = [...filter, boundary.ImportedTicks, boundary.Id, pageSize];
  assert.deepEqual(cursorQuery.all(...cursorArgs), second);
  assert.equal(second[0].ImportedTicks, boundary.ImportedTicks);
  const ticksOnly = prepareRows(`${select} AND ImportedTicks < ? ${order} LIMIT ?`);
  assert.ok(!ticksOnly.all(...filter, boundary.ImportedTicks, pageSize).some(row => row.Id === second[0].Id));

  // Find a starting boundary for this comparison only. A reader gets it from the previous page.
  const deepBoundary = offsetQuery.get(...filter, 1, deepOffset - 1);
  const deepOffsetArgs = [...filter, pageSize, deepOffset];
  const deepCursorArgs = [...filter, deepBoundary.ImportedTicks, deepBoundary.Id, pageSize];
  assert.equal(offsetQuery.all(...deepOffsetArgs).length, pageSize);
  assert.deepEqual(cursorQuery.all(...deepCursorArgs), offsetQuery.all(...deepOffsetArgs));

  function plan(sql, args) {
    return db.prepare("EXPLAIN QUERY PLAN " + sql).all(...args).map(step => step.detail);
  }

  const offsetTimes = [];
  const cursorTimes = [];
  function measure(query, args, times) {
    const start = performance.now();
    query.all(...args);
    times.push(performance.now() - start);
  }
  for (let i = 0; i < 51; i++) {
    measure(offsetQuery, deepOffsetArgs, offsetTimes);
    measure(cursorQuery, deepCursorArgs, cursorTimes);
  }
  const medianMs = times => Number(times.sort((a, b) => a - b)[Math.floor(times.length / 2)].toFixed(3));
  const deepPage = {
    offset: deepOffset,
    offsetPlan: plan(offsetSql, deepOffsetArgs),
    cursorPlan: plan(cursorSql, deepCursorArgs),
    offsetMedianMs: medianMs(offsetTimes),
    cursorMedianMs: medianMs(cursorTimes),
  };

  db.prepare("INSERT INTO Inspections (Id, Station, ImportedTicks, Result) VALUES (?, ?, ?, ?)")
    .run("cycle-new", filter[0], first[0].ImportedTicks + 1n, filter[1]);
  assert.equal(offsetQuery.get(...filter, 1, 0).Id, "cycle-new");
  const shifted = offsetQuery.all(...filter, pageSize, pageSize);
  assert.equal(shifted[0].Id, boundary.Id);
  assert.ok(!shifted.some(row => row.Id === second.at(-1).Id));
  assert.deepEqual(cursorQuery.all(...cursorArgs), second);

  console.log(JSON.stringify({
    sqliteVersion: db.prepare("SELECT sqlite_version() AS version").get().version,
    rowCount, pageSize, deepPage,
    sameTickSkippedByTicksOnly: second[0].Id,
    insertAtHead: {
      previousPageLastId: boundary.Id,
      offsetFirstId: shifted[0].Id,
      cursorFirstId: second[0].Id,
    },
  }, null, 2));
} finally {
  db.close();
}
