import { flashCardSchema, type FlashCardData } from "@/lib/interactive/schema";
import { FlashCardClient } from "./FlashCardClient";

export function FlashCard(props: FlashCardData) {
  const data = flashCardSchema.parse(props);
  return <FlashCardClient {...data} />;
}
