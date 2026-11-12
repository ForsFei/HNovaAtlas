import type { ZodType } from "zod";
import { httpError } from "./http-error";

export async function readJson<T>(url: string, schema: ZodType<T>, signal: AbortSignal) {
  const deadline = new AbortController();
  const requestSignal = AbortSignal.any([signal, deadline.signal]);
  const timer = window.setTimeout(() => deadline.abort(), 15_000);
  let response: Response | undefined;
  try {
    response = await fetch(url, { signal: requestSignal });
    if (response.status === 429) {
      const retryAfter = response.headers.get("Retry-After") ?? "";
      const seconds = /^[0-9]{1,6}$/.test(retryAfter) ? Number(retryAfter) : 0;
      const message = seconds > 0 ? `查询过于频繁，请在 ${seconds} 秒后重试。` : "查询过于频繁，请稍后重试。";
      throw new Error(httpError(response, message));
    }
    if (!response.ok) {
      throw new Error(httpError(response, response.status === 404 ? "找不到该检测记录。" : `查询失败（HTTP ${response.status}）。`));
    }
    let payload: unknown;
    try { payload = await response.json(); }
    catch (error) {
      if (requestSignal.aborted) throw error;
      throw new Error(httpError(response, "响应不是有效的 JSON，请联系维护人员。"));
    }
    const parsed = schema.safeParse(payload);
    if (!parsed.success) throw new Error(httpError(response, "接口数据格式不符合约定，请联系维护人员。"));
    return parsed.data;
  } catch (error) {
    if (deadline.signal.aborted && !signal.aborted) {
      const message = "读取超过 15 秒，请重试。";
      throw new Error(response ? httpError(response, message) : message);
    }
    throw error;
  } finally {
    window.clearTimeout(timer);
  }
}
