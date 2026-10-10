export function gradeAnswer(selected: number | null, answer: number): "idle" | "correct" | "incorrect" {
  return selected === null ? "idle" : selected === answer ? "correct" : "incorrect";
}

export function clampStep(index: number, count: number): number {
  return Math.max(0, Math.min(Math.trunc(index), Math.max(0, count - 1)));
}
