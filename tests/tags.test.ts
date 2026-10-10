import { test } from "node:test";
import assert from "node:assert/strict";
import { buildTagIndex, tagHref, tagSlug } from "../src/lib/content/tags";
import type { Article } from "../src/types/content";

function article(id: string, tags: string[], status: Article["status"] = "published", updated = "2026-10-09"): Article {
  return { id, title: id, description: id, tags, status, updated, difficulty: "beginner", order: 1, related: [], prerequisite: [], slug: [id], href: `/knowledge/${id}/`, breadcrumbs: [], toc: [] };
}

test("tag slugs are stable and distinguish punctuation, unicode and reserved prefixes", () => {
  assert.equal(tagHref("EtherCAT"), "/tag/ethercat/");
  assert.equal(tagSlug("ethercat"), tagSlug("EtherCAT"));
  assert.notEqual(tagSlug("C#"), tagSlug("C++"));
  assert.notEqual(tagSlug(".NET"), tagSlug("NET"));
  assert.notEqual(tagSlug("过程数据"), tagSlug("过程映像"));
  assert.notEqual(tagSlug("u-1234"), tagSlug("\u001234"));
});

test("tag index excludes drafts, de-duplicates labels and orders recent articles", () => {
  const groups = buildTagIndex([
    article("old", ["EtherCAT", "PDO"], "published", "2025-01-01"),
    article("new", ["ethercat", "EtherCAT", "PDO"], "published", "2026-10-09"),
    article("draft", ["Secret", "PDO"], "draft", "2026-12-01"),
  ]);
  assert.deepEqual(groups.map((group) => [group.slug, group.articles.map((item) => item.id)]), [["ethercat", ["new", "old"]], ["pdo", ["new", "old"]]]);
  assert.equal(JSON.stringify(groups).includes("Secret"), false);
});
