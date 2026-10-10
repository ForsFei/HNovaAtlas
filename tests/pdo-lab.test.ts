import { test } from "node:test";
import assert from "node:assert/strict";
import { cyclesPerSecond, getPdoLayout, getPdoMapping, getPdoStage } from "../src/lib/labs/pdo-model";

test("PDO presets keep output and input images distinct", () => {
  assert.deepEqual(getPdoMapping("basic").output.map((field) => field.bits), [16]);
  assert.deepEqual(getPdoMapping("motion").input.map((field) => field.bits), [16, 32]);
  assert.deepEqual(getPdoLayout(3, "basic").map((slave) => slave.outputOffset), [0, 2, 4]);
  assert.deepEqual(getPdoLayout(3, "motion").map((slave) => slave.inputOffset), [0, 6, 12]);
});

test("PDO layout caps the interactive slave count", () => {
  assert.equal(getPdoLayout(0, "basic").length, 1);
  assert.equal(getPdoLayout(100, "basic").length, 5);
});

test("frame journey has preparation, each slave, and returned states", () => {
  assert.deepEqual(getPdoStage(2, 0), { current: 0, processedCount: 0, activeSlave: null, returned: false });
  assert.deepEqual(getPdoStage(2, 1), { current: 1, processedCount: 1, activeSlave: 1, returned: false });
  assert.deepEqual(getPdoStage(2, 2), { current: 2, processedCount: 2, activeSlave: 2, returned: false });
  assert.deepEqual(getPdoStage(2, 3), { current: 3, processedCount: 2, activeSlave: null, returned: true });
  assert.equal(getPdoStage(2, 99).current, 3);
});

test("cycle frequency is only an arithmetic comparison, not a timing simulation", () => {
  assert.equal(cyclesPerSecond(500), 2000);
  assert.equal(cyclesPerSecond(2000), 500);
});
