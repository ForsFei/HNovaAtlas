import { useEffect, useState } from "react";
import type { ZodType } from "zod";
import { readJson } from "./read-json";

type Load<T> =
  | { state: "loading" }
  | { state: "ready"; data: T }
  | { state: "error"; message: string };

export function useJson<T>(url: string, schema: ZodType<T>, revision = 0) {
  const [attempt, setAttempt] = useState(0);
  const key = JSON.stringify([url, attempt]);
  const [result, setResult] = useState<{ key: string; schema: ZodType<T>; load: Load<T> }>();

  useEffect(() => {
    const controller = new AbortController();

    async function request() {
      try {
        const data = await readJson(url, schema, controller.signal);
        if (!controller.signal.aborted) setResult({ key, schema, load: { state: "ready", data } });
      } catch (error) {
        if (!controller.signal.aborted) {
          setResult({ key, schema, load: { state: "error", message: error instanceof Error ? error.message : "查询失败。" } });
        }
      }
    }

    void request();
    return () => controller.abort();
  }, [url, schema, key, revision]);

  const load: Load<T> = result?.key === key && result.schema === schema ? result.load : { state: "loading" };
  return { load, retry: () => setAttempt(value => value + 1) };
}

