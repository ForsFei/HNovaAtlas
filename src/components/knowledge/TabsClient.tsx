"use client";

import { useId, useRef, useState, type KeyboardEvent } from "react";
import { nextTabIndex, type TabItem } from "@/lib/mdx/tabs";

export function TabsClient({ items, label, defaultValue }: { items: TabItem[]; label: string; defaultValue: string }) {
  const id = useId();
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const [active, setActive] = useState(defaultValue);
  function move(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if (event.nativeEvent.isComposing || event.altKey || event.ctrlKey || event.metaKey) return;
    const next = nextTabIndex(event.key, index, items.length);
    if (next === undefined) return;
    event.preventDefault();
    setActive(items[next].value);
    buttons.current[next]?.focus();
  }
  return <div className="atlas-tabs">
    <div role="tablist" aria-label={label} className="atlas-tablist" data-pagefind-ignore>
      {items.map((item, index) => <button key={item.value} ref={(button) => { buttons.current[index] = button; }} type="button" role="tab" id={`${id}-tab-${item.value}`} aria-controls={`${id}-panel-${item.value}`} aria-selected={active === item.value} tabIndex={active === item.value ? 0 : -1} onClick={() => setActive(item.value)} onKeyDown={(event) => move(event, index)}>{item.label}</button>)}
    </div>
    {items.map((item) => <div key={item.value} role="tabpanel" id={`${id}-panel-${item.value}`} aria-labelledby={`${id}-tab-${item.value}`} hidden={active !== item.value} tabIndex={0} className="atlas-tabpanel atlas-component-body">{item.children}</div>)}
  </div>;
}
