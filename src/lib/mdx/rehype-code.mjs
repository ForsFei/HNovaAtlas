import rehypeShiki from "@shikijs/rehype";
import { createCssVariablesTheme } from "shiki/core";

export function parseCodeMeta(raw = "") {
  const filename = raw.match(/filename="([^"]+)"/)?.[1];
  const ranges = (raw.match(/\{([\d,\s-]+)\}/)?.[1] ?? "").split(",").map((part) => part.trim()).filter(Boolean).map((part) => {
    const [start, end = start] = part.split("-").map(Number);
    return [start, end];
  });
  return { filename, isHighlighted: (line) => ranges.some(([start, end]) => line >= start && line <= end) };
}

export const codeTransformer = {
  name: "atlas-code",
  pre(node) {
    const { filename } = parseCodeMeta(this.options.meta?.__raw);
    node.properties["data-code"] = this.source;
    node.properties["data-language"] = this.options.lang;
    if (filename) node.properties["data-filename"] = filename;
  },
  line(node, line) {
    node.properties["data-line-number"] = line;
    if (this.options.meta?.lineNumbers !== false) node.children.unshift({ type: "element", tagName: "span", properties: { className: ["line-number"], ariaHidden: "true" }, children: [{ type: "text", value: String(line) }] });
    if (parseCodeMeta(this.options.meta?.__raw).isHighlighted(line)) node.properties["data-highlighted"] = "true";
  },
};

export default function rehypeCode({ lineNumbers = true } = {}) {
  return rehypeShiki.call(this, {
    theme: createCssVariablesTheme({ variablePrefix: "--syntax-" }),
    defaultLanguage: "text", fallbackLanguage: "text", meta: { lineNumbers }, transformers: [codeTransformer],
  });
}
