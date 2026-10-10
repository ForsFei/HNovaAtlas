"use client";

import { useEffect, useState } from "react";
import { searchKnowledge } from "@/lib/search/client";
import type { SearchResponse } from "@/lib/search/types";

type State = SearchResponse & { query: string; phase: "idle" | "loading" | "ready" | "error" };

export function useKnowledgeSearch(query: string, limit: number, retry: number) {
  const [state, setState] = useState<State>({ query: "", phase: "idle", results: [], total: 0 });
  useEffect(() => {
    let cancelled = false;
    const term = query.trim();
    if (!term) return;
    const timer = setTimeout(() => {
      setState((previous) => ({ query: term, phase: "loading", results: previous.query === term ? previous.results : [], total: 0 }));
      void searchKnowledge(term, limit).then((response) => {
        if (!cancelled) setState({ ...response, query: term, phase: "ready" });
      }).catch(() => { if (!cancelled) setState({ query: term, phase: "error", results: [], total: 0 }); });
    }, 180);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [query, limit, retry]);
  if (!query.trim()) return { query: "", phase: "idle" as const, results: [], total: 0 };
  return state.query === query.trim() ? state : { query: query.trim(), phase: "loading" as const, results: [], total: 0 };
}
