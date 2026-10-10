export function parseCodeMeta(raw?: string): { filename?: string; isHighlighted: (line: number) => boolean };
export const codeTransformer: import("shiki").ShikiTransformer;
