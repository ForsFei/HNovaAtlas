import { z } from "zod";

const text = z.string().trim().min(1).max(4000);
export const quizSchema = z.object({
  question: text,
  options: z.array(text).min(2).max(8),
  answer: z.number().int().nonnegative(),
  explanation: text.optional(),
}).strict().superRefine((value, ctx) => {
  if (value.answer >= value.options.length) ctx.addIssue({ code: "custom", path: ["answer"], message: "answer must refer to an option (zero-based)" });
  if (new Set(value.options).size !== value.options.length) ctx.addIssue({ code: "custom", path: ["options"], message: "options must be unique" });
});
export const flashCardSchema = z.object({ front: text, back: text }).strict();
const stateSchema = z.object({ label: text, value: text }).strict();
export const stepSchema = z.object({
  title: text,
  description: text,
  states: z.array(stateSchema).max(8).default([]),
}).strict().superRefine((value, ctx) => {
  if (new Set(value.states.map((state) => state.label)).size !== value.states.length) ctx.addIssue({ code: "custom", path: ["states"], message: "state labels must be unique" });
});
export const stepperSchema = z.object({ title: text, steps: z.array(stepSchema).min(1).max(20) }).strict();

export type QuizData = z.infer<typeof quizSchema>;
export type FlashCardData = z.infer<typeof flashCardSchema>;
export type StepData = z.infer<typeof stepSchema>;
export type StepperData = z.infer<typeof stepperSchema>;
