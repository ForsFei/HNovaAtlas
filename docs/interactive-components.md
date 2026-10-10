# 交互学习组件（Phase 8）

组件直接用于可信 `.mdx` 文章，无需 import。普通 `.md` 不用于 JSX 练习；改扩展名不改变文章路径生成的 URL 或稳定 id。正文仍从顶层 `##` 开始。

## Quiz

```mdx
<Quiz
  question="PDO 和 SDO 哪个适合周期性实时数据？"
  options={["PDO", "SDO", "两者相同"]}
  answer={0}
  explanation="PDO 用于周期性交换过程数据；SDO 用于访问对象参数。"
/>
```

`answer` 是从 0 开始的整数下标；2–8 个选项，去除首尾空白后不可重复。点击选项立即反馈，错误时同时显示正确答案；可以改选或重新作答。`explanation` 可选，省略时使用正确选项生成基本解释，正式文章应提供原因。

选项是原生按钮，Tab 定位、Enter / Space 激活；`aria-pressed` 表达选择，状态区礼貌播报结果。不只通过颜色判断对错。“查看答案与解释”始终是原生可展开文字后备。

## FlashCard

```mdx
<FlashCard front="PDO 的英文全称是什么？" back="Process Data Object（过程数据对象）。" />
```

点击、Enter / Space 在正反两面切换，`aria-expanded` 表达答案是否展开；每张卡独立保存状态。“阅读文字版”保留正反面，适合无 JavaScript 或直接阅读。不依赖翻转动画。

## Stepper

```mdx
<Stepper title="连接建立" steps={[
  { title: "SYN", description: "服务端已收到客户端 SYN。", states: [{ label: "客户端", value: "SYN-SENT" }, { label: "服务端", value: "SYN-RECEIVED" }] },
  { title: "SYN + ACK", description: "客户端已收到有效 SYN + ACK。", states: [{ label: "客户端", value: "ESTABLISHED" }, { label: "服务端", value: "SYN-RECEIVED" }] },
  { title: "ACK", description: "服务端已收到有效 ACK，双方已建立连接。", states: [{ label: "客户端", value: "ESTABLISHED" }, { label: "服务端", value: "ESTABLISHED" }] }
]} />
```

1–20 步；每步必填 `title` / `description`，`states` 可省略，每步最多 8 项 `{label, value}`，同一步 label 不重复。初始显示第 1 步，支持上一步、下一步、从头开始、直接选择；首尾按钮禁用，不越界、不自动播放。切换后焦点进入状态区域，步数通过状态区播报；按 Tab 可继续操作。完整步骤始终保留在“阅读全部步骤”中。

TCP 示例明确以报文到达接收端后的状态为观察点，依据 [RFC 9293 Figure 6](https://www.rfc-editor.org/rfc/rfc9293.html#section-3.5)。这不是完整协议模拟器，未模拟重传、同时打开或网络异常。

## 契约与维护

- 所有文本属性仅接受非空纯字符串，去除首尾空白，最长 4,000 字符；不解析 HTML / Markdown / JavaScript 字符串。
- 服务端使用严格 Zod Schema 校验，未知属性、函数回调、错误答案或无效结构会使构建失败。客户端只接收可序列化数据，不带 Schema 运行逻辑。
- 状态仅在当前挂载页面内有效，重新加载或组件重新挂载后重置；没有账号、评分记录、localStorage 或持久化进度。主题保存仍由既有主题系统负责。
- 题目、正确答案、卡片两面和完整步骤静态输出并进入 Pagefind；重复交互区域、反馈和操作文案不索引。
- 答案会公开出现在 HTML 和客户端数据中，适合个人学习，不适合保密考试。
- 组件校验不是 MDX 执行沙箱；只编译已审核内容。首页独立实验室仍为筹备目录，文章内练习不等于完整可视化实验上线。

实现位于 `src/components/interactive`，契约和状态辅助函数位于 `src/lib/interactive`，样式复用语义 Token。维护运行 lint、typecheck、test:content、build，并检查生产浏览器键盘操作、多个实例、首尾步骤、主题与手机宽度。
