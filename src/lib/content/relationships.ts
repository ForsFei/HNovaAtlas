import type { Article, NavigationNode } from "../../types/content";

function flatten(node: NavigationNode): string[] {
  return node.kind === "article" ? (node.id ? [node.id] : []) : node.children.flatMap(flatten);
}

export function getReadingLinks(article: Article, articles: Article[], tree?: NavigationNode) {
  const byId = new Map(articles.map((item) => [item.id, item]));
  const ordered = tree ? flatten(tree).map((id) => byId.get(id)).filter((item): item is Article => !!item) : [];
  const position = ordered.findIndex((item) => item.id === article.id);
  const explicit = [...new Set(article.related)].map((id) => byId.get(id)).filter((item): item is Article => !!item && item.id !== article.id && item.status === "published");
  const score = (item: Article) => (item.slug.slice(0, 2).join("/") === article.slug.slice(0, 2).join("/") ? 2 : 0) + item.tags.filter((tag) => article.tags.includes(tag)).length;
  const inferred = articles.filter((item) => item.id !== article.id && item.status === "published" && !explicit.some((link) => link.id === item.id) && score(item) > 0)
    .sort((a, b) => score(b) - score(a) || a.order - b.order || a.href.localeCompare(b.href));
  return { previous: position > 0 ? ordered[position - 1] : undefined, next: position >= 0 ? ordered[position + 1] : undefined, related: [...explicit, ...inferred].slice(0, 4) };
}
