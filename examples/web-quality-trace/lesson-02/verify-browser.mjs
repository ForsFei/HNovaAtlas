import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { once } from "node:events";
import { mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, expect } from "@playwright/test";
import { DatabaseSync } from "node:sqlite";

const root = fileURLToPath(new URL("../../../", import.meta.url));
const work = mkdtempSync(join(tmpdir(), "atlas-web-acceptance-"));
const database = join(work, "trace.db");
const app = join(root, "examples/web-quality-trace/lesson-03/bin/Debug/net10.0/QualityTrace.Storage.dll");
const generator = join(root, "examples/industrial-station-evidence/bin/Debug/net10.0/IndustrialStation.Evidence.dll");
const assets = fileURLToPath(new URL("./.local/dist", import.meta.url));
const secrets = { alice: randomUUID(), bob: randomUUID(), viewer: randomUUID() };
const environment = { ...process.env, Review__Enabled: "true",
  Review__Alice: secrets.alice, Review__Bob: secrets.bob, Review__Viewer: secrets.viewer };

execFileSync("dotnet", ["build", "examples/web-quality-trace/lesson-03", "--nologo"], { cwd: root, stdio: "pipe" });
const demo = execFileSync("dotnet", [generator, "--demo"], { cwd: work, encoding: "utf8" });
const evidence = join(demo.match(/retained: (.+); closed=True/i)?.[1] ?? "", "evidence");
const bundles = readdirSync(evidence, { withFileTypes: true }).filter(entry => entry.isDirectory());
assert.equal(bundles.length, 2, demo);
for (const bundle of bundles) {
  execFileSync("dotnet", [app, "--TraceDb", database, "--Import", join(evidence, bundle.name)],
    { cwd: work, stdio: "pipe" });
}

let server;
let serverExit;
let origin;
let log = "";
let browser;
async function startApi(port = 0) {
  server = spawn("dotnet", [app, "--TraceDb", database, "--Live:Enabled", "true",
    "--Live:Assets", assets, "--urls", "http://127.0.0.1:" + port],
  { cwd: work, env: environment, windowsHide: true });
  serverExit = once(server, "exit");
  let current = "";
  server.stdout.setEncoding("utf8").on("data", chunk => { log += chunk; current += chunk; });
  server.stderr.setEncoding("utf8").on("data", chunk => { log += chunk; current += chunk; });
  for (let attempt = 0; attempt < 150; attempt++) {
    const found = current.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
    if (found) { origin = found[1]; return; }
    if (server.exitCode !== null) throw new Error("API exited during startup.");
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error("API startup timed out.");
}
async function stopApi() {
  if (!server) return;
  if (server.exitCode === null && server.signalCode === null) server.kill();
  await serverExit;
  server = undefined;
}

function copyNg(id, station, tickOffset = 10n) {
  const fixture = new DatabaseSync(database);
  try {
    const insert = fixture.prepare(`
      INSERT INTO Inspections (Id, Station, ImportedTicks, Result, RoiMean, Width, Height, ImageSha256, Pgm)
      SELECT ?, ?, ImportedTicks + ?, Result, RoiMean, Width, Height, ImageSha256, Pgm
      FROM Inspections WHERE Station = 'station-a' AND Result = 'NG'
      ORDER BY ImportedTicks, Id LIMIT 1
    `);
    assert.equal(insert.run(id, station, tickOffset).changes, 1);
  } finally {
    fixture.close();
  }
}

async function verifyReview(page) {
  await page.goto(origin);
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await page.getByRole("button", { name: "下一页", exact: true }).click();
  await expect(page).toHaveURL(/page=2/);

  await page.getByRole("combobox", { name: "结果", exact: true }).selectOption("NG");
  await expect(page.locator("tbody td").last()).toHaveText("NG");
  assert.equal(new URL(page.url()).searchParams.has("page"), false);

  const downloadEvent = page.waitForEvent("download");
  const csvRequest = page.waitForRequest(request => new URL(request.url()).searchParams.get("format") === "csv");
  await page.getByRole("button", { name: "导出当前筛选结果 CSV", exact: true }).click();
  const download = await downloadEvent;
  const csvQuery = new URL((await csvRequest).url()).searchParams;
  assert.deepEqual(Object.fromEntries(csvQuery), { result: "NG", format: "csv" });
  assert.equal(download.suggestedFilename(), "inspections.csv");
  const csvPath = join(work, "inspections.csv");
  await download.saveAs(csvPath);
  const csv = readFileSync(csvPath, "utf8");
  assert.match(csv, /"NG"/);
  assert.doesNotMatch(csv, /"OK"/);

  await page.locator("tbody button").click();
  await expect(page.locator("img.evidence")).toBeVisible();
  await expect.poll(() => page.locator("img.evidence").evaluate(image => image.naturalWidth)).toBeGreaterThan(0);
  const detailUrl = page.url();
  const detailId = new URL(detailUrl).searchParams.get("id");
  const originalDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "下载原始 PGM", exact: true }).click();
  const originalFile = await originalDownload;
  assert.equal(originalFile.suggestedFilename(), `${detailId}.pgm`);
  const pgmPath = join(work, originalFile.suggestedFilename());
  await originalFile.saveAs(pgmPath);
  assert.deepEqual(readFileSync(pgmPath), readFileSync(join(evidence, detailId, "image.pgm")));
  assert.equal(page.url(), detailUrl);
  await expect(page.getByText("SHA-256 校验通过，已发起文件保存。", { exact: true })).toBeVisible();
  let unexpectedDownloads = 0;
  const countDownload = () => unexpectedDownloads++;
  page.on("download", countDownload);
  const evidencePattern = "**/api/inspections/*/evidence";
  const downloadButton = page.getByRole("button", { name: "下载原始 PGM", exact: true });
  for (const defect of ["not-found", "server-error", "missing-digest", "changed-body"]) {
    await page.route(evidencePattern, async route => {
      const actual = await route.fetch();
      if (defect === "not-found" || defect === "server-error") {
        await route.fulfill({ status: defect === "not-found" ? 404 : 500,
          headers: { "X-Request-Id": "2".repeat(32), "Content-Type": "application/problem+json" },
          json: { title: "Download verification failure" } });
      } else if (defect === "missing-digest") {
        const headers = actual.headers();
        delete headers["content-digest"];
        await route.fulfill({ response: actual, headers });
      } else {
        const bytes = await actual.body();
        bytes[bytes.length - 1] ^= 1;
        await route.fulfill({ response: actual, body: bytes });
      }
    }, { times: 1 });
    await downloadButton.click();
    await expect(page.getByRole("alert")).toContainText(
      defect === "changed-body" ? "文件摘要不匹配"
        : defect === "missing-digest" ? "下载响应格式不符合约定" : "2".repeat(32));
    await expect(downloadButton).toBeEnabled();
    assert.equal(page.url(), detailUrl);
    assert.equal(unexpectedDownloads, 0);
  }
  page.off("download", countDownload);
  const retryDownload = page.waitForEvent("download");
  await downloadButton.click();
  await retryDownload;
  await expect(page.getByRole("alert")).toHaveCount(0);

  const heldDownload = Promise.withResolvers();
  const releaseDownload = Promise.withResolvers();
  page.on("download", countDownload);
  await page.route(evidencePattern, async route => {
    const actual = await route.fetch();
    heldDownload.resolve();
    await releaseDownload.promise;
    await route.fulfill({ response: actual }).catch(() => {});
  }, { times: 1 });
  try {
    await downloadButton.click();
    await heldDownload.promise;
    await expect(page.getByRole("button", { name: "正在下载并校验…" })).toBeDisabled();
    const cancelled = page.waitForEvent("requestfailed", {
      predicate: request => new URL(request.url()).pathname.endsWith("/evidence"),
    });
    await page.getByRole("button", { name: "关闭详情" }).click();
    assert.match((await cancelled).failure()?.errorText ?? "", /abort/i);
  } finally {
    releaseDownload.resolve();
    await page.unrouteAll({ behavior: "wait" });
    page.off("download", countDownload);
  }
  assert.equal(unexpectedDownloads, 0);
  await page.locator("tbody button").click();
  await expect(page.locator("img.evidence")).toBeVisible();
  console.log("PASS evidence download: client digest, errors without navigation, retry and cancellation.");
  const login = async (target, user) => {
    await target.getByLabel("用户名", { exact: true }).fill(user);
    await target.getByLabel("密码", { exact: true }).fill(secrets[user]);
    await target.getByRole("button", { name: "登录", exact: true }).click();
    await expect(target.locator(".review")).toContainText("当前用户 " + user);
  };

  const viewerContext = await browser.newContext();
  const viewer = await viewerContext.newPage();
  await viewer.goto(detailUrl);
  await login(viewer, "viewer");
  await expect(viewer.getByRole("button", { name: "提交复核", exact: true })).toHaveCount(0);
  const bobContext = await browser.newContext();
  const bob = await bobContext.newPage();
  await bob.goto(detailUrl);
  await login(bob, "bob");
  await bob.getByRole("combobox", { name: /^结论/ }).selectOption("false-reject");
  await bob.getByLabel("说明", { exact: true }).fill("Bob unsaved draft");
  await login(page, "alice");
  await page.getByLabel("说明", { exact: true }).fill("Browser acceptance review");
  const reviewPath = "/api/inspections/" + detailId + "/review";
  let reviewPosts = 0;
  const countReviewPosts = request => {
    if (new URL(request.url()).pathname === reviewPath && request.method() === "POST") reviewPosts++;
  };
  page.on("request", countReviewPosts);
  await page.route("**" + reviewPath, async route => {
    assert.equal(route.request().method(), "POST");
    const actual = await route.fetch();
    assert.equal(actual.status(), 201);
    await route.abort("failed");
  }, { times: 1 });
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  const receiptDraft = page.getByRole("complementary", { name: "待核对的复核草稿" });
  await expect(receiptDraft).toContainText("未收到提交结果");
  await expect(receiptDraft).toContainText("Browser acceptance review");
  await expect(page.getByRole("button", { name: "提交复核", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "核对提交结果", exact: true }).click();
  await expect(page.locator(".review > dl")).toContainText("Browser acceptance review");
  await expect(page.locator(".review > dl")).toContainText("alice");
  assert.equal(reviewPosts, 1);
  page.off("request", countReviewPosts);
  await page.getByRole("button", { name: "清除提交草稿", exact: true }).click();
  await expect(receiptDraft).toHaveCount(0);
  console.log("PASS lost review receipt: server committed, browser lost response, GET reconciled without another POST.");
  await bob.route("**" + reviewPath, async route => {
    if (route.request().method() === "GET") {
      await route.fulfill({ status: 503, headers: { "X-Request-Id": "3".repeat(32) } });
    } else {
      await route.continue();
    }
  });
  const conflictResponse = bob.waitForResponse(response =>
    new URL(response.url()).pathname === reviewPath && response.request().method() === "POST");
  await bob.getByRole("button", { name: "提交复核", exact: true }).click();
  const conflict = await conflictResponse;
  assert.equal(conflict.status(), 409);
  const bobDraft = bob.getByRole("complementary", { name: "未保存的复核草稿" });
  await expect(bobDraft).toContainText("Bob unsaved draft");
  await expect(bobDraft).toContainText("误判");
  await expect(bobDraft).toContainText(conflict.headers()["x-request-id"]);
  await expect(bob.getByRole("alert").filter({ hasText: "读取失败" })).toContainText("3".repeat(32));
  await expect(bob.getByRole("button", { name: "提交复核", exact: true })).toHaveCount(0);
  await bob.unroute("**" + reviewPath);
  await bob.getByRole("button", { name: "重新读取已保存结果", exact: true }).click();
  await expect(bob.locator(".review > dl")).toContainText("Browser acceptance review");
  await expect(bob.locator(".review > dl")).toContainText("alice");
  await expect(bobDraft).toContainText("Bob unsaved draft");
  const storedReview = await (await bobContext.request.get(origin + reviewPath)).json();
  assert.equal(storedReview.review.reviewer, "alice");
  assert.equal(storedReview.review.note, "Browser acceptance review");
  await bob.getByRole("button", { name: "清除未保存草稿", exact: true }).click();
  await expect(bobDraft).toHaveCount(0);
  await expect(bob.locator(".review > dl")).toContainText("Browser acceptance review");
  await bobContext.close();
  console.log("PASS two-user review conflict: saved winner, retained draft, failed refresh, retry and discard.");

  copyNg("review-not-sent", "review-fixture", 40n);
  await page.goto(origin + "/?id=review-not-sent");
  await page.getByLabel("说明", { exact: true }).fill("Retry after reconciliation");
  const retryReviewPath = "/api/inspections/review-not-sent/review";
  for (const fault of ["before-send", "server-error"]) {
    await page.route("**" + retryReviewPath, async route => {
      assert.equal(route.request().method(), "POST");
      if (fault === "before-send") await route.abort("failed");
      else await route.fulfill({ status: 503, headers: { "X-Request-Id": "4".repeat(32) } });
    }, { times: 1 });
    await page.getByRole("button", { name: "提交复核", exact: true }).click();
    const uncertain = page.getByRole("complementary", { name: "待核对的复核草稿" });
    await expect(uncertain).toContainText(fault === "before-send" ? "未收到提交结果" : "4".repeat(32));
    await page.getByRole("button", { name: "核对提交结果", exact: true }).click();
    await expect(uncertain).toContainText("暂未查到已保存结果");
    await expect(page.getByRole("textbox", { name: "说明", exact: true })).toHaveValue("Retry after reconciliation");
    const snapshot = await (await page.context().request.get(origin + retryReviewPath)).json();
    assert.equal(snapshot.review, null);
  }
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(page.locator(".review > dl")).toContainText("Retry after reconciliation");
  await page.goto(detailUrl);
  await expect(page.locator(".review > dl")).toContainText("Browser acceptance review");
  console.log("PASS unsent review and 503: preserved draft, queried empty result and explicit retry.");

  copyNg("review-auth-recovery", "review-fixture", 50n);
  const authDetail = origin + "/?id=review-auth-recovery";
  const authPath = "/api/inspections/review-auth-recovery/review";
  await page.goto(authDetail);
  const note = "Draft preserved across authentication recovery";
  const noteInput = page.getByRole("textbox", { name: "说明", exact: true });
  let fieldValidationPosts = 0;
  const countFieldPosts = request => {
    if (new URL(request.url()).pathname === authPath && request.method() === "POST") fieldValidationPosts++;
  };
  page.on("request", countFieldPosts);
  await noteInput.fill(" \n\t\u0085\u2000 ");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(noteInput).toHaveAttribute("aria-invalid", "true");
  await expect(noteInput).toBeFocused();
  const noteErrorId = await noteInput.getAttribute("aria-describedby");
  assert.ok(noteErrorId);
  await expect(page.locator(`[id="${noteErrorId}"]`)).toContainText("说明去除首尾空白");
  assert.equal(fieldValidationPosts, 0);
  await noteInput.fill(note);
  await expect(noteInput).toHaveAttribute("aria-invalid", "false");
  await page.route("**" + authPath, async route => {
    const actual = await route.fetch({ postData: JSON.stringify({ decision: "invalid", note: "x".repeat(501) }) });
    assert.equal(actual.status(), 400);
    await route.fulfill({ response: actual });
  }, { times: 1 });
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  const decisionInput = page.getByRole("combobox", { name: "结论", exact: true });
  await expect(decisionInput).toHaveAttribute("aria-invalid", "true");
  await expect(decisionInput).toBeFocused();
  await expect(noteInput).toHaveAttribute("aria-invalid", "true");
  await expect(noteInput).toHaveValue(note);
  await expect(page.getByRole("alert").filter({ hasText: "请修改标出的字段" })).toContainText("请求编号");
  assert.equal(fieldValidationPosts, 1);
  page.off("request", countFieldPosts);
  await decisionInput.selectOption("false-reject");
  await decisionInput.selectOption("confirmed-ng");
  await noteInput.fill(note + " corrected");
  await noteInput.fill(note);
  await expect(noteInput).toHaveAttribute("aria-invalid", "false");
  console.log("PASS field validation: whitespace blocked locally, real server errors, accessible focus and preserved draft.");
  await page.context().clearCookies({ name: "QualityTrace.Session" });
  const expiredResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === authPath && response.request().method() === "POST");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  const expired = await expiredResponse;
  assert.equal(expired.status(), 401);
  await expect(page.getByRole("alert")).toContainText("登录已失效");
  await expect(page.getByRole("alert")).toContainText(expired.headers()["x-request-id"]);
  await page.getByLabel("用户名", { exact: true }).fill("alice");
  await page.getByLabel("密码", { exact: true }).fill("incorrect-password-for-test");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("用户名或密码不正确");
  await login(page, "alice");
  await expect(page.getByRole("textbox", { name: "说明", exact: true })).toHaveValue(note);

  const sibling = await page.context().newPage();
  await sibling.goto(authDetail);
  await sibling.getByRole("button", { name: "退出登录", exact: true }).click();
  await login(sibling, "viewer");
  const forbiddenResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === authPath && response.request().method() === "POST");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  const forbidden = await forbiddenResponse;
  assert.equal(forbidden.status(), 403);
  await expect(page.getByRole("alert")).toContainText("当前账号没有复核权限");
  await expect(page.locator(".review")).toContainText("当前用户 viewer");
  await expect(page.getByRole("button", { name: "提交复核", exact: true })).toHaveCount(0);
  await sibling.close();
  await page.getByRole("button", { name: "退出登录", exact: true }).click();
  await login(page, "alice");
  await expect(page.getByRole("textbox", { name: "说明", exact: true })).toHaveValue(note);

  await page.route("**" + authPath, async route => {
    assert.equal(route.request().method(), "POST");
    const headers = await route.request().allHeaders();
    const actual = await route.fetch({ headers: { ...headers, "x-csrf-token": "invalid-test-token" } });
    assert.equal(actual.status(), 400);
    await route.fulfill({ response: actual });
  }, { times: 1 });
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("请求校验失败");
  await expect(page.getByRole("textbox", { name: "说明", exact: true })).toHaveValue(note);
  const empty = await (await page.context().request.get(origin + authPath)).json();
  assert.equal(empty.review, null);
  const resubmitted = page.waitForRequest(request =>
    new URL(request.url()).pathname === authPath && request.method() === "POST");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  assert.ok((await (await resubmitted).allHeaders())["x-csrf-token"]);
  await expect(page.locator(".review > dl")).toContainText(note);
  await expect(page.locator(".review > dl")).toContainText("alice");
  await page.goto(detailUrl);
  await expect(page.locator(".review > dl")).toContainText("Browser acceptance review");
  console.log("PASS auth recovery: missing session, wrong password, shared-cookie role switch, CSRF rejection and draft retry.");

  await stopApi();
  await startApi(Number(new URL(origin).port));
  await page.reload();
  await expect(page.locator(".review dl")).toContainText("Browser acceptance review");

  await page.getByLabel("开始时间", { exact: true }).fill("2000-01-01T00:00");
  await page.getByLabel("结束时间", { exact: true }).fill("2001-01-01T00:00");
  await page.getByRole("button", { name: "应用时间范围", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("没有符合条件的记录");
  assert.equal(new URL(page.url()).searchParams.get("since"), "1999-12-31T16:00:00.000Z");
  await page.reload();
  await expect(page.getByLabel("开始时间", { exact: true })).toHaveValue("2000-01-01T00:00");
  await page.getByRole("button", { name: "清除时间范围", exact: true }).click();
  await expect(page.locator("tbody td").last()).toHaveText("NG");

  const failed = page.waitForResponse(response => response.url().endsWith("/api/inspections/missing"));
  await page.goto(origin + "/?id=missing");
  const response = await failed;
  const requestId = response.headers()["x-request-id"];
  assert.match(requestId, /^[0-9a-f]{32}$/);
  await expect(page.getByRole("alert")).toContainText(requestId);
  await expect.poll(() => log.includes("Request " + requestId)).toBe(true);

  const listPattern = "**/api/inspections?*";
  for (const defect of ["missing-station", "string-total", "invalid-json"]) {
    await page.route(listPattern, async route => {
      const actual = await route.fetch();
      if (defect === "invalid-json") {
        await route.fulfill({ response: actual, body: "{not-json" });
        return;
      }
      const payload = await actual.json();
      if (defect === "missing-station") delete payload.items[0].station;
      else payload.total = "two";
      await route.fulfill({ response: actual, json: payload });
    });
    await page.goto(origin);
    await expect(page.getByRole("alert")).toContainText(defect === "invalid-json" ? "响应不是有效的 JSON" : "接口数据格式不符合约定");
    await expect(page.locator("tbody tr")).toHaveCount(0);
    await page.unroute(listPattern);
  }
  await page.route(listPattern, async route => {
    const actual = await route.fetch();
    const payload = await actual.json();
    payload.futureField = "compatible addition";
    await route.fulfill({ response: actual, json: payload });
  });
  await page.goto(origin);
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await page.unroute(listPattern);
  console.log("PASS runtime payload validation: missing field, wrong type, invalid JSON and additive field.");
  console.log("PASS browser acceptance: filter, paging, CSV download, image and original PGM, viewer role, review, restart, UTC form and request ID.");
  const heldRead = Promise.withResolvers();
  const releaseRead = Promise.withResolvers();
  const filteredUrl = origin + "/?station=station-a&result=NG";
  await page.route(listPattern, async route => {
    const actual = await route.fetch();
    heldRead.resolve();
    await releaseRead.promise;
    await route.fulfill({ response: actual }).catch(() => {});
  }, { times: 1 });
  const timedOutRequest = page.waitForEvent("requestfailed", {
    predicate: request => new URL(request.url()).pathname === "/api/inspections",
    timeout: 20_000,
  });
  try {
    await page.goto(filteredUrl);
    await heldRead.promise;
    await expect(page.getByRole("status")).toHaveText("正在加载记录…");
    await expect(page.getByRole("alert")).toHaveText("读取超过 15 秒，请重试。", { timeout: 20_000 });
    assert.match((await timedOutRequest).failure()?.errorText ?? "", /abort/i);
  } finally {
    releaseRead.resolve();
    await page.unrouteAll({ behavior: "wait" });
  }
  const retryRead = page.waitForRequest(request => new URL(request.url()).pathname === "/api/inspections");
  await page.getByRole("button", { name: "重新加载记录", exact: true }).click();
  const retryQuery = new URL((await retryRead).url()).searchParams;
  assert.equal(retryQuery.get("station"), "station-a");
  assert.equal(retryQuery.get("result"), "NG");
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(page.getByRole("alert")).toHaveCount(0);
  assert.equal(page.url(), filteredUrl);

  await page.route(listPattern, route => route.fulfill({
    status: 429, headers: { "Retry-After": "2", "X-Request-Id": "6".repeat(32) },
  }), { times: 1 });
  await page.goto(filteredUrl);
  await expect(page.getByRole("alert")).toContainText("查询过于频繁，请在 2 秒后重试。");
  await expect(page.getByRole("alert")).toContainText("6".repeat(32));
  await new Promise(resolve => setTimeout(resolve, 2000));
  await page.getByRole("button", { name: "重新加载记录", exact: true }).click();
  await expect(page.locator("tbody tr")).toHaveCount(1);
  assert.equal(page.url(), filteredUrl);
  console.log("PASS 429 read feedback: suggested wait, request ID and retry with original filters.");

  await page.route("**/api/inspections/" + detailId, route =>
    route.fulfill({ status: 503, headers: { "X-Request-Id": "5".repeat(32) } }), { times: 1 });
  const retryDetailUrl = filteredUrl + "&id=" + detailId;
  await page.goto(retryDetailUrl);
  await expect(page.getByRole("alert")).toContainText("5".repeat(32));
  await page.getByRole("button", { name: "重新加载详情", exact: true }).click();
  await expect(page.locator("img.evidence")).toBeVisible();
  assert.equal(page.url(), retryDetailUrl);
  console.log("PASS read recovery: 15-second deadline cancels request, retry keeps filters and detail retries in place.");

  copyNg("pending-workbench-old", "pending-workbench", 100n);
  copyNg("pending-workbench-new", "pending-workbench", 110n);
  await page.goto(origin + "/?station=pending-workbench&result=NG&reviewStatus=pending&paging=cursor");
  await expect(page.locator("tbody button")).toHaveText(["pending-workbench-new"]);
  await page.getByRole("button", { name: "加载更多", exact: true }).click();
  await expect(page.locator("tbody button")).toHaveText(["pending-workbench-new", "pending-workbench-old"]);
  await page.getByRole("button", { name: "pending-workbench-new", exact: true }).click();
  await page.getByRole("textbox", { name: "说明", exact: true }).fill("Reviewed from pending workbench");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(page.locator(".review > dl")).toContainText("Reviewed from pending workbench");
  await expect(page.locator("tbody button")).toHaveText(["pending-workbench-old"]);
  assert.equal(new URL(page.url()).searchParams.get("id"), "pending-workbench-new");
  const pendingCsvEvent = page.waitForEvent("download");
  await page.getByRole("button", { name: "导出当前筛选结果 CSV", exact: true }).click();
  const pendingCsv = await pendingCsvEvent;
  const pendingCsvFile = join(work, "pending.csv");
  await pendingCsv.saveAs(pendingCsvFile);
  const pendingCsvText = readFileSync(pendingCsvFile, "utf8");
  assert.ok(pendingCsvText.includes("pending-workbench-old"));
  assert.ok(!pendingCsvText.includes("pending-workbench-new"));
  await page.getByRole("combobox", { name: "复核状态", exact: true }).selectOption("reviewed");
  await expect(page.locator("tbody button")).toHaveText(["pending-workbench-new"]);
  await page.getByRole("combobox", { name: "分页方式", exact: true }).selectOption("page");
  await expect(page.locator("tbody button")).toHaveText(["pending-workbench-new"]);
  await page.reload();
  await expect(page.locator("tbody button")).toHaveText(["pending-workbench-new"]);
  assert.equal(new URL(page.url()).searchParams.get("reviewStatus"), "reviewed");
  console.log("PASS pending workbench: cursor reset after save, retained detail, matching CSV and reviewed URL restore.");

  copyNg("last-page-old", "last-page-workbench", 120n);
  copyNg("last-page-new", "last-page-workbench", 130n);
  const lastPageUrl = origin + "/?station=last-page-workbench&result=NG&reviewStatus=pending&page=2";
  await page.goto(lastPageUrl);
  await expect(page.locator("tbody button")).toHaveText(["last-page-new"]);
  await page.getByRole("button", { name: "last-page-new", exact: true }).click();
  await page.getByRole("textbox", { name: "说明", exact: true }).fill("Complete last page");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "记录已变化" })).toHaveText("记录已变化，当前只有 1 页。");
  await expect(page.locator(".review > dl")).toContainText("Complete last page");
  await page.getByRole("button", { name: "回到最后一页", exact: true }).click();
  await expect(page.locator("tbody button")).toHaveText(["last-page-old"]);
  assert.equal(new URL(page.url()).searchParams.get("id"), "last-page-new");
  assert.equal(new URL(page.url()).searchParams.get("station"), "last-page-workbench");
  assert.equal(new URL(page.url()).searchParams.has("page"), false);
  await expect(page.locator(".review > dl")).toContainText("Complete last page");
  await page.getByRole("button", { name: "last-page-old", exact: true }).click();
  await page.getByRole("textbox", { name: "说明", exact: true }).fill("Complete remaining task");
  await page.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "没有符合条件" })).toHaveText("没有符合条件的记录。");
  await expect(page.locator(".review > dl")).toContainText("Complete remaining task");
  await page.goto(lastPageUrl + "&id=last-page-old");
  await page.getByRole("button", { name: "回到第 1 页", exact: true }).click();
  assert.equal(new URL(page.url()).searchParams.get("id"), "last-page-old");
  assert.equal(new URL(page.url()).searchParams.get("reviewStatus"), "pending");
  assert.equal(new URL(page.url()).searchParams.has("page"), false);
  await expect(page.locator(".review > dl")).toContainText("Complete remaining task");
  console.log("PASS last-page recovery: shrinking page count, retained filters/detail and empty-range reset.");

  copyNg("external-page-task", "external-review-workbench", 140n);
  const externalUrl = origin + "/?station=external-review-workbench&result=NG&reviewStatus=pending";
  await page.goto(externalUrl);
  await expect(page.locator("tbody button")).toHaveText(["external-page-task"]);
  const externalContext = await browser.newContext();
  const external = await externalContext.newPage();
  await external.goto(origin + "/?id=external-page-task");
  await login(external, "bob");
  await external.getByRole("textbox", { name: "说明", exact: true }).fill("Completed by another reviewer");
  await external.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(external.locator(".review > dl")).toContainText("Completed by another reviewer");
  await expect(page.locator("tbody button")).toHaveText(["external-page-task"]);
  await page.getByRole("button", { name: "刷新复核列表", exact: true }).click();
  await expect(page.locator("tbody button")).toHaveCount(0);
  await expect(page.getByRole("status")).toHaveText("没有符合条件的记录。");
  assert.equal(page.url(), externalUrl);
  await page.getByRole("combobox", { name: "复核状态", exact: true }).selectOption("reviewed");
  await expect(page.locator("tbody button")).toHaveText(["external-page-task"]);

  copyNg("external-cursor-task", "external-review-workbench", 150n);
  await page.goto(externalUrl + "&paging=cursor");
  await expect(page.locator("tbody button")).toHaveText(["external-cursor-task"]);
  await external.goto(origin + "/?id=external-cursor-task");
  await external.getByRole("textbox", { name: "说明", exact: true }).fill("Another reviewer completed cursor task");
  await external.getByRole("button", { name: "提交复核", exact: true }).click();
  await expect(external.locator(".review > dl")).toContainText("Another reviewer completed cursor task");
  await expect(page.locator("tbody button")).toHaveText(["external-cursor-task"]);
  await page.getByRole("button", { name: "刷新复核列表", exact: true }).click();
  await expect(page.locator("tbody button")).toHaveCount(0);
  await expect(page.getByRole("status")).toHaveText("没有符合条件的记录。");
  assert.equal(page.url(), externalUrl + "&paging=cursor");
  await externalContext.close();
  console.log("PASS collaborative refresh: independent reviewer, page/cursor refresh, empty result and unchanged filters.");

  copyNg("cursor-station-b", "station-b");
  copyNg("cursor-newest", "station-a");

  await page.goto(origin);
  await page.getByRole("combobox", { name: "分页方式" }).selectOption("cursor");
  await expect(page.getByRole("status")).toHaveText("请选择一个工位和 OK 或 NG 结果。");
  await page.getByRole("combobox", { name: "工位" }).selectOption("station-a");
  await page.getByRole("combobox", { name: "结果" }).selectOption("NG");
  const rows = page.locator("tbody button");
  const more = page.getByRole("button", { name: "加载更多", exact: true });
  const refresh = page.getByRole("button", { name: "刷新列表", exact: true });
  await expect(rows).toHaveText(["cursor-newest"]);
  assert.equal(new URL(page.url()).searchParams.get("paging"), "cursor");

  const cursorPattern = "**/api/inspection-pages?*";
  const isContinuation = request => {
    const url = new URL(request.url());
    return url.pathname === "/api/inspection-pages" && url.searchParams.has("after");
  };
  let retriedCursor;
  await page.route(cursorPattern, async route => {
    retriedCursor = new URL(route.request().url()).searchParams.get("after");
    assert.ok(retriedCursor);
    await route.fulfill({ status: 503, headers: { "X-Request-Id": "1".repeat(32) } });
  }, { times: 1 });
  await more.click();
  await expect(page.getByRole("alert")).toContainText("1".repeat(32));
  await expect(rows).toHaveText(["cursor-newest"]);
  const retry = page.waitForRequest(isContinuation);
  await more.click();
  assert.equal(new URL((await retry).url()).searchParams.get("after"), retriedCursor);
  await expect(rows).toHaveCount(2);
  await expect(rows.first()).toHaveText("cursor-newest");
  await expect(more).toHaveCount(0);
  await expect(page.getByRole("status")).toHaveText("已加载全部记录。");
  assert.equal(new URL(page.url()).searchParams.has("after"), false);

  await refresh.click();
  await expect(rows).toHaveText(["cursor-newest"]);
  const held = Promise.withResolvers();
  const release = Promise.withResolvers();
  await page.route(cursorPattern, async route => {
    if (!isContinuation(route.request())) {
      await route.continue();
      return;
    }
    const actual = await route.fetch();
    held.resolve(actual.status());
    await release.promise;
    await route.fulfill({ response: actual }).catch(() => {});
  });
  try {
    await more.click();
    assert.equal(await held.promise, 200);
    await expect(more).toBeDisabled();
    const cancelled = page.waitForEvent("requestfailed", { predicate: isContinuation });
    await page.getByRole("combobox", { name: "工位" }).selectOption("station-b");
    await expect(rows).toHaveText(["cursor-station-b"]);
    assert.match((await cancelled).failure()?.errorText ?? "", /abort/i);
  } finally {
    release.resolve();
    await page.unrouteAll({ behavior: "wait" });
  }
  await expect(rows).toHaveText(["cursor-station-b"]);
  await page.goBack();
  await expect(rows).toHaveText(["cursor-newest"]);
  await expect(more).toBeEnabled();
  await page.getByRole("combobox", { name: "分页方式" }).selectOption("page");
  await expect(page.getByRole("navigation", { name: "检测记录分页" })).toBeVisible();
  console.log("PASS cursor browser acceptance: append, retry, terminal page, refresh, cancellation, history and page-mode switch.");
}

async function verifyPageSize(page) {
  const size = page.getByRole("combobox", { name: "每页条数", exact: true });
  const rows = page.locator("tbody button");
  await page.goto(origin + "/?station=station-a&page=2");
  await expect(rows).toHaveCount(1);
  await rows.first().click();
  await expect(page.getByRole("heading", { name: "周期详情", exact: true })).toBeVisible();
  await size.selectOption("10");
  await expect(rows).toHaveCount(2);
  let url = new URL(page.url());
  assert.equal(url.searchParams.get("pageSize"), "10");
  assert.equal(url.searchParams.get("station"), "station-a");
  assert.equal(url.searchParams.has("page"), false);
  assert.equal(url.searchParams.has("id"), false);
  await expect(page.getByRole("heading", { name: "周期详情", exact: true })).toHaveCount(0);
  await page.reload();
  await expect(size).toHaveValue("10");
  await expect(rows).toHaveCount(2);
  await page.goBack();
  await expect(size).toHaveValue("1");
  await expect(rows).toHaveCount(1);
  await page.goForward();
  await expect(size).toHaveValue("10");
  await expect(rows).toHaveCount(2);
  await page.goto(origin + "/?station=station-a&result=NG&paging=cursor");
  await expect(rows).toHaveCount(1);
  const fresh = page.waitForRequest(request => {
    const target = new URL(request.url());
    return target.pathname === "/api/inspection-pages" && target.searchParams.get("pageSize") === "20" && !target.searchParams.has("after");
  });
  await size.selectOption("20");
  await fresh;
  await expect(rows).toHaveCount(1);
  url = new URL(page.url());
  assert.equal(url.searchParams.get("paging"), "cursor");
  assert.equal(url.searchParams.get("result"), "NG");
  for (const invalid of ["999", "0", "10.5", "1e1", "abc"]) {
    await page.goto(origin + "/?station=station-a&pageSize=" + invalid);
    await expect(size).toHaveValue("1");
    await expect(rows).toHaveCount(1);
  }
  console.log("PASS page size: page/detail reset, filters, reload, history, fresh cursor and invalid URL fallback.");
}

async function verifyClearFilters(page) {
  const clear = page.getByRole("button", { name: "清除筛选", exact: true });
  const initial = "/?station=station-a&result=NG&reviewStatus=pending&since=2020-01-01T00%3A00%3A00.000Z&until=2099-01-01T00%3A00%3A00.000Z&page=2&id=missing&pageSize=10";
  await page.goto(origin + initial);
  await expect(page.getByRole("heading", { name: "周期详情", exact: true })).toBeVisible();
  await clear.click();
  await expect(page.locator("tbody button")).toHaveCount(2);
  assert.equal(new URL(page.url()).search, "?pageSize=10");
  for (const name of ["工位", "结果", "复核状态"]) {
    await expect(page.getByRole("combobox", { name, exact: true })).toHaveValue("");
  }
  await expect(page.getByRole("heading", { name: "周期详情", exact: true })).toHaveCount(0);
  await expect(page.getByLabel("开始时间", { exact: true })).toHaveValue("");
  await expect(page.getByLabel("结束时间", { exact: true })).toHaveValue("");
  await page.goBack();
  await expect(page.getByRole("combobox", { name: "复核状态", exact: true })).toHaveValue("pending");
  await expect(page.getByLabel("开始时间", { exact: true })).not.toHaveValue("");
  await page.goForward();
  await expect(page.locator("tbody button")).toHaveCount(2);
  // With an already-clear URL, clearing must still discard unapplied time drafts.
  await page.getByLabel("开始时间", { exact: true }).fill("2027-01-02T00:00");
  await page.getByLabel("结束时间", { exact: true }).fill("2027-01-01T00:00");
  await page.getByRole("button", { name: "应用时间范围", exact: true }).click();
  await expect(page.getByText("结束时间必须晚于开始时间。", { exact: true })).toBeVisible();
  await clear.click();
  await expect(page.getByLabel("开始时间", { exact: true })).toHaveValue("");
  await expect(page.getByText("结束时间必须晚于开始时间。", { exact: true })).toHaveCount(0);
  assert.equal(new URL(page.url()).search, "?pageSize=10");
  await page.goto(origin + "/?station=station-a&result=NG&paging=cursor&pageSize=20");
  await expect(page.locator("tbody button")).toHaveCount(1);
  await clear.click();
  await expect(page.getByText("请选择一个工位和 OK 或 NG 结果。", { exact: true })).toBeVisible();
  assert.equal(new URL(page.url()).search, "?paging=cursor&pageSize=20");
  await expect(page.getByRole("combobox", { name: "分页方式", exact: true })).toHaveValue("cursor");
  console.log("PASS clear filters: URL, controls, history, retained preferences, time drafts/errors and cursor prerequisites.");
}

async function verifyLiveCursor(page) {
  copyNg("live-first", "station-a");
  let changeMessages = 0;
  page.on("websocket", socket => {
    socket.on("framereceived", ({ payload }) => {
      if (payload.toString().includes('"target":"InspectionsChanged"')) changeMessages++;
    });
  });
  const notice = page.getByText("当前筛选有更新。点击「刷新列表」查看。", { exact: true });
  const connected = page.getByText("实时连接正常", { exact: true });
  const rows = page.locator("tbody button");
  await page.goto(origin + "/?paging=cursor&station=station-a&result=NG");
  await expect(connected).toBeVisible();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toHaveText("live-first");
  await page.getByRole("button", { name: "加载更多", exact: true }).click();
  await expect(rows).toHaveCount(2);
  const previousRows = await rows.allTextContents();

  const previousMessages = changeMessages;
  const probe = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === "/api/inspection-pages" && !url.searchParams.has("after") && response.ok();
  });
  copyNg("unrelated-station", "station-b");
  await expect.poll(() => changeMessages).toBeGreaterThan(previousMessages);
  assert.equal((await (await probe).json()).items[0].id, "live-first");
  await expect(notice).toHaveCount(0);
  await expect(rows).toHaveText(previousRows);

  copyNg("live-new-head", "station-a", 20n);
  await expect(notice).toBeVisible();
  await expect(rows).toHaveText(previousRows);
  const refresh = page.getByRole("button", { name: "刷新列表", exact: true });
  await refresh.click();
  await expect(rows).toHaveText(["live-new-head"]);
  await expect(notice).toHaveCount(0);

  const port = Number(new URL(origin).port);
  await stopApi();
  await expect(connected).toHaveCount(0);
  copyNg("live-during-outage", "station-a", 30n);
  await startApi(port);
  await expect(connected).toBeVisible();
  await expect(notice).toBeVisible();
  await expect(rows).toHaveText(["live-new-head"]);
  await refresh.click();
  await expect(rows).toHaveText(["live-during-outage"]);
  await expect(notice).toHaveCount(0);
  console.log("PASS live cursor: filtered SignalR hint, retained rows, manual refresh and reconnect catch-up.");
}

try {
  await startApi();
  browser = await chromium.launch({ channel: "msedge", headless: true });
  const context = await browser.newContext({ timezoneId: "Asia/Shanghai", acceptDownloads: true });
  const page = await context.newPage();
  page.setDefaultTimeout(10000);
  await verifyPageSize(page);
  await verifyClearFilters(page);
  if (process.argv.includes("--live-cursor")) await verifyLiveCursor(page);
  else await verifyReview(page);
  console.log("Retained acceptance files:", work);
} catch (error) {
  writeFileSync(join(work, "api.log"), log);
  console.error("Acceptance failed. API log:", join(work, "api.log"));
  throw error;
} finally {
  await browser?.close();
  await stopApi();
}
