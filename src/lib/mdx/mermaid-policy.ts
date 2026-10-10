export const MAX_DIAGRAM_LENGTH = 10_000;

/** Conservative authoring subset, not a sandbox for arbitrary MDX. */
export function validateMermaidSource(chart: string): string {
  const source = chart.trim();
  if (!source || source.length > MAX_DIAGRAM_LENGTH) throw new Error("图表为空或超过 10,000 字符限制。");
  if (!/^(?:flowchart|graph|sequenceDiagram|stateDiagram-v2|classDiagram|erDiagram)\b/.test(source)) throw new Error("当前仅支持流程、时序、状态、类和实体关系图。");
  if (/%%\s*\{|^\s*---\s*$/m.test(source)) throw new Error("图表不能覆盖站点配置。");
  if (/^\s*click\b|<[a-z!/]|(?:https?:|javascript:|data:|url\s*\(|@import)/im.test(source)) throw new Error("图表不允许 HTML、点击指令或外部资源。");
  return source;
}
