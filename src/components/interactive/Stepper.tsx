import { stepperSchema } from "@/lib/interactive/schema";
import { StepperClient } from "./StepperClient";
import type { z } from "zod";

export function Stepper(props: z.input<typeof stepperSchema>) {
  const data = stepperSchema.parse(props);
  return <StepperClient {...data} />;
}
