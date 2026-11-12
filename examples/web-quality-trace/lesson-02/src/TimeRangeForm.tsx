import { useState, type FormEvent } from "react";

function localValue(utc: string) {
  const date = new Date(utc);
  if (!Number.isFinite(date.getTime())) return "";
  const pad = (value: number, digits = 2) => String(value).padStart(digits, "0");
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
}

function utcValue(value: string) {
  if (!value) return "";
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) {
    throw new Error("日期或时间无效。");
  }
  // Detect local wall-clock times normalized through a daylight-saving gap.
  const normalizedInput = value.length === 16 ? value + ":00.000"
    : value.length === 19 ? value + ".000" : value.padEnd(23, "0");
  if (localValue(date.toISOString()) !== normalizedInput) {
    throw new Error("这个本地时间不存在，请重新选择。");
  }
  return date.toISOString();
}

export function TimeRangeForm({ since, until, onApply }: {
  since: string;
  until: string;
  onApply: (changes: Record<string, string>) => void;
}) {
  const [error, setError] = useState("");
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    const data = new FormData(event.currentTarget);
    try {
      const start = utcValue(String(data.get("since") ?? ""));
      const end = utcValue(String(data.get("until") ?? ""));
      if (start && end && start >= end) throw new Error("结束时间必须晚于开始时间。");
      onApply({ since: start, until: end, page: "", id: "" });
    } catch (error) {
      setError(error instanceof Error ? error.message : "时间范围无效。");
    }
  }

  return <form className="time-range" onSubmit={submit}>
    <fieldset>
      <legend>导入时间范围（{zone}，结束时间不包含）</legend>
      <label>开始时间 <input name="since" type="datetime-local" step="0.001"
        min="0001-01-01T00:00" max="9999-12-31T23:59:59.999" defaultValue={localValue(since)} /></label>
      <label>结束时间 <input name="until" type="datetime-local" step="0.001"
        min="0001-01-01T00:00" max="9999-12-31T23:59:59.999" defaultValue={localValue(until)} /></label>
      <button>应用时间范围</button>
      <button type="button" onClick={event => {
        event.currentTarget.form?.reset();
        setError("");
        onApply({ since: "", until: "", page: "", id: "" });
      }}>清除时间范围</button>
    </fieldset>
    {error && <p role="alert">{error}</p>}
  </form>;
}
