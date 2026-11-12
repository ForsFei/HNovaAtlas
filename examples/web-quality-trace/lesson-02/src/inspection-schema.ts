import { z } from "zod";
import type { components } from "./generated-query";

export const inspectionSchema = z.object({
  id: z.string().min(1),
  station: z.string().min(1),
  inspectedAt: z.iso.datetime({ offset: true }).optional(),
  importedAt: z.iso.datetime({ offset: true }).optional(),
  result: z.enum(["OK", "NG"]),
  roiMean: z.number().min(0).max(255),
}).refine(row => row.inspectedAt !== undefined || row.importedAt !== undefined,
  "An inspection needs its recorded timestamp.");

export const storageInspectionSchema = inspectionSchema.safeExtend({
  importedAt: z.iso.datetime({ offset: true }),
}) satisfies z.ZodType<components["schemas"]["InspectionSummary"]>;

export const inspectionArraySchema = z.array(inspectionSchema);
export const inspectionPageSchema = z.object({
  items: z.array(storageInspectionSchema),
  total: z.number().int().nonnegative(),
  page: z.number().int().positive(),
  pageSize: z.number().int().min(1).max(100),
}).refine(value => value.items.length <= value.pageSize && value.items.length <= value.total,
  "Page rows exceed the declared page size or total.") satisfies z.ZodType<components["schemas"]["InspectionPage"]>;

export const inspectionCursorPageSchema = z.object({
  items: z.array(storageInspectionSchema).max(100),
  nextCursor: z.string().min(1).nullable(),
}).refine(page => page.items.length > 0 || page.nextCursor === null,
  "An empty page cannot provide a next cursor.") satisfies z.ZodType<components["schemas"]["InspectionCursorPage"]>;

export type InspectionCursorPage = z.infer<typeof inspectionCursorPageSchema>;
export type Inspection = z.infer<typeof inspectionSchema>;
export type InspectionPage = z.infer<typeof inspectionPageSchema>;
