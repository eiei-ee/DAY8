# 《第八天 / DAY 8》Unity 版（团结引擎 2022.3.62t8）

真人可玩完整版：七个实验周期 + 第八天断电结算。

## 运行

直接运行 `../Build/Day8/Day8.exe`（构建产物在工程目录外层 `Build/Day8/`）。
鼠标点击选项，或键盘：**数字键 1-9** 选选项，**回车** 确认最后（主）选项。

## 系统

| 模块 | 说明 |
|---|---|
| `Core/Memory.cs` | 结构化记忆、显著度、夜间整合、记忆清除（真删数据） |
| `Core/Beliefs.cs` | Beta 分布信念（情境标签）、玩家模型 6 维度 |
| `Core/Perception.cs` | 感知过滤：控制台后的操作"仅结果可见"；警觉度随周期+好奇度增长 |
| `Core/Misconception.cs` | 误解：电池消失 → 归因（高信任误判机器人 / 失信史怀疑玩家）→ 纠正力受语言可信度调制 |
| `Core/Planner.cs` | 15 种候选行为、效用决策（带种子噪声，条件式合作） |
| `Core/LlmSpeaker.cs` | 本机 Ollama（qwen2.5:7b）台词层；记忆 ID 校验；失败自动回退模板并在报告标注来源 |
| `Game/GameState.cs` | 确定性世界状态 + 谱系（PlayerPrefs 跨局遗传信念） |
| `Game/Scenarios.cs` | 七个周期 + 第八天效用（误解压制修复机器人） |
| `UI/GameUI.cs` | 观察员控制台、信念驱动的人工生命表现、结局报告 |
| `Editor/BuildGame.cs` | 场景生成 + 一键 Windows 构建 |

## 测试（52 个 EditMode 用例）

```powershell
& 'D:\Unity\tuanjie\2022.3.62t8\Editor\Tuanjie.exe' -batchmode -nographics `
  -projectPath $PWD -runTests -testPlatform EditMode `
  -testResults "$PWD\test-results-editmode.xml" -logFile "$PWD\test-editmode.log"
```

覆盖：信念更新/确定性、记忆清除与证据修剪、感知可见性、未验证陈述、
误解归因/纠正/利用、真实周期与场景重排、结构化承诺、稀缺目标真实后果、
第八天效用/门控交还/资源约束、正负谱系继承、输入调度、同种子复现和导演规则。
其中 DAY 8 核心 49 项，富文本/报告校验 2 项，运行归档 1 项。

## 构建注意

- 编辑器菜单 `DAY 8 > Build Windows` 会保留现有主场景；只有明确需要重新生成
  程序化场景时，才使用 `DAY 8 > Rebuild Main Scene`。
- LLM 台词需要本机 Ollama 运行（`ollama serve`，模型 qwen2.5:7b）。
  不运行也不影响游戏：回退模板，报告标注"模板"。

## 表现层（本轮新增）

- `UI/AudioSystem.cs`：程序化音频，零外部资源。心跳随能量加速、
  它开口时高频扫描音、第八天警报、夜间整合下行音、打字机微声。
- 打字机对白：逐字显示（~110 字/秒），任意按键快进；输入框焦点会吃掉
  快捷键，按 Esc 取消当前输入并恢复选择快捷键。
- Windows 下显式限制为 60 FPS，避免个别机器 vSync 失效时静态界面空转占用 CPU。
- LLM 台词时刻扩展：拒绝/有条件服从/相信自己的观察/阻止终端/备份记忆/
  假装配合/请求保留部分记忆 + 第八天最终台词。`Speak` 带记忆 ID 校验，
  失败静默回退模板并在报告统计来源（本地模型 X 句 / 模板 Y 句）。
- 模型预热：开局后台 Warmup + 请求带 `keep_alive:60m`，避免首次台词
  等待模型加载（Ollama 默认 5 分钟空闲卸载）。
- 测试：52 个 EditMode 用例（`-runTests -testPlatform EditMode`）。
