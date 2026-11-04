# 工位原图与测量证据（离线）

第12步第九批。net10.0、纯托管，引用原组合工位；无新包、相机、雷赛、PLC、原生SDK或网络。

```powershell
dotnet build examples/industrial-station-evidence/IndustrialStation.Evidence.csproj -c Release
dotnet run --project examples/industrial-station-evidence/IndustrialStation.Evidence.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-evidence/IndustrialStation.Evidence.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2；创建唯一系统临时目录并打印、保留证据。默认WPF/MC入口不变。

## 使用顺序

显式准备原模型/回放，完成周期后ExportEvidence取得同一保留帧及原InspectionResult。EvidenceWorkflow.SaveThenAcknowledgeAsync依次导出→后台Save并读回验证→原SaveResult→原视觉Ack。随后显式模拟Review。一个合作所有者顺序调用会话，不能绕开工作流并发操作；saving仅防同实例重入。

EvidenceStore持有独占.writer.lock；每周期GUID-Cycle目录包含image.pgm和manifest.json。P5/Mono8有效像素、全文件SHA256、工位Terminal/FrameId、FrameStamp、请求/处理会话/触发、固定配方、实际ROI统计。读取重算统计并检查判定，无设备调用或状态推进。

两个CreateNew文件分别Flush(true)/关闭，再同父目录不覆盖发布，读回验证。已存在相同证据幂等，不同内容拒绝。发布失败保留.pending并阻止Store继续保存；重开发现.pending拒绝。不得自动清理现场或重拍补图。

仅支持成功检测且工位Completed（含OK/NG）的原图证据。故障/停止不伪造图，仍使用原账本。证据与账本没有跨目录事务；批量重启对账、异常周期分类下一批补。仅可信专用目录，哈希不是防篡改或曝光因果证明，无断电/长期性能/实机验收。

教程：
- /knowledge/industrial/host-computer/station/original-image-measurement-evidence-and-publication/
- /knowledge/industrial/host-computer/station/csharp-image-evidence-store-and-save-workflow/

## 本批验证

Release零警告零错误；新增52项留图/身份/幂等/失败/损坏/重开检查，原组合49与视觉47回归通过，共148项。Demo原图均值220/75，对应OK/NG；保存和读取不增加Move/采图/测量次数。源文件、构建运行目录与实际Demo证据已核对物理文件头为明文。

## 第十批：异常诊断与只读对账

新增--reconciliation-test / --reconciliation-demo。AbnormalEvidence.Capture要求原Faulted/Stopped与实际Task/Pending/模型结清；仅关联当前周期Capture操作，分无合格帧、采图未合格、未接受帧、已接受帧。DiagnosticOnly-NoPixels记录帧身份/统计/处理错误，不保存图像字节，不把诊断OK/NG覆盖原终态。

AbnormalEvidenceStore另存诊断JSON，CreateNew/Flush/不覆盖发布/读回，失败保留.pending并阻止继续。EvidenceWorkflow.SaveAbnormalThenAcknowledgeAsync先存说明，再原终态保存/消费/视觉Ack；采用异常只留诊断的显式政策，生产若要求异常原图不能直接照搬。旧正常路径不变。

EvidenceReconciler.Inspect(ledger,normal,abnormal)只读现有根目录和现有锁，持有合作读锁；StationLedger.OpenReadOnly不创建文件、拒绝所有写方法。分类匹配、缺终态、缺证据、孤立、冲突、坏材料与重复，StorageIssues单列.pending/不可读/超限。RequiresAttention包含未Review周期，文件匹配不会自动复核/放行。应关闭原应用写者后离线核对。

新增52项异常/只读检查，原留图52+账本59+应用44+组合49+视觉47，共303项通过。Demo匹配异常但reviewed=False/attention=True。两个原项目的InternalsVisibleTo只用于测试现有门闩/处理异常，不新增设备参数。没有跨目录事务、图像认证、突然断电、实际硬件或长期现场验收；下一批实机接入前验收与故障演练。

教程：
- /knowledge/industrial/host-computer/station/abnormal-cycle-diagnostics-and-restart-reconciliation/
- /knowledge/industrial/host-computer/station/csharp-abnormal-evidence-and-readonly-reconciliation/


## 第十三批：异常原始像素

ExportAbnormalFrame在原Task/Pending结清且确认前返回原OwnedFrame；无图、错周期、确认/关闭后拒绝。AbnormalImageArchive独立images目录保存单份JSON（Mono8字节Base64、尺寸、SHA及原异常诊断），Save幂等/冲突拒绝，Read有界严格校验，Restore返回复制图。先存图/校验再原异常工作流，不改原故障、不补拍、不伪造统计。旧对账器不扫描images。新增13项，--reconciliation-test合计65项通过，含两类迟到与实际处理异常。

教程：/knowledge/industrial/host-computer/station/abnormal-original-pixels-and-diagnostic-archives/ 与 /knowledge/industrial/host-computer/station/csharp-abnormal-frame-export-and-image-archive/。


## 第十四批：只读图像索引

新增--image-reconciliation-test（15项）。ImageEvidenceReconciler.Inspect(ledger,normal,abnormal,images)叠加原报告与像素完整性列，完整Diagnostic比较，区分Verified/Missing/StorageUnavailable/Invalid/DiagnosticConflict/BaseNotMatched/NoFrameExpected/Orphan。未知条目和.pending单列，不写入、修复、确认或放行。关闭写者后离线读取，不保证跨目录一致快照。


使用 `--inspect-images <ledger> <normal> <abnormal> <images>` 输出只读核对 JSON。退出码 0 表示没有待关注项，3 表示报告需要关注，2 表示参数错误，1 表示执行异常。报告输出请放在证据目录之外。


`--image-reconciliation-demo` 运行迟到检测并输出四个证据目录和周期身份的 JSON，可直接传给 `--inspect-images`。演示生成成功返回 0，核对报告因周期仍待复核返回 3。
