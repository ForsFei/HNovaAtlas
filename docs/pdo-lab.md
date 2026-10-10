# EtherCAT PDO 过程数据实验

访问 `/labs/ethercat-pdo/`。实验是纯浏览器端的教学模型，页面本身可静态导出，无数据库、设备连接或后端运行时。

## 如何操作

1. 拖动 Cycle Time。页面将 `1,000,000 ÷ 周期(µs)` 显示为理论每秒周期数；**不改变**固定的演示播放速度，也不预测现场可达周期。
2. 调整从站数量（1–5）和示例 PDO 映射。基础档每站每方向 2 Byte；扩展档每站每方向 6 Byte。配置变更会暂停并重置当前演示。
3. 点击“下一步”或“播放”，跟随主站准备、各从站处理、返回主站的概念顺序。点击从站可查看其 RxPDO/TxPDO 字段及分别计算的输入/输出字节偏移。

实验把相同示例从站的整字节字段顺序拼接到两张独立的过程映像。它不编码真实 EtherCAT 帧，不模拟物理传播、同步、工作计数器、设备状态、位级打包、帧头开销或多报文分配。字段名称和大小仅为教学示例，不对应具体型号；实际项目必须核对 ESI、设备手册和主站配置。

模型计算在 `src/lib/labs/pdo-model.ts`，交互在 `src/components/labs/PdoLab.tsx`，独立静态路由在 `src/app/labs/ethercat-pdo/page.tsx`。测试覆盖映射、偏移、阶段与周期换算。

配套文章：PDO 概览、PDO Mapping、过程映像。协议事实参考 [ETG 技术概览](https://www.ethercat.org/en/technology.html)、[Beckhoff PDO 说明](https://infosys.beckhoff.com/content/1033/cx1500_fb_510/3222417931.html) 与[设备过程映像分配示例](https://infosys.beckhoff.com/content/1033/epp1xxx/15127234443.html)。
