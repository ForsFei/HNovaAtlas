import { useEffect, useId, useRef, useState, type FormEvent } from "react";

import { httpError } from "./http-error";
import { z } from "zod";

type Session = { user: string | null; canReview: boolean; csrf: string };
const reviewSchema = z.object({
  decision: z.enum(["confirmed-ng", "false-reject"]),
  note: z.string().min(1).max(500),
  reviewer: z.string().min(1),
  reviewedAt: z.iso.datetime({ offset: true }),
});
type Review = z.infer<typeof reviewSchema>;
type Draft = { decision: string; note: string };
type FieldErrors = Partial<Record<keyof Draft, string[]>>;
const validationSchema = z.object({
  errors: z.object({
    decision: z.array(z.string()).min(1).optional(),
    note: z.array(z.string()).min(1).optional(),
  }).refine(errors => errors.decision !== undefined || errors.note !== undefined),
});

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal });
  if (!response.ok) throw new Error(httpError(response, `读取失败（HTTP ${response.status}）。`));
  return response.json();
}

async function readSnapshot(url: string, signal?: AbortSignal) {
  const session = await getJson<Session>("/api/session", signal);
  const saved = session.user ? await getJson<unknown>(url, signal) : { review: null };
  const parsed = z.object({ review: reviewSchema.nullable() }).safeParse(saved);
  if (!parsed.success) throw new Error("复核数据格式不符合约定，请重新读取。");
  return { session, review: parsed.data.review };
}

function post(url: string, csrf: string, body?: object) {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

export function ReviewPanel({ id, onSaved }: { id: string; onSaved?: () => void }) {
  const url = `/api/inspections/${encodeURIComponent(id)}/review`;
  const [session, setSession] = useState<Session>();
  const [review, setReview] = useState<Review | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const form = useRef<HTMLFormElement>(null);
  const invalidField = useRef<keyof Draft | null>(null);
  const fieldId = useId();
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [draft, setDraft] = useState<Draft>({ decision: "confirmed-ng", note: "" });
  const [recovery, setRecovery] = useState<(Draft & {
    message: string; uncertain?: boolean; retryAllowed?: boolean;
  }) | null>(null);

  function applySnapshot(snapshot: Awaited<ReturnType<typeof readSnapshot>>) {
    setSession(snapshot.session);
    setReview(snapshot.review);
  }

  async function refresh() {
    applySnapshot(await readSnapshot(url));
  }

  useEffect(() => {
    const controller = new AbortController();
    void readSnapshot(url, controller.signal).then(snapshot => {
      if (!controller.signal.aborted) applySnapshot(snapshot);
    }).catch(() => {
      if (!controller.signal.aborted) setMessage("无法读取登录或复核状态，请刷新重试。");
    });
    return () => controller.abort();
  }, [url]);

  async function run(action: () => Promise<void>) {
    setMessage("");
    setBusy(true);
    try {
      await action();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "请求失败，请刷新后重试。");
    } finally {
      setBusy(false);
    }
  }

  async function reject(response: Response, reason: string): Promise<never> {
    const failure = httpError(response, reason);
    try {
      await refresh();
    } catch {
      throw new Error(failure + " 无法更新登录状态，请点击「刷新登录状态」。");
    }
    throw new Error(failure);
  }

  function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!session) return;
    const data = new FormData(event.currentTarget);
    void run(async () => {
      const response = await post("/api/session/login", session.csrf, {
        user: data.get("user"), password: data.get("password"),
      });
      if (!response.ok) {
        await reject(response, response.status === 401 ? "用户名或密码不正确。" : `登录失败（HTTP ${response.status}）。`);
      }
      await refresh();
    });
  }

  useEffect(() => {
    if (!busy && invalidField.current) {
      form.current?.querySelector<HTMLElement>(`[name="${invalidField.current}"]`)?.focus();
      invalidField.current = null;
    }
  }, [busy, fieldErrors]);

  function showFieldErrors(errors: FieldErrors) {
    invalidField.current = errors.decision ? "decision" : "note";
    setFieldErrors(errors);
  }

  function updateDraft(field: keyof Draft, value: string) {
    setDraft(previous => ({ ...previous, [field]: value }));
    setFieldErrors(previous => ({ ...previous, [field]: undefined }));
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!session) return;
    const submitted = { ...draft };
    const errors: FieldErrors = {};
    const noteLength = submitted.note.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, "").length;
    if (!["confirmed-ng", "false-reject"].includes(submitted.decision))
      errors.decision = ["请选择确认 NG 或误判。"];
    if (noteLength < 1 || noteLength > 500)
      errors.note = ["说明去除首尾空白后须为 1～500 个字符。"];
    if (Object.keys(errors).length) {
      setMessage("");
      showFieldErrors(errors);
      return;
    }
    setFieldErrors({});
    void run(async () => {
      setRecovery(null);
      let response: Response;
      try {
        response = await post(url, session.csrf, submitted);
      } catch {
        setRecovery({ ...submitted, uncertain: true, message: "未收到提交结果，请核对已保存结果。" });
        return;
      }
      if (response.status >= 500) {
        setRecovery({ ...submitted, uncertain: true, message: httpError(response, "提交结果待确认，请核对已保存结果。") });
        return;
      }
      if (response.status === 409) {
        setRecovery({ ...submitted, message: httpError(response, "提交冲突，请核对已保存的复核结果。") });
        await refresh();
        onSaved?.();
        return;
      }
      if (response.status === 400) {
        const problem = validationSchema.safeParse(await response.json().catch(() => null));
        if (problem.success) {
          showFieldErrors(problem.data.errors);
          setMessage(httpError(response, "请修改标出的字段后重试。"));
          return;
        }
      }
      if (!response.ok) {
        const reason = response.status === 401 ? "登录已失效，请重新登录后继续复核。"
          : response.status === 403 ? "当前账号没有复核权限，请切换到复核账号。"
          : response.status === 400 ? "请求校验失败，请检查内容后重试。"
          : `提交失败（HTTP ${response.status}）。`;
        await reject(response, reason);
      }
      let saved: Review;
      try {
        saved = reviewSchema.parse(await response.json());
      } catch {
        setRecovery({ ...submitted, uncertain: true, message: httpError(response, "提交响应不完整，请核对已保存结果。") });
        return;
      }
      setReview(saved);
      setDraft({ decision: "confirmed-ng", note: "" });
      onSaved?.();
    });
  }

  async function reconcile() {
    const snapshot = await readSnapshot(url);
    applySnapshot(snapshot);
    if (!recovery) return;
    if (snapshot.review) onSaved?.();
    setRecovery({
      ...recovery,
      retryAllowed: snapshot.session.canReview && !snapshot.review,
      message: !snapshot.session.user ? "请登录后核对提交结果。"
        : snapshot.review ? "已读取保存的复核结果，请核对草稿。"
        : "暂未查到已保存结果，可以稍后再核对或重新提交。",
    });
  }

  return <div className="review">
    <h3>NG 复核</h3>
    <button disabled={busy} onClick={() => void run(refresh)}>刷新登录状态</button>
    {message && <p role="alert">{message}</p>}
    {recovery && <aside aria-label={recovery.uncertain ? "待核对的复核草稿" : "未保存的复核草稿"}>
      <p role="alert">{recovery.message}</p>
      <dl>
        <dt>{recovery.uncertain ? "本次提交的结论" : "未保存的结论"}</dt><dd>{recovery.decision === "confirmed-ng" ? "确认 NG" : "误判"}</dd>
        <dt>{recovery.uncertain ? "本次提交的说明" : "未保存的说明"}</dt><dd>{recovery.note}</dd>
      </dl>
      <button disabled={busy} onClick={() => void run(recovery.uncertain ? reconcile : refresh)}>
        {recovery.uncertain ? "核对提交结果" : "重新读取已保存结果"}
      </button>
      {review && <button disabled={busy} onClick={() => {
        setRecovery(null);
        setDraft({ decision: "confirmed-ng", note: "" });
        setMessage("");
      }}>{recovery.uncertain ? "清除提交草稿" : "清除未保存草稿"}</button>}
    </aside>}
    {!session && !message && <p role="status">正在读取登录状态…</p>}
    {session && !session.user && <form onSubmit={login}>
      <label>用户名 <input name="user" autoComplete="username" required /></label>
      <label>密码 <input name="password" type="password" autoComplete="current-password" required /></label>
      <button disabled={busy}>登录</button>
    </form>}
    {session?.user && <>
      <p>当前用户 {session.user} <button disabled={busy} onClick={() => void run(async () => {
        const response = await post("/api/session/logout", session.csrf);
        await refresh();
        if (!response.ok) throw new Error(`退出失败（HTTP ${response.status}）。`);
      })}>退出登录</button></p>
      {review ? <dl>
        <dt>复核结论</dt><dd>{review.decision === "confirmed-ng" ? "确认 NG" : "误判"}</dd>
        <dt>说明</dt><dd>{review.note}</dd>
        <dt>复核人</dt><dd>{review.reviewer}</dd>
        <dt>复核时间</dt><dd><time dateTime={review.reviewedAt}>{review.reviewedAt}</time></dd>
      </dl> : recovery && !recovery.retryAllowed ? <p>请重新读取已保存结果，或登录后查看。</p> : session.canReview ? <form ref={form} noValidate onSubmit={submit}>
        <label>结论 <select name="decision" disabled={busy} value={draft.decision}
          aria-invalid={!!fieldErrors.decision} aria-describedby={fieldErrors.decision ? `${fieldId}-decision` : undefined}
          onChange={event => updateDraft("decision", event.target.value)}>
          <option value="confirmed-ng">确认 NG</option>
          <option value="false-reject">误判</option>
        </select></label>
        {fieldErrors.decision && <p id={`${fieldId}-decision`} role="alert">{fieldErrors.decision.join(" ")}</p>}
        <label>说明 <textarea name="note" maxLength={500} required disabled={busy} value={draft.note}
          aria-invalid={!!fieldErrors.note} aria-describedby={fieldErrors.note ? `${fieldId}-note` : undefined}
          onChange={event => updateDraft("note", event.target.value)} /></label>
        {fieldErrors.note && <p id={`${fieldId}-note`} role="alert">{fieldErrors.note.join(" ")}</p>}
        <button disabled={busy}>提交复核</button>
      </form> : <p>尚无复核记录。当前账号可以查看复核记录。</p>}
    </>}
  </div>;
}
