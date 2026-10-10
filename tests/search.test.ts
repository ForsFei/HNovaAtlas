import { test } from "node:test";
import assert from "node:assert/strict";
import { excerptParts, safeKnowledgeUrl, searchPagefind, searchPreview } from "../src/lib/search/results";
import type { PagefindAPI, SearchPreview } from "../src/lib/search/types";

test("result URLs are restricted to knowledge routes, retaining safe heading anchors", () => {
  assert.equal(safeKnowledgeUrl("/knowledge/industrial/ethercat/pdo/#section-mapping"), "/knowledge/industrial/ethercat/pdo/#section-mapping");
  for (const url of ["https://evil.invalid/knowledge/", "//evil.invalid/knowledge/", "javascript:alert(1)", "/about/", "/knowledge/../about/", "/knowledge/\\evil", "/knowledge/ space/"]) assert.equal(safeKnowledgeUrl(url), undefined);
});

test("search result details are loaded only for the requested batch", async () => {
  let loaded = 0;
  let received = "";
  const api: PagefindAPI = { init: async () => {}, search: async (query) => {
    received = query;
    return { results: Array.from({ length: 25 }, (_, id) => ({ id: String(id), data: async () => { loaded++; return { url: `/knowledge/domain/tech/${id}/`, excerpt: "<mark>PDO</mark>", meta: { title: `Title ${id}`, category: "Industrial" } }; } })) };
  } };
  const response = await searchPagefind(api, " PDO ", 10);
  assert.equal(received, "PDO");
  assert.equal(response.total, 25);
  assert.equal(loaded, 10);
  assert.equal(response.results.length, 10);
  assert.equal(response.results[0].title, "Title 0");
});

test("unsafe search URLs are not rendered and engine errors propagate for retry UI", async () => {
  const api: PagefindAPI = { init: async () => {}, search: async () => ({ results: [{ id: "unsafe", data: async () => ({ url: "https://evil.invalid", excerpt: "test", meta: {} }) }] }) };
  assert.deepEqual((await searchPagefind(api, "test")).results, []);
  await assert.rejects(searchPagefind({ ...api, search: async () => { throw new Error("offline"); } }, "test"), /offline/);
});

test("excerpt parsing preserves only text and marks, including encoded symbols", () => {
  assert.deepEqual(excerptParts('等待 <mark>Task&lt;string&gt;</mark> &amp; &#x4e2d;&#25991;'), [
    { text: "等待 ", marked: false }, { text: "Task<string>", marked: true }, { text: " & 中文", marked: false },
  ]);
  assert.deepEqual(excerptParts('<img src=x onerror=alert(1)><mark>&lt;script&gt;</mark>'), [{ text: "<script>", marked: true }]);
  assert.equal(excerptParts("&#999999999;")[0].text, "&#999999999;");
});

test("development metadata preview searches title, summary, tags and category without mutation", () => {
  const items: SearchPreview[] = [
    { id: "pdo", url: "/knowledge/industrial/ethercat/pdo/", title: "PDO", description: "过程数据 <对象>", category: "工业 / EtherCAT", tags: ["CoE"] },
    { id: "sdo", url: "/knowledge/industrial/ethercat/sdo/", title: "SDO", description: "参数访问", category: "工业 / EtherCAT", tags: [] },
  ];
  assert.equal(searchPreview(items, "CoE").results[0].id, "pdo");
  assert.equal(searchPreview(items, "过程数据").results[0].excerpt, "过程数据 &lt;对象&gt;");
  assert.equal(searchPreview(items, "工业 EtherCAT").total, 2);
  assert.equal(searchPreview(items, "   ").total, 0);
  assert.equal(searchPreview(items, "never").total, 0);
  assert.deepEqual(items.map((item) => item.id), ["pdo", "sdo"]);
});
