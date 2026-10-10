import { test } from "node:test";
import assert from "node:assert/strict";
import { lastHeadingAtOrBefore } from "../src/lib/content/toc-position";

test("TOC position finds first, middle, final and empty heading efficiently", () => {
  const tops = [-500, -100, 100, 160, 900];
  const find = (threshold: number) => lastHeadingAtOrBefore(tops.length, (index) => tops[index], threshold);
  assert.equal(find(-600), -1);
  assert.equal(find(-100), 1);
  assert.equal(find(140), 2);
  assert.equal(find(900), 4);
  assert.equal(lastHeadingAtOrBefore(0, () => { throw new Error("should not read"); }, 140), -1);
});

test("TOC heading work grows logarithmically for long articles", () => {
  const count = 10_000;
  let reads = 0;
  const found = lastHeadingAtOrBefore(count, (index) => { reads += 1; return index * 30; }, 140_000);
  assert.equal(found, Math.floor(140_000 / 30));
  assert(reads <= Math.ceil(Math.log2(count)) + 1);
});
