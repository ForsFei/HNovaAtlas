export type MappingPreset = "basic" | "motion";
export type PdoField = { name: string; bits: 16 | 32 };
export type SlaveLayout = { number: number; outputOffset: number; inputOffset: number; outputBytes: number; inputBytes: number };

const presets: Record<MappingPreset, { output: PdoField[]; input: PdoField[] }> = {
  basic: { output: [{ name: "控制字", bits: 16 }], input: [{ name: "状态字", bits: 16 }] },
  motion: { output: [{ name: "控制字", bits: 16 }, { name: "目标位置", bits: 32 }], input: [{ name: "状态字", bits: 16 }, { name: "实际位置", bits: 32 }] },
};

export function getPdoMapping(preset: MappingPreset) { return presets[preset]; }

/** Independent output/input process-image offsets for an illustrative identical-slave layout. */
export function getPdoLayout(slaveCount: number, preset: MappingPreset): SlaveLayout[] {
  const count = Math.max(1, Math.min(5, Math.trunc(slaveCount)));
  const mapping = getPdoMapping(preset);
  const outputBytes = mapping.output.reduce((total, field) => total + field.bits / 8, 0);
  const inputBytes = mapping.input.reduce((total, field) => total + field.bits / 8, 0);
  return Array.from({ length: count }, (_, index) => ({
    number: index + 1, outputOffset: index * outputBytes, inputOffset: index * inputBytes, outputBytes, inputBytes,
  }));
}

/** Step 0 = master prepares, 1..N = each slave handles data, N+1 = master receives. */
export function getPdoStage(slaveCount: number, step: number) {
  const count = Math.max(1, Math.min(5, Math.trunc(slaveCount)));
  const current = Math.max(0, Math.min(count + 1, Math.trunc(step)));
  return { current, processedCount: Math.min(current, count), activeSlave: current > 0 && current <= count ? current : null, returned: current === count + 1 };
}

export function cyclesPerSecond(cycleMicroseconds: number) { return Math.round(1_000_000 / cycleMicroseconds); }
