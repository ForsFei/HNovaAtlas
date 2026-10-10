import { test } from "node:test";
import assert from "node:assert/strict";
import { renderToStaticMarkup } from "react-dom/server";
import { readFile, access } from "node:fs/promises";
import { unified } from "unified";
import remarkParse from "remark-parse";
import remarkMdx from "remark-mdx";
import { Quiz } from "../src/components/interactive/Quiz";
import { FlashCard } from "../src/components/interactive/FlashCard";
import { Stepper } from "../src/components/interactive/Stepper";
import { quizSchema, flashCardSchema, stepperSchema } from "../src/lib/interactive/schema";
import { gradeAnswer, clampStep } from "../src/lib/interactive/state";

test("Quiz validates zero-based answers, unique options and data-only props", () => {
  const quiz = { question: " Q ", options: [" A ", "B"], answer: 0 };
  assert.deepEqual(quizSchema.parse(quiz), { question: "Q", options: ["A", "B"], answer: 0 });
  for (const invalid of [{ ...quiz, answer: 2 }, { ...quiz, answer: -1 }, { ...quiz, answer: 0.5 }, { ...quiz, options: ["A"] }, { ...quiz, options: ["A", " A "] }, { ...quiz, question: " " }, { ...quiz, onAnswer: () => {} }, { ...quiz, explanation: " " }, { ...quiz, options: Array.from({ length: 9 }, (_, i) => `${i}`) }]) {
    assert.equal(quizSchema.safeParse(invalid).success, false);
  }
});

test("FlashCard and Stepper reject invalid text, shapes and duplicated state labels", () => {
  assert.equal(flashCardSchema.safeParse({ front: " ", back: "B" }).success, false);
  assert.equal(flashCardSchema.safeParse({ front: "A", back: "B", html: true }).success, false);
  const step = { title: "SYN", description: "Client sends SYN" };
  assert.deepEqual(stepperSchema.parse({ title: "TCP", steps: [step] }).steps[0].states, []);
  for (const invalid of [{ title: "TCP", steps: [] }, { title: "TCP", steps: Array(21).fill(step) }, { title: "TCP", steps: [{ ...step, description: " " }] }, { title: "TCP", steps: [{ ...step, states: [{ label: "Client", value: "A" }, { label: "Client", value: "B" }] }] }, { title: "TCP", steps: [step], onStep: () => {} }]) {
    assert.equal(stepperSchema.safeParse(invalid).success, false);
  }
  assert.equal(flashCardSchema.safeParse({ front: "a".repeat(4001), back: "B" }).success, false);
});

test("Answer grading and bounded step navigation cover idle, retry and endpoints", () => {
  assert.equal(gradeAnswer(null, 0), "idle");
  assert.equal(gradeAnswer(1, 0), "incorrect");
  assert.equal(gradeAnswer(0, 0), "correct");
  assert.equal(clampStep(-1, 3), 0);
  assert.equal(clampStep(1, 3), 1);
  assert.equal(clampStep(3, 3), 2);
  assert.equal(clampStep(1, 1), 0);
});

test("Quiz renders an unselected question with an indexable native answer fallback", () => {
  const html = renderToStaticMarkup(<Quiz question="Which?" options={["<script>", "B"]} answer={0} explanation="Why A" />);
  assert.equal((html.match(/aria-pressed="false"/g) ?? []).length, 2);
  assert(html.includes('role="status"') && html.includes('aria-live="polite"'));
  assert(html.includes("<details") && html.includes("正确答案：&lt;script&gt;"));
  assert(html.includes("Why A"));
  assert(!html.includes("<script>"));
  assert.throws(() => renderToStaticMarkup(<Quiz question="Q" options={["A", "B"]} answer={9} />));
});

test("FlashCard starts on the front and preserves both sides as native readable text", () => {
  const html = renderToStaticMarkup(<FlashCard front="Front" back="<img>Back" />);
  assert(html.includes('aria-expanded="false"'));
  assert(html.includes("hidden=\"\""));
  assert(html.includes("阅读文字版") && html.includes("&lt;img&gt;Back"));
  assert(!html.includes("<img>"));
});

test("Stepper renders the first state, bounded controls and all static steps", () => {
  const html = renderToStaticMarkup(<Stepper title="TCP" steps={[{ title: "SYN", description: "First", states: [{ label: "Client", value: "SYN-SENT" }] }, { title: "ACK", description: "Last" }]} />);
  assert.equal((html.match(/aria-current="step"/g) ?? []).length, 1);
  assert(html.includes('role="region"') && html.includes('tabindex="-1"'));
  assert(html.includes("阅读全部步骤") && html.includes("First") && html.includes("Last"));
  assert(html.includes("SYN-SENT") && html.includes("上一步") && html.includes("下一步"));
  assert.equal((html.match(/disabled=""/g) ?? []).length, 2);
});

test("Multiple learning components have independent accessible identifiers", () => {
  const html = renderToStaticMarkup(<><Quiz question="Q1" options={["A", "B"]} answer={0} /><Quiz question="Q2" options={["A", "B"]} answer={1} /><FlashCard front="F1" back="B1" /><FlashCard front="F2" back="B2" /><Stepper title="One" steps={[{ title: "Start", description: "Only" }]} /></>);
  const ids = [...html.matchAll(/ id="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(new Set(ids).size, ids.length);
  for (const match of html.matchAll(/aria-(?:labelledby|controls)="([^"]+)"/g)) assert(ids.includes(match[1]));
});

test("Interactive article examples use MDX extensions and real JSX expressions", async () => {
  const url = new URL("../content/computer-science/network/transport/tcp-handshake.mdx", import.meta.url);
  const source = await readFile(url, "utf8");
  await assert.rejects(access(new URL("../content/computer-science/network/transport/tcp-handshake.md", import.meta.url)));
  const tree = unified().use(remarkParse).use(remarkMdx).parse(source.replace(/^---[\s\S]*?\r?\n---\r?\n/, ""));
  const components = tree.children.filter((node) => node.type === "mdxJsxFlowElement");
  assert.deepEqual(components.map((node) => node.name), ["Stepper", "Quiz", "FlashCard"]);
  assert(components[0].attributes.some((attribute) => attribute.type === "mdxJsxAttribute" && attribute.name === "steps" && typeof attribute.value === "object"));
});
