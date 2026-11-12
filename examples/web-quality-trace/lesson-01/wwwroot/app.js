const form = document.querySelector("#lookup");
const cycle = document.querySelector("#cycle");
const status = document.querySelector("#status");
const record = document.querySelector("#record");
const button = form.querySelector("button");

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const id = cycle.value.trim();
  record.textContent = "";
  if (!id) {
    status.textContent = "请输入周期编号。";
    return;
  }

  button.disabled = true;
  status.textContent = "查询中…";
  try {
    const response = await fetch(`/api/inspections/${encodeURIComponent(id)}`);
    if (response.status === 404) {
      status.textContent = "没有找到该周期。";
      return;
    }
    if (!response.ok) {
      status.textContent = `查询失败（HTTP ${response.status}）。`;
      return;
    }

    const inspection = await response.json();
    record.textContent = JSON.stringify(inspection, null, 2);
    status.textContent = "查询成功。";
  } catch {
    status.textContent = "未能读取响应，请检查服务和 Network 面板。";
  } finally {
    button.disabled = false;
  }
});
