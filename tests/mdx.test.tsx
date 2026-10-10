import { test } from "node:test";
import assert from "node:assert/strict";
import { Fragment } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { unified } from "unified";
import remarkParse from "remark-parse";
import remarkMdx from "remark-mdx";
import { Callout } from "../src/components/knowledge/Callout";
import { Accordion } from "../src/components/knowledge/Accordion";
import { Tabs, Tab } from "../src/components/knowledge/Tabs";
import { collectTabs, nextTabIndex } from "../src/lib/mdx/tabs";
import { validateMermaidSource, MAX_DIAGRAM_LENGTH } from "../src/lib/mdx/mermaid-policy";
import { transformMermaid } from "../src/lib/mdx/remark-mermaid.mjs";
import { collectHeadings } from "../src/lib/mdx/remark-headings.mjs";

test("Callout supports semantic variants and escapes author text", () => {
  for (const type of ["info", "tip", "warning", "danger"] as const) {
    const html = renderToStaticMarkup(<Callout type={type} title="注意"><p>{"<script>"}</p></Callout>);
    assert(html.includes(`data-kind="${type}"`));
    assert(html.includes('aria-label="注意"'));
    assert(html.includes("&lt;script&gt;"));
  }
  assert.throws(() => renderToStaticMarkup(<Callout type={"bad" as "info"}>Invalid</Callout>), /Unknown/);
});

test("Accordion remains native HTML with default-open semantics", () => {
  const closed = renderToStaticMarkup(<Accordion title="问题">答案</Accordion>);
  assert(closed.includes("<details") && closed.includes("<summary>"));
  assert(!closed.includes('open=""'));
  assert(renderToStaticMarkup(<Accordion title="问题" defaultOpen>答案</Accordion>).includes('open=""'));
  assert.throws(() => renderToStaticMarkup(<Accordion title=" ">答案</Accordion>), /non-empty/);
});

test("Tabs preserves server-rendered children and handles fragments", () => {
  const children = <Fragment><Tab value="source" label="源"><p>源正文</p></Tab><Tab value="target" label="目标"><p>目标正文</p></Tab></Fragment>;
  assert.deepEqual(collectTabs(children, Tab).map(({ value }) => value), ["source", "target"]);
  const html = renderToStaticMarkup(<Tabs label="绑定两端" defaultValue="target">{children}</Tabs>);
  assert(html.includes('role="tablist"') && html.includes('aria-label="绑定两端"'));
  assert.equal((html.match(/role="tab"/g) ?? []).length, 2);
  assert.equal((html.match(/role="tabpanel"/g) ?? []).length, 2);
  assert.equal((html.match(/aria-selected="true"/g) ?? []).length, 1);
  assert.equal((html.match(/hidden=""/g) ?? []).length, 1);
  assert(html.includes("源正文") && html.includes("目标正文"));
});

test("Tabs rejects duplicate identifiers, missing labels, invalid structure and defaults", () => {
  assert.throws(() => collectTabs(null, Tab), /at least one/);
  assert.throws(() => collectTabs(<div>not a tab</div>, Tab), /only Tab/);
  assert.throws(() => collectTabs(<Tab value="bad id" label="标题">test</Tab>, Tab), /identifier/);
  assert.throws(() => collectTabs(<Tab value="a" label=" ">test</Tab>, Tab), /label/);
  assert.throws(() => collectTabs([<Tab key="1" value="a" label="一">{null}</Tab>, <Tab key="2" value="a" label="二">{null}</Tab>], Tab), /Duplicate/);
  assert.throws(() => renderToStaticMarkup(<Tabs defaultValue="missing"><Tab value="a" label="一">{null}</Tab></Tabs>), /Unknown default/);
});

test("Tabs keyboard navigation wraps and supports Home/End without capturing unrelated keys", () => {
  assert.equal(nextTabIndex("ArrowLeft", 0, 3), 2);
  assert.equal(nextTabIndex("ArrowRight", 2, 3), 0);
  assert.equal(nextTabIndex("Home", 2, 3), 0);
  assert.equal(nextTabIndex("End", 0, 3), 2);
  assert.equal(nextTabIndex("Tab", 0, 3), undefined);
  assert.equal(nextTabIndex("ArrowRight", 0, 0), undefined);
});

test("Mermaid policy accepts supported diagrams and blocks directives, HTML and resources", () => {
  assert.equal(validateMermaidSource(" flowchart LR\n A --> B "), "flowchart LR\n A --> B");
  assert(validateMermaidSource("sequenceDiagram\n A->>B: hello"));
  for (const chart of ["", "pie\n title test", "flowchart LR\n%%{init: {securityLevel: 'loose'}}%%", "---\nconfig: {}\n---\nflowchart LR", 'flowchart LR\n click A "javascript:alert(1)"', 'flowchart LR\n A[<script>]', 'flowchart LR\n A[https://evil.invalid]', "flowchart LR\n classDef bad fill:url(x)", "flowchart LR\n" + "a".repeat(MAX_DIAGRAM_LENGTH)]) {
    assert.throws(() => validateMermaidSource(chart));
  }
});

test("Mermaid fences become data-only components while code and document TOC remain intact", () => {
  const tree = unified().use(remarkParse).use(remarkMdx).parse('## 原理\n\n```mermaid caption="绑定流程"\nflowchart LR\n A --> B\n```\n\n```xml\n<TextBlock />\n```');
  transformMermaid(tree);
  const diagram = tree.children[1] as unknown as { type: string; name: string; attributes: { name: string; value: string }[] };
  assert.equal(diagram.type, "mdxJsxFlowElement");
  assert.equal(diagram.name, "Mermaid");
  assert.deepEqual(diagram.attributes.map(({ name, value }) => [name, value]), [["chart", "flowchart LR\n A --> B"], ["caption", "绑定流程"]]);
  assert.equal(tree.children[2].type, "code");
  assert.equal(collectHeadings(tree).length, 1);
});
