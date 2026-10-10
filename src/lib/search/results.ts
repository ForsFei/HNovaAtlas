import type { PagefindAPI, SearchPreview, SearchResponse } from "./types";

export function safeKnowledgeUrl(url: string): string | undefined {
  if (!url.startsWith("/knowledge/") || /[\\\s]/.test(url)) return undefined;
  const parsed = new URL(url, "https://atlas.invalid");
  return parsed.origin === "https://atlas.invalid" && parsed.pathname.startsWith("/knowledge/") ? `${parsed.pathname}${parsed.hash}` : undefined;
}

export async function searchPagefind(api: PagefindAPI, query: string, limit = 10): Promise<SearchResponse> {
  const response = await api.search(query.trim());
  const loaded = await Promise.all(response.results.slice(0, limit).map(async (result) => {
    const data = await result.data();
    const url = safeKnowledgeUrl(data.url);
    return url ? { id: result.id, url, title: data.meta.title || "知识文章", category: data.meta.category || "", excerpt: data.excerpt } : undefined;
  }));
  return { total: response.results.length, results: loaded.filter((result): result is NonNullable<typeof result> => !!result) };
}

export function searchPreview(articles: SearchPreview[], query: string, limit = 10): SearchResponse {
  const terms = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
  if (!terms.length) return { total: 0, results: [] };
  const matched = articles.filter((article) => {
    const text = [article.title, article.description, article.category, ...article.tags].join(" ").toLocaleLowerCase();
    return terms.every((term) => text.includes(term));
  }).sort((a, b) => Number(b.title.toLocaleLowerCase().includes(query.toLocaleLowerCase())) - Number(a.title.toLocaleLowerCase().includes(query.toLocaleLowerCase())) || a.url.localeCompare(b.url));
  return { total: matched.length, results: matched.slice(0, limit).map((article) => ({ ...article, excerpt: escapeText(article.description) })) };
}

function escapeText(text: string) { return text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;"); }

function decodeEntities(text: string) {
  return text.replace(/&(#x[\da-f]+|#\d+|amp|lt|gt|quot|apos|nbsp);/gi, (entity, key: string) => {
    const named: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: " " };
    if (!key.startsWith("#")) return named[key.toLowerCase()] ?? entity;
    const code = key.toLowerCase().startsWith("#x") ? parseInt(key.slice(2), 16) : parseInt(key.slice(1), 10);
    return code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : entity;
  });
}

/** Only text and explicit marks become React nodes. No HTML injection or DOM parsing. */
export function excerptParts(excerpt: string) {
  let marked = false;
  return excerpt.split(/(<\/?mark>)/g).flatMap((part) => {
    if (part === "<mark>") { marked = true; return []; }
    if (part === "</mark>") { marked = false; return []; }
    const text = decodeEntities(part.replace(/<[^>]*>/g, ""));
    return text ? [{ text, marked }] : [];
  });
}
