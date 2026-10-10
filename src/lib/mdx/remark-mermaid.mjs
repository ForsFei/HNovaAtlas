/** Convert only Mermaid code fences; ordinary code keeps the Shiki pipeline. */
export function transformMermaid(tree) {
  function visit(node) {
    if (!node.children) return;
    node.children = node.children.map((child) => {
      if (child.type === "code" && child.lang === "mermaid") {
        const caption = child.meta?.match(/caption="([^"]+)"/)?.[1] ?? "流程图";
        return { type: "mdxJsxFlowElement", name: "Mermaid", attributes: [
          { type: "mdxJsxAttribute", name: "chart", value: child.value },
          { type: "mdxJsxAttribute", name: "caption", value: caption },
        ], children: [], position: child.position };
      }
      visit(child);
      return child;
    });
  }
  visit(tree);
}
export default function remarkMermaid() { return transformMermaid; }
