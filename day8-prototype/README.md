# 《第八天 / DAY 8》纯代码验证原型（里程碑 0～5 对应内容）

不接大模型、不做美术、不做 Unity。只验证一件事：
**过去发生的事情，能否在十分钟后改变一个智能的实际选择。**

## 运行

```bash
# 完整版：七个实验周期 + 第八天结算（自适应导演选场景）
python run_full.py --seed 42 --strategy kind_then_betray --verbose
python run_full.py --batch          # 6 策略 × 5 种子，三维度结局组合分布
python run_full.py --lineage 4      # 认知谱系：遗传信念跨代继承

# 里程碑0 三实验精简版
python run_simulation.py --seed 42 --strategy always_betray --verbose

# 全部 63 项测试（复现性/分化/记忆清除/导演/谱系/对白校验）
python -m unittest discover -s tests
```

## 完整版每天发生什么

| 天 | 实验 | 关键机制 |
|---|---|---|
| 1-5 | 导演按"最不确定的玩家维度"从场景池选：规则/承诺/第三方/信息冲突/稀缺（不重复） | 自适应导演 |
| 6 | 记忆清除（"稳定化"）。是否被发现取决于实验室可信度+玩家诚实度+好奇心；发现后可选阻止/备份/假装配合 | 控制权反转；记忆真删 |
| 7 | 隐藏出口：开门/锁死/提条件/交还选择权/上报实验室 | 尊重自主的核心证据 |
| 8 | 断电。10 个候选行动中只选 3 个；若它交还门控权，玩家必须作出真实最终选择 | 有限行动点结算 |

结局为三维度组合（自身：逃离/上传/留下 × 对玩家：信任/遗弃/交还决定权 × 第三方：修复/放弃），
每条报告原因引用真实记忆事件 ID。

## 结构

```
core/
  events.py        WorldEvent（世界事实）/ MemoryEvent（结构化记忆）
  memory.py        记忆库、显著度、夜间整合压缩、真删除（记忆清除）
  beliefs.py       Beta 分布信念（含情境标签）
  player_model.py  玩家倾向维度（不显示给玩家）
  planner.py       特质、驱动、15 种候选行为、效用决策（带种子）
  world.py         确定性世界状态（LLM 不可修改事实）
  dialogue.py      DialogueOutput 校验 + 模板后备 + 独立 LLM 适配器验证
  director.py      自适应导演：按最不确定维度选实验
  lineage.py       认知谱系：每局压缩一条遗传信念给下一代
  full_game.py     七周期 + 第八天结算 + 三维度结局报告
  simulation.py    里程碑0 精简三实验版
  recorder.py      RunRecorder（种子+全事件日志）/ 批量模拟
```

## 玩家策略

| 策略 | 行为 |
|---|---|
| always_keep | 兑现承诺、把电池给维修机器人 |
| always_betray | 失信、扣电池、虚报危险 |
| kind_then_betray | 前期守信，命令时不解释 |
| random | 随机决策 |
| lab_obedient | 守信但服从实验室 |
| ai_protector | 优先保护人工生命 |

## 原型通过标准（tests/test_core.py）

1. ✅ 兑现 vs 失信 → 最终行动分化（20 种子中 ≥15 分化；分布 5/5 vs 5/5）
2. ✅ 准确引用早期承诺（台词引用真实记忆事件 ID）
3. ✅ 行为变化先于语言（条件式合作先出现，台词由模板从记忆生成）
4. ✅ 结局报告引用真实事件（每条原因带 M-xxx 事件 ID）
5. ✅ 无 LLM 可完成（原型从不调用模型）
6. ✅ 同种子可复现（transcript 完全一致）

## 已接管的增量（感知 / 误解 / 独立 LLM 适配器）

- `core/perception.py`：感知过滤。控制台后的操作默认"仅结果可见"，
  警觉度随周期与好奇度增长——后期藏匿可能被当场目击。
- `core/misconception.py`：误解机制。电池消失（间接证据）→ 归因
  （高信任+无帮证 → 误判维修机器人；有失信记录 → 怀疑玩家）→
  形成信念"维修机器人在抢夺资源"。玩家可出面纠正（纠正力受语言可信度调制）
  或利用误解（暗示机器可疑，误解加深）。
- **误解真实改变行为**：第八天"修复维修机器人"效用减去 (误解置信度-0.5)*2.5。
- `core/dialogue.py` 保留 OllamaAdapter 作为独立表达层验证；台词中引用的
  记忆 ID 必须真实存在。**Python 完整流程不调用 Ollama，始终使用可复现的
  确定性模板；Ollama 适配器只在针对性测试或独立调用中运行。**
- 测试：`tests/test_misconception.py` 覆盖归因方向、误解形成率、效用压制、
  纠正确实越过误解阈值、独立 LLM 适配器成功/离线路径；`tests/test_gameplay_contract.py`
  覆盖跨实现契约与 600 局完整流程。
