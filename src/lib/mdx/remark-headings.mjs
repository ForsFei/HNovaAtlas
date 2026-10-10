import GithubSlugger from "github-slugger";
import { toString } from "mdast-util-to-string";

// Only document-level headings: hidden interactive panels must not enter the TOC.
export function collectHeadings(tree) {
  const slugger = new GithubSlugger();
  return tree.children.flatMap((node) => {
    if (node.type !== "heading" || ![2, 3].includes(node.depth)) return [];
    const title = toString(node);
    const id = `section-${slugger.slug(title)}`;
    node.data = { ...node.data, hProperties: { ...node.data?.hProperties, id } };
    return [{ id, title, depth: node.depth }];
  });
}

export default function remarkHeadings() { return (tree) => { collectHeadings(tree); }; }
