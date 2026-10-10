import "server-only";
import rawIndex from "@/generated/content-index.json";
import { articleLoaders } from "@/generated/article-loaders";
import type { Article, ContentIndex, NavigationNode } from "@/types/content";

// Generated JSON is validated by the content preparation step.
const index: ContentIndex = rawIndex as ContentIndex;
const bySlug = new Map(index.articles.map((article) => [article.slug.join("/"), article]));

export function getArticles(): Article[] { return index.articles; }
export function getNavigation(): NavigationNode[] { return index.navigation; }
export function getArticle(slug: string[]): Article | undefined { return bySlug.get(slug.join("/")); }
export function getArticleLoader(slug: string[]) { return articleLoaders[slug.join("/")]; }

export function getTechnologyTree(article: Article) {
  const domain = index.navigation.find((node) => node.path === article.slug[0]);
  return domain?.children.find((node) => node.path === article.slug.slice(0, 2).join("/"));
}

export function getDomainArticles(domain: string) { return index.articles.filter((article) => article.slug[0] === domain); }
export function getFirstArticle(node: NavigationNode): NavigationNode | undefined {
  if (node.kind === "article") return node;
  for (const child of node.children) {
    const article = getFirstArticle(child);
    if (article) return article;
  }
}
