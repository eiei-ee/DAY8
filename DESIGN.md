---
name: "DAY 8 / 第八天"
description: "A restrained observation-room interface where evidence, memory, and agency stay legible."
colors:
  void: "#0F1114"
  surface: "#15181C"
  divider: "#262B31"
  text: "#C9D1D9"
  muted: "#7B838C"
  signal-amber: "#D9A05B"
  danger: "#B8433A"
  success: "#7BAE7F"
typography:
  title:
    fontFamily: "LegacyRuntime.ttf, Microsoft YaHei UI, sans-serif"
    fontSize: "18px"
    fontWeight: 500
    lineHeight: 1.25
  body:
    fontFamily: "LegacyRuntime.ttf, Microsoft YaHei UI, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "LegacyRuntime.ttf, Microsoft YaHei UI, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.35
rounded:
  none: "0px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "20px"
  choice-gap: "8px"
components:
  button-primary:
    backgroundColor: "{colors.signal-amber}"
    textColor: "{colors.void}"
    rounded: "{rounded.none}"
    padding: "12px 16px"
    height: "44px"
  button-choice:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.none}"
    padding: "10px 14px"
    height: "44px"
  input:
    backgroundColor: "{colors.void}"
    textColor: "{colors.text}"
    rounded: "{rounded.none}"
    padding: "10px 12px"
    height: "40px"
---

# Design System: DAY 8 / 第八天

## Overview

**Creative North Star: "The Quiet Observation Chamber"**

界面是一间安静但正在工作的观察室。左侧空间让玩家看见人工生命与环境的变化，右侧控制台只呈现此刻需要理解的事实、历史和选择；稀少的琥珀色像设备指示灯，只在人工生命发言、关键证据和主行动上出现。

系统必须保持克制、临床、有人性。它明确拒绝霓虹赛博朋克、紫蓝渐变、泛滥故障特效和“程序员控制台”式信息倾倒。动效只说明状态变化，并且任何内容在动效未完成时仍然可访问。

**Key Characteristics:**

- 深色、低反射的观察室表面
- 稀少而有意义的琥珀信号
- 明确分离的世界事实、它的表达和系统解释
- 方正、稳定、可预测的交互组件
- 关系变化通过空间、声音和措辞呈现

## Colors

这是一套低色度实验室中性色，单一琥珀色负责生命、注意和主行动。

### Primary

- **生命信号琥珀**：用于人工生命发言、当前周期和唯一主行动；单屏面积不得超过 10%。

### Secondary

- **危险断电红**：只用于真正危险、失电和不可逆警告。
- **修复确认绿**：只用于已验证的修复或成功状态。

### Neutral

- **深空底色**：全局背景与输入底面。
- **控制台表面**：面板、选择行和顶栏。
- **结构分隔线**：边界与焦点前的静态轮廓。
- **主文字灰白**：正文和可操作标签。
- **历史静音灰**：次要元数据；不能承载关键选择。

**The One Signal Rule.** 琥珀色只表示“现在值得注意”，禁止把它当装饰铺满界面。

## Typography

**Display Font:** LegacyRuntime.ttf（中文回退 Microsoft YaHei UI）
**Body Font:** LegacyRuntime.ttf（中文回退 Microsoft YaHei UI）

**Character:** 单一无衬线体系维持控制台可信度；层级来自字号、颜色和留白，而不是夸张字重。

### Hierarchy

- **Title**（500，18px，1.25）：周期、场景名与终局标题。
- **Body**（400，15px，1.5）：情境说明、人工生命台词和选择；叙事段落控制在约 36 个汉字宽度内。
- **Label**（400，13px，1.35）：能源、记忆完整性、输入提示与证据元数据。

**The Evidence Voice Rule.** “世界｜”“它｜”“系统｜”必须稳定使用各自前缀，不能靠颜色单独区分来源。

## Elevation

系统完全依靠色面和一像素结构线建立深度，不使用装饰性阴影。焦点与选中状态通过明度、轮廓和短促过渡表达。

**The Flat Chamber Rule.** 静止界面保持平面；只有交互焦点和危险状态可以短暂抬升视觉优先级。

## Components

### Buttons

- **Shape:** 方正边缘（0px），最小高度 44px，整行均可点击。
- **Primary:** 琥珀底、深色文字，只用于开始、继续、查看报告和重新开始。
- **Choice:** 控制台表面底、主文字、清晰一像素轮廓；前置数字键提示属于组件本身。
- **Hover / Focus:** 150–200ms 内提升表面明度，并显示琥珀焦点轮廓；键盘焦点与鼠标悬停等价。
- **Disabled:** 降低整体对比度并保留标签，不隐藏合法性原因。

### Cards / Containers

- **Corner Style:** 方正（0px）。
- **Background:** 深空底色用于日志，控制台表面用于结构区。
- **Shadow Strategy:** 无阴影。
- **Border:** 一像素结构分隔线。
- **Internal Padding:** 16px；密集元数据可降至 8px。

### Inputs / Fields

- **Style:** 深空底色、一像素结构线、12px 横向内边距。
- **Focus:** 琥珀轮廓与明确的输入光标。
- **Error / Disabled:** 使用文字说明并辅以危险红；禁止只变色。

### Navigation

顶栏只显示当前日、场景和三个最重要状态。它不承担菜单功能；回到标题或新开一局只能在明确的阶段转换处出现。

### Observation Log

日志按“世界、你、它、系统”区分说话者。关键事件先显示结果，再显示可折叠或弱化的证据依据；自动滚动必须始终落在完整行边界。

## Do's and Don'ts

### Do:

- **Do** 在 1600×900、1920×1080 和 2560×1600 验证所有按钮、日志和输入框均在屏幕内。
- **Do** 为所有关键选择同时提供鼠标与数字键路径，并让键位直接显示在按钮上。
- **Do** 用距离、大小、扫描声和句式变化表现人工生命的关系状态。
- **Do** 让结局理由只引用仍然存在的事件 ID，并清楚标记模板或本地模型来源。

### Don't:

- **Don't** 做霓虹赛博朋克、紫蓝渐变或泛滥的故障特效。
- **Don't** 做充满调试数字、概率和内部变量的“程序员控制台”。
- **Don't** 用花哨动画掩盖因果不清，也不要让内容等待动画后才可操作。
- **Don't** 让大模型台词替代世界事实、可执行行为或真实记忆证据。
- **Don't** 使用装饰性阴影、玻璃拟态、渐变文字或大圆角卡片。
