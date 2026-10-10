import { Children, Fragment, isValidElement, type ReactNode } from "react";

export type TabItem = { value: string; label: string; children: ReactNode };

/** Inspect descriptors on the server; pass only data and rendered children to the client. */
export function collectTabs(children: ReactNode, marker: unknown): TabItem[] {
  const items: TabItem[] = [];
  function visit(nodes: ReactNode) {
    Children.forEach(nodes, (node) => {
      if (node == null || (typeof node === "string" && !node.trim())) return;
      if (!isValidElement<{ children?: ReactNode }>(node)) throw new Error("Tabs accepts only Tab children");
      if (node.type === Fragment) { visit(node.props.children); return; }
      if (node.type !== marker) throw new Error("Tabs accepts only Tab children");
      const props = node.props as TabItem;
      if (typeof props.value !== "string" || !/^[a-zA-Z0-9_-]+$/.test(props.value)) throw new Error("Tab value must be a non-empty identifier");
      if (typeof props.label !== "string" || !props.label.trim()) throw new Error("Tab requires a non-empty label");
      if (items.some((item) => item.value === props.value)) throw new Error(`Duplicate Tab value: ${props.value}`);
      items.push({ value: props.value, label: props.label, children: props.children });
    });
  }
  visit(children);
  if (!items.length) throw new Error("Tabs requires at least one Tab");
  return items;
}

export function nextTabIndex(key: string, index: number, count: number): number | undefined {
  if (!count) return undefined;
  if (key === "ArrowRight") return (index + 1) % count;
  if (key === "ArrowLeft") return (index - 1 + count) % count;
  if (key === "Home") return 0;
  if (key === "End") return count - 1;
}
