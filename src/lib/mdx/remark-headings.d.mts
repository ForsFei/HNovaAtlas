import type { Root } from "mdast";
import type { TocItem } from "../../types/content";
export function collectHeadings(tree: Root): TocItem[];
export default function remarkHeadings(): (tree: Root) => void;
