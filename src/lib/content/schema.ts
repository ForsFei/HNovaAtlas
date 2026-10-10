import { z } from "zod";

export const identifier = z.string().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/, "必须使用小写英文、数字和连字符");
const date = z.string().regex(/^\d{4}-\d{2}-\d{2}$/).refine((value) => {
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}, "日期必须是有效的 YYYY-MM-DD");

export const frontmatterSchema = z.object({
  id: identifier,
  title: z.string().trim().min(1),
  description: z.string().trim().min(1),
  tags: z.array(z.string().trim().min(1)).default([]),
  difficulty: z.enum(["beginner", "intermediate", "advanced"]).default("beginner"),
  status: z.enum(["draft", "published"]).default("draft"),
  updated: date,
  order: z.number().int().nonnegative().default(100),
  related: z.array(identifier).default([]),
  prerequisite: z.array(identifier).default([]),
}).strict();

export const directorySchema = z.object({
  title: z.string().trim().min(1),
  order: z.number().int().nonnegative().default(100),
}).strict();
