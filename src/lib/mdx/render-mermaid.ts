import mermaid from "mermaid";
import DOMPurify from "dompurify";
import { validateMermaidSource, MAX_DIAGRAM_LENGTH } from "./mermaid-policy";

// Mermaid configuration is global. Serialize initialize + render across diagrams/themes.
let queue: Promise<unknown> = Promise.resolve();
export function renderMermaid(id: string, chart: string): Promise<string> {
  const source = validateMermaidSource(chart);
  const job = queue.then(async () => {
    const tokens = getComputedStyle(document.documentElement);
    const color = (name: string) => tokens.getPropertyValue(name).trim();
    mermaid.initialize({
      startOnLoad: false, securityLevel: "strict", suppressErrorRendering: true,
      maxTextSize: MAX_DIAGRAM_LENGTH, maxEdges: 100, htmlLabels: false,
      theme: "base", layout: "dagre", fontFamily: '"Segoe UI", "Microsoft YaHei", sans-serif',
      themeVariables: {
        primaryColor: color("--accent-soft"), primaryTextColor: color("--text-primary"),
        primaryBorderColor: color("--accent"), lineColor: color("--text-secondary"),
        secondaryColor: color("--surface-hover"), tertiaryColor: color("--surface"),
        background: color("--surface"), mainBkg: color("--accent-soft"),
        textColor: color("--text-primary"), nodeBorder: color("--accent"),
        actorBkg: color("--accent-soft"), actorBorder: color("--accent"), actorTextColor: color("--text-primary"),
        signalColor: color("--text-secondary"), signalTextColor: color("--text-primary"),
        noteBkgColor: color("--surface-hover"), noteTextColor: color("--text-primary"),
      },
      flowchart: { useMaxWidth: true },
      secure: ["securityLevel", "startOnLoad", "maxTextSize", "maxEdges", "htmlLabels", "theme", "themeCSS", "themeVariables"],
    });
    const { svg } = await mermaid.render(id, source);
    // No bindFunctions: diagrams remain non-interactive, without external links/resources.
    return DOMPurify.sanitize(svg, {
      USE_PROFILES: { svg: true, svgFilters: true },
      ADD_TAGS: ["style"], FORBID_TAGS: ["foreignObject", "a", "image", "script"],
    });
  });
  queue = job.catch(() => undefined);
  return job;
}
