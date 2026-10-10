import type { ReactNode } from "react";
import { Info, Lightbulb, TriangleAlert, CircleAlert } from "lucide-react";

const kinds = {
  info: { label: "提示", Icon: Info },
  tip: { label: "建议", Icon: Lightbulb },
  warning: { label: "注意", Icon: TriangleAlert },
  danger: { label: "风险", Icon: CircleAlert },
};

export function Callout({ type = "info", title, children }: { type?: keyof typeof kinds; title?: string; children: ReactNode }) {
  if (!Object.hasOwn(kinds, type)) throw new Error(`Unknown Callout type: ${type}`);
  const { label, Icon } = kinds[type];
  return <aside className="atlas-callout" data-kind={type} aria-label={title || label}>
    <div className="atlas-callout-title"><Icon size={18} aria-hidden="true" /><strong>{title || label}</strong></div>
    <div className="atlas-component-body">{children}</div>
  </aside>;
}
