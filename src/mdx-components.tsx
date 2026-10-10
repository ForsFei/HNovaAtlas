import type { MDXComponents } from "mdx/types";
import { CodeBlock } from "@/components/knowledge/CodeBlock";
import { Callout } from "@/components/knowledge/Callout";
import { Accordion } from "@/components/knowledge/Accordion";
import { Tabs, Tab } from "@/components/knowledge/Tabs";
import { Mermaid } from "@/components/knowledge/Mermaid";
import { Quiz } from "@/components/interactive/Quiz";
import { FlashCard } from "@/components/interactive/FlashCard";
import { Stepper } from "@/components/interactive/Stepper";

const components: MDXComponents = {
  Callout, Accordion, Tabs, Tab, Mermaid, Quiz, FlashCard, Stepper,
  pre: CodeBlock,
  hr: () => <hr className="my-8 border-border" />,
};

export function useMDXComponents(): MDXComponents {
  return components;
}
