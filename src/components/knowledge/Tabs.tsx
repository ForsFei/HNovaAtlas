import type { ReactNode } from "react";
import { collectTabs, type TabItem } from "@/lib/mdx/tabs";
import { TabsClient } from "./TabsClient";

export function Tab({ children }: TabItem) { return <>{children}</>; }

export function Tabs({ children, label = "内容切换", defaultValue }: { children: ReactNode; label?: string; defaultValue?: string }) {
  const items = collectTabs(children, Tab);
  const initial = defaultValue ?? items[0].value;
  if (!items.some((item) => item.value === initial)) throw new Error(`Unknown default Tab: ${initial}`);
  return <TabsClient items={items} label={label} defaultValue={initial} />;
}
