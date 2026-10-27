const STORAGE_KEY = "atlas-roadmap-progress";

let lastRaw: string | null = null;
let cachedDone: ReadonlySet<string> = new Set();
const listeners = new Set<() => void>();

function readStore(): ReadonlySet<string> {
  let raw: string | null = null;
  try {
    raw = window.localStorage.getItem(STORAGE_KEY);
  } catch {
    raw = null;
  }
  if (raw === lastRaw) return cachedDone;
  let ids: string[] = [];
  if (raw) {
    try {
      const parsed: unknown = JSON.parse(raw);
      if (Array.isArray(parsed)) ids = parsed.filter((id): id is string => typeof id === "string");
    } catch {
      ids = [];
    }
  }
  lastRaw = raw;
  cachedDone = new Set(ids);
  return cachedDone;
}

function notify() {
  for (const listener of listeners) listener();
}

export const roadmapProgress = {
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    window.addEventListener("storage", listener);
    return () => {
      listeners.delete(listener);
      window.removeEventListener("storage", listener);
    };
  },
  getDone(): ReadonlySet<string> {
    return readStore();
  },
  getEmpty(): ReadonlySet<string> {
    return cachedDone.size === 0 ? cachedDone : new Set();
  },
  toggle(id: string) {
    const next = new Set(readStore());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify([...next]));
    } catch {
      // 隐私模式等场景下 localStorage 不可写，进度仅保留在当前页面
    }
    lastRaw = null;
    notify();
  },
};
