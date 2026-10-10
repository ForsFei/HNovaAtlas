import { test } from "node:test";
import assert from "node:assert/strict";
import { buildHomeOverview, getHomeLabs } from "../src/lib/content/home";
import type { Article, NavigationNode } from "../src/types/content";

function article(id: string, domain: string, technology: string, overrides: Partial<Article> = {}): Article {
  return { id, title: id, description: "示例", tags: [], difficulty: "beginner", status: "published", updated: "2026-09-29", order: 100, related: [], prerequisite: [], toc: [], slug: [domain, technology, id], href: `/knowledge/${domain}/${technology}/${id}/`, breadcrumbs: [{ title: domain, path: domain }, { title: technology, path: `${domain}/${technology}` }], ...overrides };
}

function directory(path: string, children: NavigationNode[] = [], title = path): NavigationNode {
  return { kind: "directory", path, title, order: 100, children };
}

test("homepage counts published articles and hides draft-only domains and technologies", () => {
  const articles = [article("a", "domain", "tech"), article("b", "domain", "tech"), article("c", "domain", "another"), article("draft", "domain", "hidden", { status: "draft" }), article("other-draft", "unpublished", "tech", { status: "draft" })];
  const navigation = [directory("domain", [directory("domain/another", [], "另一个技术"), directory("domain/tech", [], "技术"), directory("domain/hidden")], "示例领域"), directory("unpublished", [directory("unpublished/tech")])];
  const overview = buildHomeOverview(articles, navigation);
  assert.equal(overview.articleCount, 3);
  assert.equal(overview.technologyCount, 2);
  assert.deepEqual(overview.categories, [{ path: "domain", title: "示例领域", count: 3, technologies: [{ path: "domain/another", title: "另一个技术", count: 1 }, { path: "domain/tech", title: "技术", count: 2 }] }]);
  assert.equal(JSON.stringify(overview).includes("draft"), false);
});

test("recent updates sort by date then URL, respect the limit and do not mutate input", () => {
  const articles = [article("z", "domain", "tech"), article("older", "domain", "tech", { updated: "2025-01-01" }), article("a", "domain", "tech"), article("draft", "domain", "tech", { status: "draft", updated: "2026-12-31" })];
  const original = articles.map((item) => item.id);
  assert.deepEqual(buildHomeOverview(articles, [], 2).recent.map((item) => item.id), ["a", "z"]);
  assert.deepEqual(buildHomeOverview([...articles].reverse(), [], 3).recent.map((item) => item.id), ["a", "z", "older"]);
  assert.deepEqual(articles.map((item) => item.id), original);
  assert.equal(buildHomeOverview(articles, [], 0).recent.length, 0);
});

test("new category paths and renamed directory titles are reflected without hardcoded domains", () => {
  const result = buildHomeOverview([article("intro", "new-domain", "new-tech")], [directory("new-domain", [directory("new-domain/new-tech", [], "新技术")], "新知识领域")]);
  assert.equal(result.categories[0].title, "新知识领域");
  assert.equal(result.categories[0].technologies[0].title, "新技术");
});

test("empty homepage has zero counts and no dead experiment reading links", () => {
  assert.deepEqual(buildHomeOverview([], []), { categories: [], recent: [], articleCount: 0, technologyCount: 0 });
  assert.equal(getHomeLabs([]).length, 3);
  assert.equal(getHomeLabs([]).filter((lab) => lab.status === "available").length, 1);
  assert.ok(getHomeLabs([]).every((lab) => !lab.reading));
});

test("lab reading links resolve stable IDs after moving paths and never expose drafts", () => {
  const published = article("csharp-async-await", "programming", "csharp", { href: "/knowledge/programming/csharp/new-topic/async-await/" });
  const draft = article("ethercat-pdo", "industrial", "ethercat", { status: "draft" });
  const labs = getHomeLabs([published, draft]);
  assert.equal(labs.find((lab) => lab.id === "async-task")?.reading?.href, published.href);
  assert.equal(labs.find((lab) => lab.id === "ethercat-frame")?.reading, undefined);
  assert.equal(labs.find((lab) => lab.id === "ethercat-frame")?.status, "available");
});
