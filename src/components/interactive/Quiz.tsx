import { quizSchema, type QuizData } from "@/lib/interactive/schema";
import { QuizClient } from "./QuizClient";

export function Quiz(props: QuizData) {
  const data = quizSchema.parse(props);
  return <QuizClient {...data} />;
}
