import type { PagefindAPI, SearchPreview } from "./types";
import { searchPagefind, searchPreview } from "./results";

let engine: Promise<PagefindAPI> | undefined;
let attempts = 0;
export async function loadSearch() {
  if (!engine) {
    // Browsers cache rejected module imports; retries need a fresh module URL.
    const attempt = attempts++;
    const url = `/pagefind/pagefind.js${attempt ? `?retry=${attempt}` : ""}`;
    engine = import(/* webpackIgnore: true */ url).then(async (api: PagefindAPI) => { await api.init(); return api; }).catch((error: unknown) => { engine = undefined; throw error; });
  }
  return engine;
}

export async function searchKnowledge(query: string, limit: number) {
  if (process.env.NODE_ENV === "development") {
    const { default: preview } = await import("@/generated/search-preview.json");
    return searchPreview(preview as SearchPreview[], query, limit);
  }
  const activeEngine = loadSearch();
  try {
    return await searchPagefind(await activeEngine, query, limit);
  } catch (error) {
    // Failed index/fragment fetches may also be cached by the engine instance.
    if (engine === activeEngine) engine = undefined;
    throw error;
  }
}
