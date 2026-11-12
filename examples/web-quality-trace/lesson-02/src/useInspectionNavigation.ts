import { useEffect, useState } from "react";

export function useInspectionNavigation() {
  const [search, setSearch] = useState(window.location.search);
  useEffect(() => {
    const sync = () => setSearch(window.location.search);
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);

  function navigate(changes: Record<string, string>) {
    const next = new URL(window.location.href);
    for (const [name, value] of Object.entries(changes)) {
      if (value) next.searchParams.set(name, value);
      else next.searchParams.delete(name);
    }
    if (next.search === window.location.search) return;
    window.history.pushState(null, "", next);
    setSearch(next.search);
  }

  return { search, navigate };
}
