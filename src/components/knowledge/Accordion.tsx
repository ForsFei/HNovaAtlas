import type { ReactNode } from "react";
import { ChevronDown } from "lucide-react";

export function Accordion({ title, defaultOpen = false, children }: { title: string; defaultOpen?: boolean; children: ReactNode }) {
  if (!title?.trim()) throw new Error("Accordion requires a non-empty title");
  return <details className="atlas-accordion" open={defaultOpen || undefined}>
    <summary><span>{title}</span><ChevronDown size={18} aria-hidden="true" /></summary>
    <div className="atlas-component-body atlas-accordion-content">{children}</div>
  </details>;
}
