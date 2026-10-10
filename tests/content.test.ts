import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { stringify } from "yaml";
import { scanContent } from "../scripts/content-pipeline";
import { unified } from "unified";
import remarkParse from "remark-parse";
import remarkMdx from "remark-mdx";
import { collectHeadings } from "../src/lib/mdx/remark-headings.mjs";
import { parseCodeMeta } from "../src/lib/mdx/rehype-code.mjs";
import { codeToHast } from "shiki";
import { createCssVariablesTheme } from "shiki/core";
import { codeTransformer } from "../src/lib/mdx/rehype-code.mjs";
import { getReadingLinks } from "../src/lib/content/relationships";

async function fixture(run: (root: string) => Promise<void>) {
  const root = await mkdtemp(path.join(tmpdir(), "hnova-content-test-"));
  try { await run(root); } finally {
    assert.equal(path.dirname(root), path.resolve(tmpdir()));
    await rm(root, { recursive: true, force: true });
  }
}

test("TOC uses AST text and stable duplicate anchors, excluding code and nested JSX", () => {
  const tree = unified().use(remarkParse).use(remarkMdx).parse('## 标题 `code`\n\n### 标题 `code`\n\n```md\n## Not a heading\n```\n\n<div>\n\n## Hidden heading\n\n</div>');
  assert.deepEqual(collectHeadings(tree), [
    { id: "section-标题-code", title: "标题 code", depth: 2 },
    { id: "section-标题-code-1", title: "标题 code", depth: 3 },
  ]);
  assert.equal(tree.children[0].data?.hProperties?.id, "section-标题-code");
});

test("code metadata parses filenames and inclusive highlighted ranges", () => {
  const meta = parseCodeMeta('filename="Page Reader.cs" {1,3-5}');
  assert.equal(meta.filename, "Page Reader.cs");
  assert.deepEqual([1, 2, 3, 4, 5, 6].map(meta.isHighlighted), [true, false, true, true, true, false]);
  assert.equal(parseCodeMeta().isHighlighted(1), false);
});

test("highlighting keeps original copy source separate from accessible line numbers", async () => {
  const source = 'const value = "hello";\nconsole.log(value);';
  const tree = await codeToHast(source, { lang: "javascript", theme: createCssVariablesTheme(), transformers: [codeTransformer], meta: { __raw: 'filename="demo.js" {2}' } });
  const pre = tree.children[0];
  assert.equal(pre.type, "element");
  if (pre.type !== "element") return;
  assert.equal(pre.properties["data-code"], source);
  assert.equal(pre.properties["data-filename"], "demo.js");
  const html = JSON.stringify(pre);
  assert.ok(html.includes('"ariaHidden":"true"'));
  assert.ok(html.includes('"data-highlighted":"true"'));
});

test("reading links follow directory order, not dates; related links prioritize explicit IDs", async () => fixture(async (root) => {
  await article(root, "domain/tech/topic/z.mdx", { order: 10, updated: "2025-01-01", related: ["other"] });
  await article(root, "domain/tech/topic/a.mdx", { order: 20, updated: "2026-09-29" });
  await article(root, "domain/another/other.mdx");
  await article(root, "domain/tech/topic/draft.mdx", { status: "draft" });
  const index = await scanContent(root, false);
  const current = index.articles.find((item) => item.id === "z")!;
  const tree = index.navigation[0].children.find((item) => item.path === "domain/tech");
  const links = getReadingLinks(current, index.articles, tree);
  assert.equal(links.previous, undefined);
  assert.equal(links.next?.id, "a");
  assert.deepEqual(links.related.map((item) => item.id), ["other", "a"]);
}));

test("production skips even malformed draft MDX and extracts published headings", async () => fixture(async (root) => {
  await article(root, "domain/tech/live.mdx", {}, "## Live\n\n### Detail");
  await article(root, "domain/tech/draft.mdx", { status: "draft" }, "<Unclosed");
  assert.equal((await scanContent(root, true)).articles[0].toc.length, 2);
  await assert.rejects(scanContent(root, false), /draft.mdx/);
}));

async function article(root: string, file: string, overrides: Record<string, unknown> = {}, body = "## Content\nExample.") {
  const target = path.join(root, file);
  await mkdir(path.dirname(target), { recursive: true });
  const metadata = { id: path.basename(file).replace(/\.(md|mdx)$/, ""), title: "示例文章", description: "测试内容", status: "published", updated: "2026-09-29", ...overrides };
  await writeFile(target, `---\n${stringify(metadata)}---\n\n${body}`, "utf8");
}

test("production excludes drafts and their empty folders; scanner never executes MDX", async () => fixture(async (root) => {
  await article(root, "industrial/ethercat/basic/intro.mdx");
  await article(root, "industrial/ethercat/advanced/draft.mdx", { status: "draft" }, "{(() => { throw new Error('must not execute'); })()}");
  const production = await scanContent(root, true);
  assert.deepEqual(production.articles.map((item) => item.id), ["intro"]);
  assert.equal(JSON.stringify(production).includes("draft"), false);
  const development = await scanContent(root, false);
  assert.equal(development.articles.length, 2);
}));

test("directory names, numeric order, Markdown and new files are discovered automatically", async () => fixture(async (root) => {
  await article(root, "industrial/ethercat/communication/a.mdx", { order: 20 });
  await article(root, "industrial/ethercat/communication/z.md", { order: 10 });
  await writeFile(path.join(root, "industrial/_meta.yml"), "title: 工业自动化\norder: 10\n");
  await writeFile(path.join(root, "industrial/ethercat/_meta.yml"), "title: EtherCAT\n");
  let index = await scanContent(root, true);
  assert.deepEqual(index.navigation[0].children[0].children[0].children.map((item) => item.id), ["z", "a"]);
  assert.equal(index.articles[0].breadcrumbs[0].title, "工业自动化");
  await article(root, "industrial/ethercat/communication/new.mdx");
  index = await scanContent(root, true);
  assert.equal(index.articles.length, 3);
  assert.equal(index.articles.find((item) => item.id === "new")?.href, "/knowledge/industrial/ethercat/communication/new/");
}));

test("duplicate identifiers fail with a useful message", async () => fixture(async (root) => {
  await article(root, "domain/tech/a.mdx", { id: "same" });
  await article(root, "domain/tech/b.mdx", { id: "same" });
  await assert.rejects(scanContent(root, true), /重复文章 id: same/);
}));

test("md and mdx cannot produce the same URL", async () => fixture(async (root) => {
  await article(root, "domain/tech/a.mdx", { id: "first" });
  await article(root, "domain/tech/a.md", { id: "second" });
  await assert.rejects(scanContent(root, true), /重复文章路由/);
}));

test("invalid dates and unknown metadata fields fail", async () => fixture(async (root) => {
  await article(root, "domain/tech/a.mdx", { updated: "2026-02-30" });
  await assert.rejects(scanContent(root, true), /有效的 YYYY-MM-DD/);
  await article(root, "domain/tech/a.mdx", { typo: "unexpected" });
  await assert.rejects(scanContent(root, true), /typo/);
}));

test("broken and unpublished knowledge references fail", async () => fixture(async (root) => {
  await article(root, "domain/tech/a.mdx", { related: ["missing"] });
  await assert.rejects(scanContent(root, true), /知识引用不存在/);
  await article(root, "domain/tech/b.mdx", { status: "draft" });
  await article(root, "domain/tech/a.mdx", { prerequisite: ["b"] });
  await assert.rejects(scanContent(root, true), /引用了未发布草稿/);
  assert.equal((await scanContent(root, false)).articles.length, 2);
}));

test("missing status defaults to draft", async () => fixture(async (root) => {
  await article(root, "domain/tech/a.mdx", { status: undefined });
  assert.equal((await scanContent(root, true)).articles.length, 0);
  assert.equal((await scanContent(root, false)).articles[0].status, "draft");
}));

test("non-YAML frontmatter engines and malformed directory metadata are rejected", async () => fixture(async (root) => {
  await article(root, "domain/tech/a.mdx");
  const target = path.join(root, "domain/tech/a.mdx");
  await writeFile(target, "---js\nthrow new Error('never run');\n---\n");
  await assert.rejects(scanContent(root, true), /YAML Frontmatter/);
  await article(root, "domain/tech/a.mdx");
  await writeFile(path.join(root, "domain/tech/_meta.yml"), "title: Test\norder: wrong\n");
  await assert.rejects(scanContent(root, true), /_meta.yml/);
}));
