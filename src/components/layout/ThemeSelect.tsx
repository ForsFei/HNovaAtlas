"use client";

import { Monitor } from "lucide-react";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

const subscribe = () => () => {};

export function ThemeSelect() {
  const { theme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(subscribe, () => true, () => false);

  return (
    <label className="flex items-center gap-2 rounded-lg border border-border bg-surface px-3 py-2 text-xs text-secondary">
      <Monitor size={15} aria-hidden="true" />
      <span className="sr-only">颜色主题</span>
      <select
        aria-label="颜色主题"
        className="max-w-24 bg-transparent text-primary"
        value={mounted ? theme ?? "system" : "system"}
        disabled={!mounted}
        onChange={(event) => setTheme(event.target.value)}
      >
        <option value="system">跟随系统</option>
        <option value="dark">深色</option>
        <option value="light">浅色</option>
      </select>
    </label>
  );
}
