export type SearchResult = { id: string; url: string; title: string; category: string; excerpt: string };
export type SearchResponse = { results: SearchResult[]; total: number };
export type SearchPreview = { id: string; url: string; title: string; description: string; category: string; tags: string[] };
export type PagefindAPI = {
  init: () => Promise<void>;
  search: (query: string) => Promise<{ results: { id: string; data: () => Promise<{ url: string; excerpt: string; meta: Record<string, string> }> }[] }>;
};
