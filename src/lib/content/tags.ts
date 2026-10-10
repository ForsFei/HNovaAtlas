import type { Article } from "@/types/content";

export type TagGroup = { slug: string; name: string; articles: Article[] };

/** Readable simple ASCII tags; everything else uses an injective UTF-8 hex slug. */
export function tagSlug(name: string): string {
  const normalized = name.normalize("NFKC").trim().toLowerCase();
  if (/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(normalized) && !normalized.startsWith("u-")) return normalized;
  const hex = Array.from(new TextEncoder().encode(normalized), (byte) => byte.toString(16).padStart(2, "0")).join("");
  return `u-${hex}`;
}

export function tagHref(name: string): string { return `/tag/${tagSlug(name)}/`; }

/** Published metadata only: never expose draft-only tags or draft articles. */
export function buildTagIndex(articles: Article[]): TagGroup[] {
  const groups = new Map<string, TagGroup>();
  for (const article of articles) {
    if (article.status !== "published") continue;
    const seen = new Set<string>();
    for (const name of article.tags) {
      const slug = tagSlug(name);
      if (seen.has(slug)) continue;
      seen.add(slug);
      const group = groups.get(slug);
      if (group) group.articles.push(article);
      else groups.set(slug, { slug, name, articles: [article] });
    }
  }
  return [...groups.values()].map((group) => ({ ...group, articles: [...group.articles].sort((a, b) => b.updated.localeCompare(a.updated) || a.href.localeCompare(b.href)) }))
    .sort((a, b) => b.articles.length - a.articles.length || a.name.localeCompare(b.name, "zh-CN"));
}
