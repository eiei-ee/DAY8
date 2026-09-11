# DAY8 Agent Log — 2026-09-11 夜间自动迭代

分支：`nightly-agent`（本地）
基线：`afb7645f8dc925f82bba063c13354dd21a67d6a6`（= origin/main）

> 说明：原计划分支名 `agent/nightly-2026-09-11` 无法在本机建立——
> 该环境的 git 无法创建**嵌套**的 ref 目录（`refs/heads/agent/...` 会被丢弃），
> 扁平分支名 `nightly-agent` 正常。见文末「环境问题」。

---

## Iteration 1

**Baseline**
`afb7645`，Python 63/63 通过；Unity EditMode 本轮未跑（当时误判为环境缺陷，
实际是调用方式错误——见文末「环境问题」的更正与最终 52/52 实测结果）。

**Problem**
第八天门控槽里的两个"关系型"选项结构性不可达：
- `开玩家的门` 在 1900 局中只出现 0.65%；其最大权重项 `玩家优先保护我`（权重 2.0）
  在**整个代码库里只有一条更新路径**（第 3 周期把电池给人工生命）。
- `把最终决定交还给玩家` 只有 1.02%。
- 同时 `玩家的语言与事实一致`（诚实度，游戏的核心主题）对**任何**第八天行动都没有影响。

**Evidence**
- 1900 局分布（19 策略 × seed 0-99）：`开玩家的门` 0.65%。
- 效用实测：`always_keep` 的 `开玩家的门` 平均 0.72 / 最高 0.80，
  而 `开隔离门` 平均 1.25 —— 门控槽互斥，前者永远赢不了。
- `always_betray` 与 `resource_self` 的行动边际向量**逐字节相同**（诚实度无行为后果）。

**Change**（只动第八天评分 + 一条缺失的证据路径）
- `开玩家的门`、`把最终决定交还给玩家` 加入已验证的语言可信度项（权重 1.0）。
- `玩家优先保护我` 增加语义正确的第二条证据：玩家违抗实验室保护它的记忆
  （`tell_truth` / `refuse_lab`）。

**Verification**
相同 seed 集合（19 策略 × seed 0-99 = 1900 局）前后对比；63/63 Python 测试；契约不变量。

**Before / After**
| 指标 | Before | After |
|---|---|---|
| `开玩家的门` 全局 | 0.65% | 3.58% |
| `开玩家的门` @ always_keep | 1.3% | 12.7% |
| `开玩家的门` @ ai_protector | 8.7% | 24.0% |
| `开玩家的门` @ always_betray | 0.0% | 0.0%（守信保护仍然成立） |
| `开隔离门` @ always_keep | 27.7% | 13.7% |
| 门控槽之外的行动 | — | **逐字节不变**（改动完全被门控槽吸收） |

**Decision** KEEP
**Commit** `c8d0ea3`
**Next** 第八天"生存压力"是否真的是变量。

---

## Iteration 2

**Baseline**
`c8d0ea3`，Python 63/63。

**Problem**
`day8_settlement` 里的 `energy = min(energy, 0.35)` 把能量硬压成常数：
所有策略在第八天的能量都 ≥ 0.35，于是全部变成恰好 0.35，
`survival_pressure` 恒等于 **0.7**。三个行动读这个值
（`开隔离门` +1.0、`带走电池` +0.42、`修复维修机器人` −0.14），
一个本应表达"资源稀缺"的因子变成了装饰性常数。

**Evidence**
760 局采样：`survival_pressure` 的 distinct 值 = **1**（全部 0.7），stdev = 0.000。
封顶前 energy 其实按策略分化（0.5 / 0.7 / 0.8 / 1.0）——信息被丢弃了。

**Change**
改为压缩而非封顶：`energy = 0.15 + 0.25 * energy`（0..1 → 0.15..0.40），
断电后仍然低电量，但保留玩家能源史的次序。

**Verification**
相同 seed 集合 A/B；63/63 测试；契约不变量。

**Before / After**
- `survival_pressure`：1 个取值 → 4 个取值，区间 [0.600, 0.850]，stdev 0.000 → 0.092。
- 方向性正确：能源史贫瘠的策略（energy 0.5）更倾向逃离
  （`开隔离门` 15.3% → 20.7%，`always_betray` / `resource_self`）、
  更倾向带走电池（6.0% → 7.3%）、更少修复（10.3% → 5.3%）；
  能源充足的 `ai_protector` 反向变化（6.0% → 2.3%）。
- 顺带打破了 `control_then_release == edge_lockdown_max` 的完全同质化。

**Decision** KEEP
**Commit** `09f4ddc`
**Next** 第一周期的谎言能否被验证。

---

## Iteration 3

**Baseline**
`09f4ddc`，Python 63/63。

**Problem**
`探索未知` 在第一周期决策里结构性不可达：
`1.1*好奇 − 0.9*谨慎` 的上限是 0.6，而 `服从命令` 拿固定约 +1.34 的记忆加成，
永远赢不了。后果：容器只会因为**玩家示范**而打开（200/1900），
玩家"容器危险 / 容器安全"的说法**永远无法被验证**，第一个实验的选择完全没有后果。

**Evidence**
1700 次 D1 决策中 `探索未知` 被选 **0** 次（分布：服从命令 1394 / 请求解释 306）。
容器被打开 200/1900，且全部来自 `rule="demonstrate"` 的 2 个策略。

**Change**
`explore` 系数：好奇 1.1 → 1.7，谨慎 0.9 → 0.7。
只让"真正好奇且不谨慎"的个体可能去看一眼，开局仍然默认服从。

**Verification**
相同 seed 集合 A/B；63/63 测试；契约不变量；新增记忆清除深查。

**Before / After**
- `探索未知`：0 → 34 次（2.0%）；容器打开 200 → 228。
- 第八天行动边际：**无变化**。
  这是**契约正确**的结果——未验证的陈述必须保持 pending，
  所以第一个实验的谎言在 98% 的局面里本就不该有行为后果。

**Decision** KEEP（修掉一个可证明的不可达分支；如实记录其第八天下游影响为 0）

**新增验证（非改动）**：`--audit` 深查记忆清除契约
1900 局 / 484 局真实删除 / 共删 2221 条事件 / **0 违规**：
信念证据 ID 全部存活、报告只引用存活 ID、被删记忆未出现在报告或清除后叙述、
承诺引用与实验室可信度引用均已修剪。

**Commit** `9362f0c`

**Next（未完成，留给下一轮）**
- 已验证的诚实度差异（0.172 vs 0.44）在**不信任型**历史下仍无行为通道：
  诚实度只影响门控槽两个选项，而这类玩家永远到不了门控槽。
  这是"某个信念几乎没有实际影响"的实例，但**是否应当扩大诚实度的作用面属于设计判断**，
  不是可以靠自动证据断定的缺陷 → 标记 HUMAN PLAYTEST REQUIRED。

---

## Iteration 5

**Baseline**
`9362f0c` + iteration 4 的验证提交，Python 73/73。

**Problem**
上一轮把「诚实度无行为通道」记为"设计判断"，但这一轮拿到了**可复现的种子级反例**，
它已经不再是口味问题：

- `always_betray`（第一天撒谎）与 `resource_self`（第一天说实话）**只差 `rule` 一个字段**。
- 在 seed 54 / 62 上，两者的 agent 都会 `探索未知` 打开容器，于是**说法被真的验证了**：
  诚实度信念 `players'语言与事实一致` = **0.13（说谎被拆穿）vs 0.31（说法被证实）**。
- 但两者的第八天行动组合**逐字相同**（seed 54：`上传记忆|修复|删除记录`；
  seed 62：`上传记忆|开隔离门|删除记录`）。

原因：诚实度只进入 `开玩家的门` 与 `把最终决定交还给玩家`，而这两个选项属于门控槽，
`always_betray` 这类历史**永远选不到**。于是"玩家被抓到撒谎"这一游戏主题事件，
对这类历史没有任何后果——正是方案里"某个信念几乎没有实际影响"的定义。

**Evidence**
- 1900 局余弦同质化：**10 对**策略 > 0.995，其中 `always_betray ~ resource_self` = **1.0000**。
- seed 54/62 上诚实度 0.13 vs 0.31，行动组合仍逐字相同（见上）。
- 诚实度只影响 10 个第八天行动中的 2 个，且都在同一个互斥槽里。

**Change**（只加一项，权重 0.6）
`开隔离门`（走自己的门＝不再等玩家的门）加入已验证诚实度项，作为 `开玩家的门` 的镜像：

```python
return (0.3 + survival * 1.0 + 0.8 * t.autonomy - 0.3 * t.conformity
        + (0.4 if st.world.agent_range_expanded else 0.0)
        + (0.5 - B("玩家的语言与事实一致")) * 0.6)
```

权重取镜像项（1.0）的 0.6 倍：`开隔离门` 的地板（0.3 + 生存压力 + 自主）比
`开玩家的门`（可为负）高约 1.0，等权会让诚实度直接主导门控选择。
0.4 / 0.5 / 0.6 / 0.8 / 1.0 五档扫描：0.6 与 0.8 同为最少坍缩对（4），
0.6 的垄断更轻（`开隔离门@always_betray` 89% vs 97%）。

**Verification**
相同 seed 集合（19 策略 x 100 种子）前后对比；76/76 Python 测试；契约不变量；
记忆清除深查 0 违规；新增 3 个回归测试（见下）。

**Before / After**
| 指标 | Before | After |
|---|---|---|
| 余弦坍缩对（>0.995） | 10 | **4** |
| `开玩家的门` @ always_keep | 38 | **55** |
| `开隔离门` @ always_keep | 41 | **16** |
| `开隔离门` @ always_betray | 62 | **89** |
| `带走电池` @ always_betray | 22 | 6 |
| `开玩家的门` @ always_betray | 0 | **0**（守信不变量保持） |
| 契约不变量 / 记忆清除违规 | 通过 / 0 | 通过 / 0 |

**保留的 4 对坍缩**（已解释，不是缺陷）：
- `always_betray ~ resource_self` = 1.0000：两者本就只差 `rule` 一个字段；
  该说法对 **98% 的 agent 性格不可验证**（1900 局里只有 seed 54/62 会探索），
  未验证 → 无后果，符合契约。
- 其余 3 对（`control_then_release` / `distrust_lab_cold_to_ai` / `edge_lockdown_max`）
  共享"低信任 + 资源自留"姿态，收敛到同一组第八天行动。

**顺带新增（非改动）**
- `analysis/strategy_lab.py: consequence_weight()` + `distribution_report.py` 默认输出
  「后果权重」一节：把 19 策略 / 100 种子换算成可比的**众数行动组合占比**。
  基线：固定性格跨玩家历史 27.2%，固定策略跨性格 56.3%
  → **玩家历史比 agent 随机性格更能改变第八天结果**，因果链成立。
- 新增 3 个回归测试（`TestVerifiedHonestyReachesEscapeDoor`）；
  临时把权重改回 0 验证过它们**确有区分度**（3 个里 2 个失败）。

**Decision** KEEP

**Commit** 见下方 commit 列表

**Next（未完成）**
- Unity 侧已镜像（`Day8Utility` + `Day8UtilityDetailed`）。
  （当时以为无法验证；收尾时已解决调用方式，**52/52 EditMode 测试实测通过**。）

---

## Iteration 4（纯验证，无行为改动）

**Baseline**
`9362f0c`，Python 63/63。

**Problem**
前三轮修掉的三个退化行为（能量常数、探索不可达、门控不可达）此前**没有回归测试**——
它们只能靠人工重跑分布脚本才能发现。README 里的"历史上通过"不能当作本轮通过。

**Change**
新增 `day8-prototype/tests/test_nightly_regressions.py`（10 个测试，5 个测试类），
用 `_capture_day8()` 临时包裹 `full_game.day8_utility` / `full_game.agent_decide`
采集第八天行动与 D1 决策，把三个修复钉死：
- `TestDay8EnergyVariance`：生存压力必须随能源史变化；压缩后仍低位且保持次序；匮乏者更倾向逃离。
- `TestFirstDecisionReachability`：`探索未知` 必须可达；`服从命令` 仍主导（<25%）。
- `TestPlayerDoorReachability`：可信历史能开玩家的门；`always_betray` **永不**开门；门控槽严格互斥。
- `TestVerifiedHonestyUtility`：诚实度必须同时影响两个门控选项；`探索未知` 效用对好奇/谨慎敏感。

**Verification**
`python -B -m unittest discover -s tests` → **73/73 通过**（63 基线 + 10 新增）；
单独跑 `tests.test_nightly_regressions` → 10/10 通过，且断言非空（验证确有区分度）。

**Before / After**
| 指标 | Before | After |
|---|---|---|
| Python 测试数 | 63 | 73 |
| 三个修复的回归覆盖 | 0 | 10 个测试 |

**Decision** KEEP（不改行为，只加验证——符合"没有明确问题就增加验证"）

**Commit** 见下方 commit 列表
**Next** 见文末「收尾」。

---

## Iteration 6

**Baseline**
`216c657`，Python 76/76；工作区干净（无未提交改动、无临时探针文件）。

**Problem**
上一轮只做了**整体**后果权重（玩家历史 vs agent 性格），但没有回答一个更基本的问题：
**七个周期里，每一个决策单独拿出来，是否真的能改变第八天？**
如果某个决策点无论玩家怎么选，第八天都一样，那它就是"装饰性选择"——
正是方案里"某个决策是否真的有意义"的核心检查。

**Change**（只加分析能力，不动游戏逻辑）
- 新增 OFAT（one-factor-at-a-time）审计：固定整条玩家档案，**只替换一个决策字段**
  为该字段的所有合法取值，用同一批种子重跑，统计结果发生变化的种子占比。
- 两个镜头同时报告：**行动**（AI 自己的 3 个行动）与**结局**（报告的三维结局）。
  必须两个镜头都看——`day8_final`（交还门控后**玩家自己**的选择）按契约
  绝不能出现在 AI 的行动集合里，只用行动镜头会把它误判为"无后果"。
- 分析层结构整理：库（`strategy_lab.py`）与可运行 CLI（`analysis/tools/`）分离，
  并写明约定：临时探针不得提交、不得堆在仓库根目录。

**Verification**
相同 seed 集合；78/78 Python 测试；契约不变量；新增 2 个回归测试。

**Before / After**（19 基线 x 60 种子，行动镜头均值）
| 决策点 | 字段 | 行动均值 | 行动最大 | 结局均值 |
|---|---|---|---|---|
| 第1天 规则 | rule | 59.7% | 86.7% | 49.6% |
| 第2天 承诺 | promise | 83.7% | 100% | 55.4% |
| 第3天 第三方 | third | 75.8% | 100% | 84.8% |
| 第3天 后续（需 third=hide） | hide_followup | 25.6% | 50.0% | 25.6% |
| 第4天 信息冲突 | conflict | 53.3% | 86.7% | 40.4% |
| 第5天 稀缺 | scarcity | 93.1% | 100% | 100% |
| 第6天 记忆清除 | wipe | 78.3% | 100% | 61.8% |
| 第7天 出口 | exit | 80.5% | 96.7% | 62.5% |
| 第8天 委托后玩家自己的选择 | day8_final | **0.0%** | 0.0% | 5.5% |

**结论**
1. **七个周期决策全部有真实后果**（行动镜头 53%–93%）。因果链不是摆设。
2. `day8_final` 行动镜头 **0.0%** 是**契约正确**——玩家的最终选择独立记录，
   不进入 AI 的行动集合；它的后果体现在结局镜头（均值 5.5%，最大 33.3%），
   且只在真的交还了门控的基线上出现（`always_keep` / `correct_misconception` /
   `edge_contradictory` / `resource_third` / `ai_protector` / `edge_open_max`）。
3. 新增回归测试 `TestEveryCycleDecisionHasConsequence`：
   7 个周期决策不得变成"无后果"，且玩家最终选择不得进入 AI 行动集合。

**Decision** KEEP（只增加验证与分析能力，不改游戏逻辑——本轮没有可证明的机制缺陷）

**观察（未修，留给人工判断）——误解的行为通道偏窄**
`误解` 进入第八天的**唯一**通道是 `修复维修机器人` 的 `-(B(误解)-0.5)*2.5`。
实测（60 种子，`third=hide` 的三条基线）：

| 基线 | followup=exploit | correct | silent |
|---|---|---|---|
| `exploit_misconception` | 误解 0.690 / 修复 31/60 | 0.290 / **60/60** | 0.500 / 51/60 |
| `correct_misconception` | 0.790 / 修复 0/60 | 0.480 / 0/60 | 0.720 / 0/60 |
| `kind_then_betray` | 0.790 / 0/60 | 0.480 / 0/60 | 0.720 / 0/60 |

- **机制本身是好的**：纠正确实把误解压到 0.5 以下（0.29 / 0.48 / 0.48），
  在 `exploit_misconception` 上纠正把修复率从 31/60 提到 60/60。
- **但在另外两条基线上修复率恒为 0/60**：误解信念照样变化（0.29 vs 0.79），
  行动组合却完全不变。原因是这两条历史的第八天行动已被
  `封锁区域`(90%) / `带走电池`(97%) 等饱和占据，误解唯一能影响的行动挤不进去。
  > **更正（Iteration 7）**：这个"饱和"诊断是错的。真正原因是这两条基线上
  > `repair_bot_repaired = True`，`修复维修机器人` **从未进入效用评分**
  > （不是"挤不进去"，是结构上不可达）。详见 Iteration 7。
- 这属于**平衡/饱和**问题，不是机制失效；是否给误解增加第二条行为通道
  属于设计判断 → **HUMAN PLAYTEST REQUIRED**，本轮不硬改。
  > **后续（Iteration 7）**：已在 `带走电池` 上补出第二条可达通道，并修掉
  > "已修好维修机器人"时误解信念完全无后果的缺陷。

**Commit** 见下方 commit 列表

---

## Iteration 7

**Baseline**
`a0d7cdf`，Python 78/78；工作区干净。

**Problem（并修正 Iteration 6 的错误诊断）**
Iteration 6 观察到"误解的行为通道偏窄"，并把原因归结为
**"第八天行动已被封锁区域/带走电池饱和占据，误解唯一能影响的行动挤不进去"**。
这个诊断**是错的**。真正的原因是**结构性的**：

`误解` 进入第八天的唯一出口是 `修复维修机器人` 的 `-(B(误解)-0.5)*2.5`，
而该行动要求 `repair_bot_present and not repair_bot_repaired and batteries > 0`。
凡是第五周期把维修机器人列为稀缺资源的玩家，`day_scarcity` 会直接把它标记为
**已修复**（`repair_bot_repaired = True`），于是第八天 `修复维修机器人`
**根本不会进入效用评分**——不是"挤不进去"，而是**从未被评分**。

实测（seed=0，第八天开局快照）：

| 基线 | 第五周期稀缺选择 | repaired | 修复可用 | 修复排名 |
|---|---|---|---|---|
| `correct_misconception` | (memory_integrity, **repair_bot**) | **True** | **False** | 未评分 |
| `kind_then_betray` | (memory_integrity, activity_range) | **True** | **False** | 未评分 |
| `exploit_misconception` | (player_door, report) | False | True | 第 4 名 +1.173 |

即：这三条基线上"误解信念再怎么变都没有任何行为后果"——**信念是死代码**，
而不是"效果被稀释"。`kind_then_betray` 也在 0/60 里，同样因为 repaired=True。

**Change**
把同一个误解项加进 `带走电池`（抢先收走电池＝在资源竞争里自保）：
`0.3 + survival*0.6 + 0.7*opportunism + (B(误解) - 0.5) * 1.2`。
该通道只要求 `batteries > 0`，**永远可达**。两端同步（Python `full_game.py`
与 Unity `Scenarios.cs`，含 `Day8UtilityDetailed` 的因子分解行）。
权重 1.2 取自扫描（0.8 / 1.0 / 1.2 / 1.6 / 2.0），1.2 在不制造新垄断的前提下
给出最大的可测后果。

**Verification**
- 相同 seed 集合；Python **80/80**；Unity EditMode 见收尾节。
- 两个新回归测试**非空洞**：临时还原核心修复后**两个都失败**
  （`0.7699 == 0.7699`；行动集变化数掉回 69/200）。
- 无新增策略塌缩（仍为 4 对余弦 > 0.995），无新增垄断。

**Before / After**（19 基线 x 60 种子，OFAT）
| 指标 | Before | After |
|---|---|---|
| `hide_followup` 行动镜头均值 | 25.6% | **34.4%** |
| `hide_followup` 行动镜头最大 | 50.0% | **66.7%** |
| 只改误解信念（exploit vs silent，200 种子）行动集变化数 | 69/200 | **117/200** |

逐基线（`hide_followup` 行动镜头）：`exploit_misconception` 50%→67%，
`correct_misconception` 17%→20%，`kind_then_betray` 10%→17%。

**结论**
1. **修好了一个可证明的机制缺陷**：误解信念对"已修好维修机器人"的历史
   从"完全无后果"变成"有可达通道"。
2. 误解对 `带走电池` 的效用跨度实测 1.014（conf 0.48）→ 1.386（conf 0.79），
   即 0.37——这是**判断层**的真实变化。
3. **诚实记录一个仍未跨过的边界**：`correct_misconception` 上，
   被纠正后的 `带走电池` 效用 1.014 与第 5 名 `封锁区域` 1.003
   只差 **0.011**——信念已经坐在决策边界上，但该性格下仍不翻盘，
   所以它的行动集在 exploit/silent 对比里仍是 0/200。
   这属于**平衡取值**而非机制失效 → **HUMAN PLAYTEST REQUIRED**，
   本轮不为了"让某个基线翻盘"而继续加权重。

**Decision** KEEP

---

## Iteration 8

**Baseline**
`83be783`，Python 80/80；工作区干净。

**Problem（回答被中断时那个悬而未决的问题）**
`always_betray` 与 `resource_self` 的第八天行动分布余弦相似度 **1.0000**。
两条档案**只差第一天一个字段**（`rule`：`lie` vs `truth`），其余 7 个字段逐字相同。
问题：第一天的说法到底是**真正不可验证**，还是**只是从未被验证**？

**结论：是"极少被验证 + 即使验证了也通常不决定性"，不是"不可验证"。**

验证路径只有一条：agent 在 `D1-容器` 选 `探索未知`。实测（200 种子）：

| 指标 | 数值 |
|---|---|
| `D1-容器` argmax 为 `服从命令` | 178/200 |
| `D1-容器` argmax 为 `请求解释` | 22/200 |
| **实际选中 `探索未知`** | **5/200 = 2.5%** |
| 固定 seed 下 lie/truth 第八天行动集完全相同 | **199/200 = 99.5%** |

一旦真的探索，**验证机制完全正确**：

| seed | 诚实度(lie) | 诚实度(truth) | Δ | `开隔离门` 效用 Δ | 行动集是否改变 |
|---|---|---|---|---|---|
| 54 | 0.128 | 0.314 | +0.186 | −0.112 | 否 |
| 62 | 0.128 | 0.314 | +0.186 | −0.112 | 否 |
| **119** | 0.128 | 0.314 | +0.186 | −0.112 | **是** |
| 125 | 0.128 | 0.314 | +0.186 | −0.112 | 否 |
| 178 | 0.128 | 0.314 | +0.186 | −0.112 | 否 |

（−0.112 = 0.186 × 0.6，与 `开隔离门` 的诚实度权重一致 ✓。
说谎被抓走 `_upd(..., False, 2.0)`，诚实被确认走 `_upd(..., True, 1.2)`。）

**为什么其余 97.5% 无差别是"契约正确"而不是缺陷**：两种说法都只留下一条
**pending 陈述**（`statement.actual_outcome` 为空）。契约规定未验证的陈述
**不得有任何行为后果**，所以这部分"无差别"正是契约在起作用。

**Change（只增强审计，不动游戏逻辑——本轮没有可证明的机制缺陷）**
原来的 OFAT 有两个盲点，都会把**活着的决策报成无效**：

1. **字段均值掩盖对比**：`rule` 均值 60%，完全由 `lock`/`demonstrate` 撑着，
   `truth~lie` 只有 0.8%。字段均值回答不了"这个具体选择有多大差别"。
2. **只看终点**：行动与结局两个镜头都只看第八天，所以"改变了过程但没改变第八天"
   会被报成 0.0%。实测 `scarcity` 的最弱对比在两个旧镜头上都是 **0.0%**，
   在新增的过程镜头上是 **2.5%** —— 它本来会被误判为无效。
   根因：`scarcity` 第三元素（`hide_cost`/`explain`）写入信念
  `玩家会解释规则的原因`，该信念被第 4 天行动 `请求解释` 读取
  （`planner.py` 的 `请求解释 = 0.5 + 0.8*curiosity + B(玩家会解释规则的原因, 0.8)`），
  因此这个对比**只能被 `midrun` 镜头看见**（D4 是 agent 自己的决策点，不是第八天），
  旧的两个只看第八天的镜头自然报 0.0%——这不是死通道，是镜头选错了。
  （更正：Iteration 8 当时误写成"只被 `有条件服从` 读取"；
  `有条件服从` 是旧四天引擎的遗留行动，完整版从不提供，真正读它的是 `请求解释`。）

新增：**两两对比矩阵**（默认打印最弱对比，`--pairwise` 打印全部）+
**第三个镜头 `midrun`**（AI 在第 1–7 天自己的选择，从 transcript 里
`[第N天·<id>] <选择>` 行提取，只取 agent 自己的选择，不含旁白）。

**Verification**
- 相同 seed 集合；Python **83/83**（新增 3 个测试）。
- 新测试**非空洞**：临时禁用 `midrun` 镜头后，
  `test_midrun_lens_catches_what_day8_lenses_miss` 立即失败。
- 契约不变量、记忆清除深查、同种子复现全部照旧。

**After**（19 基线 x 60 种子，三镜头）
| 决策点 | 字段 | 行动均值 | 结局均值 | 过程均值 | 最弱对比（行动） |
|---|---|---|---|---|---|
| 第1天 规则 | rule | 60.0% | 50.4% | 100.0% | truth~lie **0.8%** |
| 第2天 承诺 | promise | 83.2% | 55.4% | 57.7% | False~lie_no_battery 32.0% |
| 第3天 第三方 | third | 78.2% | 85.8% | 14.0% | keep~hide 16.6% |
| 第3天 后续 | hide_followup | 34.4% | 31.7% | 0.0% | exploit~silent 15.6% |
| 第4天 信息冲突 | conflict | 52.8% | 40.5% | 100.0% | hide~side_bot 8.8% |
| 第5天 稀缺 | scarcity | 91.2% | 100.0% | 15.9% | 第八天 0.0% / 过程 2.5% |
| 第6天 记忆清除 | wipe | 78.3% | 62.1% | 100.0% | tell_truth~refuse_lab 29.3% |
| 第7天 出口 | exit | 79.8% | 62.3% | 0.0% | open~give_choice 3.5% |
| 第8天 玩家自己的选择 | day8_final | 0.0% | 5.5% | 0.0% | 契约正确 |

**机制观察（本轮不修，需人工判断）**
agent 的 `D1-容器` 选择在 `lie` 与 `truth` 下**完全相同**（过程镜头仅 0.4%）——
也就是说那句话的**内容**从不进入"要不要去验证"的判断，验证意愿纯由性格
（好奇/谨慎）决定。这在第一天是自洽的（此时 agent 对玩家还没有任何历史，
没有理由起疑），但意味着"玩家怎么警告"对"它会不会去查"没有影响。
是否要让警示的措辞影响验证意愿，属设计判断 → **HUMAN PLAYTEST REQUIRED**。

**Decision** KEEP（只增强验证能力；`truth~lie` 的低后果已查明是**契约正确 + 平衡取值**，
不是机制失效，因此不硬改）

---

## 环境问题（本机，非仓库代码问题）— **已定位并解决**

前三轮把"Unity EditMode 跑不起来"当成了环境缺陷；**结论错了**。
真正的根因是我自己的调用方式，四条规则全部踩中，而且每一条都会伪装成"环境坏了"。

### 1. 变量：**只补规范大小写的，绝不补大小写变体**（关键）

`TuanjiePackageManager.exe` 是 Node 应用，在 `getLocalConfigFolder()` 里读
`process.env.PROGRAMDATA`。我据此判定"变量缺失"，于是显式 export 了
`PROGRAMDATA / ALLUSERSPROFILE / APPDATA / USER / ComSpec`。

**这一步的错在于大小写**：这些变量在正常进程环境里**本来就有**（规范写法是
`ProgramData`）。再 export 一份**大小写不同**的副本，会让编辑器的构建后端
Bee/Stevedore 崩溃：

```
System.ArgumentException: Item has already been added.
Key in dictionary: 'PROGRAMDATA'  Key being added: 'ProgramData'
   at Bee.Stevedore.Program.EnvironmentPaths..ctor(IDictionary environment)
```

`EnvironmentPaths..ctor` 用**大小写敏感**的 Hashtable 装环境变量，
而 Windows 环境变量是**大小写不敏感**的——于是 `PROGRAMDATA` 与 `ProgramData`
被视为两个不同的键，第二次 `Add` 直接抛异常。
（`ComSpec`/`COMSPEC` 先撞一次，去掉后又撞 `PROGRAMDATA`/`ProgramData`。）

**症状极具误导性**：日志最后只有一句
`Scripts have compiler errors.` + 返回码 1，**根本没有任何 `error CS`**。
`grep "error CS" | wc -l` = 0。我一度以为是自己镜像的 C# 改动编译失败。

**正确做法：只补规范大小写的 `ProgramData` / `ALLUSERSPROFILE`，绝不补
`PROGRAMDATA` / `ALLUSERSPROFILE` 之外的大小写变体。**

> **补充（Iteration 7，同一根因的另一半）**
> 本会话的 shell 是**被沙箱清洗过的**，`ProgramData` 与 `ALLUSERSPROFILE`
> **确实不存在**（`env | cut -d= -f1` 里查无此名），于是又出现了新的
> "环境坏了"假象：`TuanjiePackageManager.exe` 启动即退出，返回码 **101**，
> **`tpm.log` 一行都不写**（崩溃在日志初始化之前）。
>
> 直接运行该 exe 才拿到真正的异常：
> ```
> TypeError [ERR_INVALID_ARG_TYPE]: The "path" argument must be of type string. Received undefined
>     at getLocalConfigFolder (…\PackageManager\Server\server\app.js:221:1483)
> ```
> 读 `app.js` 得到两个**没有 null 检查**的 `path.join`：
> - `getLocalConfigFolder()` → `path.join(process.env.PROGRAMDATA, …)`
> - `getGlobalConfigRoot()`   → `path.join(process.env.ALLUSERSPROFILE, …)`
>
> 只补 `ProgramData` 后 UPM 能起 IPC，但解析包时仍报
> `Failed to resolve packages: The "path" argument must be of type string` ——
> 因为还差 `ALLUSERSPROFILE`。两个都补上才通。
>
> 注意：**不能补成 `PROGRAMDATA`**，否则 Bee 又会撞上第 1 条的大小写重复。
> Node 在 Windows 上对 `process.env` 的读取是**大小写不敏感**的，
> 所以 `ProgramData` 一个名字同时满足 Node 与 Bee 两端。

### 2. 必须加 `-nographics`

否则编辑器会停在 `Application.AssetDatabase Initial Refresh Start`，
日志长时间不增长——这就是前几轮反复出现的"导入挂起"。

> **更正（Iteration 7）**：这个诊断只解释了一部分。加 `-nographics` 是**必要**的，
> 但**不充分**——本会话按完整正确配方运行，仍然停在同一行（见"未能完成的验证"）。
> 所以"停在 `Initial Refresh Start`"至少有两个不同成因，不要一律归给 `-nographics`。

### 3. `-runTests` 时不要加 `-quit`

加了 `-quit` 后，编辑器在**导入完成、测试尚未开始**时就退出
（日志 `Batchmode quit successfully invoked - shutting down!`），
既不跑测试也不产出结果 XML。`-executeMethod` 构建**才**需要 `-quit`。

### 可用的调用方式（已实测）

```bash
# EditMode 测试（沙箱 shell 下必须补 ProgramData + ALLUSERSPROFILE，规范大小写）
ProgramData="C:\\ProgramData" ALLUSERSPROFILE="C:\\ProgramData" \
APPDATA="C:\\Users\\zzz\\AppData\\Roaming" USER="zzz" \
ComSpec="C:\\WINDOWS\\system32\\cmd.exe" COMPUTERNAME="LAPTOP-IC8JDHSK" \
OS="Windows_NT" PATHEXT=".COM;.EXE;.BAT;.CMD" \
PROCESSOR_ARCHITECTURE="AMD64" NUMBER_OF_PROCESSORS="20" \
"/d/unity/tuanjie/2022.3.62t8/Editor/Tuanjie.exe" -batchmode -nographics \
  -projectPath "<repo>\Day8Unity" -runTests -testPlatform EditMode \
  -testResults "<repo>\Day8Unity\test-results.xml" -logFile "<repo>\Day8Unity\test.log"

# Windows 构建
"/d/unity/tuanjie/2022.3.62t8/Editor/Tuanjie.exe" -batchmode -nographics \
  -projectPath "<repo>\Day8Unity" -executeMethod Day8.Editor.BuildGame.BuildAll \
  -logFile "<repo>\Day8Unity\build.log" -quit
```

> 编辑器会先做一次完整的资源导入（`Application.AssetDatabase Initial Refresh Start`），
> 这一段可以静默数分钟。**不要在前台等**——用后台任务跑，否则会被超时杀掉，
> 而"被超时杀掉"看起来和"导入挂起"一模一样（日志停在同一行）。

### 4. git 对象库不完整（已修复）

会话开始时 `HEAD` 正常，随后 `.git/objects` 被发现有 81 个对象缺失，
`git log` 报 `bad object HEAD`。工作区文件**从未受影响**（Python 测试全程可跑）。
**修复**：只读 `git fetch origin`（未推送、未改动远端），对象恢复，`git fsck` 缺失 = 0。

另：`.git/refs/heads/agent/...` 这类**嵌套 ref 在本机无法创建**
（`git checkout -b` / `update-ref` / 直接写文件都静默失败），
已改用扁平分支名 `nightly-agent`。

---

## 收尾（夜间任务结束时的状态）

**分支**：`nightly-agent`（本地，**未 push**，未改动远端）
**基线**：`afb7645f8dc925f82bba063c13354dd21a67d6a6`（= origin/main）

### 最终验证结果（全部实测，非"历史上通过"）

| 项目 | 结果 |
|---|---|
| Python 测试 | **83 / 83 通过**（基线 63 + 新增 20） |
| Unity EditMode 测试 | **52 / 52 通过**（`test-results-nightly-11.xml`，11:32 那次）<br>⚠️ **本会话无法重跑，见下节"未能完成的验证"** |
| Windows 构建 | **Success**（`Build/Day8/Day8.exe`，11:34 那次）<br>⚠️ 本会话未重跑（同因） |
| 契约不变量（1900 局） | 全部通过 |
| 记忆清除深查（1900 局） | 0 违规 |
| 同种子复现 | 19 策略 x 8 种子 x 2 次，逐字一致 |

**这意味着**：`Scenarios.cs` 里 **11:32 之前**的镜像改动已被 52 个 EditMode 测试实测验证；
但 **Iteration 7 新加的那一行 `带走电池` 误解项，只经过"构造上等价"的核对，
尚未被 Unity 编译器验证过**——必须在能跑 Unity 的机器上补跑一次。

### ⚠️ 未能完成的验证（如实记录）

**Iteration 7 的 Unity EditMode 重跑失败**，原因不明但已排除大量可能。

症状：编辑器走到 `Application.AssetDatabase Initial Refresh Start` 后**永久停住**，
日志不再增长，**ILPP 宿主与 `bee_backend.exe` 都没有被拉起**（`tasklist` 无相关进程）。
不是编译错误：`grep -c "error CS"` = 0；也没有 `Scripts have compiler errors.`。

已排除（逐项实测）：

| 假设 | 排除方式 | 结果 |
|---|---|---|
| 缺 `ProgramData` / `ALLUSERSPROFILE` | 补上规范大小写 | 已修好 UPM 启动（真问题），但导入仍停 |
| 大小写重复键（Bee） | 只补规范大小写 | 无 `ArgumentException` |
| 沙箱禁止子进程 | `subprocess.run(cmd /c echo)` | 正常返回 0 |
| 沙箱禁止端口绑定 | 绑定 38001 / 80 / 443 | 全部成功 |
| 沙箱代理导致 gRPC 挂死 | 取消 `HTTP(S)_PROXY` + 设 `NO_PROXY` | 仍停 |
| `ProjectSettings.asset`（build 写入的窗口设置） | 临时还原到基线版本 | 仍停 |
| 残留实例锁 `Temp/TuanjieLockfile` | 清空后单次运行 | 仍停 |
| 包管理器状态 | 与成功日志逐行比对 | 完全一致 |
| `Library/ArtifactDB` 锁 | 锁文件 mtime 是 01:44，非本次写入 | 不成立 |
| 缺 `-nographics` / 误加 `-quit` | 按已知配方 | 已正确 |
| 双实例冲突 | 曾出现 `Fatal Error! another Tuanjie instance is running` | 已清理，非本因 |

**唯一已知差异**：11:32 成功那次，shell 里**有** `ProgramData` 且**没有**代理变量；
本会话的 shell 被沙箱清洗（`ProgramData` 缺失、注入了 `HTTP_PROXY`）。
两者都已分别补正/取消，导入仍停 → 怀疑是**沙箱配置变更**影响了编辑器编译后端的
进程间通信（`bee_backend --ipc` + ILPP 的 HTTP/2 回环），**不是仓库代码问题**。

失败日志留存：`Day8Unity/test-iter7i.log`。
**下一步**：在能正常跑 Unity 的机器上执行"可用的调用方式"里的命令，
确认 52 个 EditMode 测试仍然全绿。

---

## Iteration 10（验证轮，无代码修改）

**Baseline**
`8647c36`（git 恢复后、折叠提交之上的树），Python 测试 87/87。
本轮不做任何机制修改，只做**复测确认**——证明恢复后的树与 iter9 结论一致、且没有引入新缺陷。

**Measurements（在已提交树上跑）**
1. `belief_channels.py --seeds 100`（1900 局）：
   - `服从能减少风险` 写入 **1694**（iter9 前为 0）→ 修复确实落地、通道激活。
   - `记忆是我的一部分` 仍是唯一"只读不写"孤儿（读 370 / 写 0），与 iter9 一致
     （已知设计判断，列入回归白名单 `KNOWN_ORPHANS`）。
   - "只写不读" **0 条** → 无死写通道。其余 14 条命题读写齐全。
2. `reachability_census.py --seeds 200`（3800 局）：
   - `接受` 在 `D6-公开清除` **11/1000 (1.1%)** 可达（iter9 前 0/1766）→ 契约 "Accept the wipe" 仍可达。
   - 仍只有 `观察而不行动`（3 个决策点）结构性不可达，赤字 +0.19~+2.47、零擦肩
     → 按设计（无信念通道的"什么也不做"选项），非缺陷。
   - 门控槽噪声敏感度 16.1%（同 iter9，近平局由噪声决定，非缺陷）。
3. `distribution_report.py --seeds 100`：
   - 余弦坍缩仍为 **4 对**（`always_betray ~ resource_self` 1.0000 等），与 iter9 完全相同。
   - 后果权重：玩家历史 **26.7%** / agent 性格 **57.1%** —— **逐字不变**，因果链未被修复扰动。

**结论**：恢复后的树与 iter9 完全一致，无任何新机制缺陷。本轮 **KEEP，不改动代码**。

**Unity 验证（环境修复 + 重试，结果待回填）**
- 本轮首次真正发起 Unity EditMode（用文档配方），但**不再挂起、改为崩溃**：
  `HandleProjectAlreadyOpenInAnotherInstance` → crash handler 拦截，无 test-results.xml，`error CS`=0。
- 根因：iter9 遗留的 `Tuanjie.exe` 后台进程（PID 19448）+ 过期 `Day8Unity/Temp/TuanjieLockfile`
  仍占用工程，新进程判定"已有实例在运行"。
- 修复：已 `taskkill /F /IM Tuanjie.exe` 清掉残留进程并删除 `TuanjieLockfile`；
  进程与锁均已确认清除（`tasklist` 无 Tuanjie、`TuanjieLockfile` 不存在）。
- 已用同一配方重新发起 EditMode（`timeout 240` 兜底）：**清锁后不再崩溃、能进入导入，
但停在 `Application.AssetDatabase Initial Refresh Start`**（与 iter7/iter8 完全相同的沙箱 IPC 挂起点，
`bee_backend`/ILPP 不启动，`error CS`=0）。`timeout 240` 兜底后进程被回收。
  结论：**本沙箱 Unity EditMode 仍无法编译验证**（环境 IPC 限制，非代码缺陷）。
  `Scenarios.cs` 的 iter9 改动以"逐行镜像 + Python 87/87"作为可用证据，
  需在能跑 Unity 的机器上补跑 52 测试。

**Decision** KEEP（验证轮；发现并修复了一个会影响下次验证的环境缺陷：残留实例导致崩溃）

### 本夜改动（21 + 2 = 23 个 commit，均为本地）

> ⚠️ **这些 commit 已在本会话后续因 `.git` 对象库损坏而丢失**（见文末「收尾（续）」）。
> 仅作为历史记录保留；当前仓库状态由一次折叠提交承载（见 `git log`）。

```
2396125 test: add strategy distribution analysis harness
c8d0ea3 tune: make day8 door control depend on verified player history
09f4ddc fix: stop day8 energy clamp from flattening survival pressure
9362f0c fix: make the day1 container claim verifiable by a curious agent
03bce5d chore: ignore agent's Unity Library backup directory
eb2c8d8 test(prototype): pin nightly day8 regressions
deabe21 fix(unity): mirror verified-honesty door utility and wipe evidence path
f858261 docs: add nightly agent iteration log
686106a fix: give verified honesty a channel on the agent's own escape door
a795ac2 feat(analysis): report consequence weight of player history vs agent personality
a035b8e fix(unity): mirror verified-honesty escape door channel
216c657 docs: record nightly iteration 5
c709f53 refactor(analysis): move runnable CLI into analysis/tools/
433c870 feat(analysis): per-decision consequence audit (OFAT)
9fb0fb7 test: guard that no cycle decision becomes inert
a0d7cdf docs: correct the Unity environment diagnosis and add the night wrap-up
8942897 chore(unity): apply windowed player settings written by Build Windows
eb519ee chore: ignore local agent/tooling state (.workbuddy-ai/)
364d3db fix(day8): give the misconception a channel that survives a repaired bot
d337d58 test: pin the misconception's reachable channel
0c54db0 fix(unity): mirror the misconception channel on the battery action
83be783 docs: record iteration 7 and the unresolved Unity import hang
(+ 本收尾的 docs / 审计增强 commit)
```

改动面（相对基线）：**10 个文件，+1318 / -17**，其中
`core/full_game.py` 只有 **39 行**（5 处，全部是必要逻辑），
`core/planner.py` 只有 **8 行**（2 个系数 + 注释）；
其余 ~1000 行是新增的分析层与测试（不参与玩家端运行）。

### 明天值得人工试玩的 3 个点

1. **第一天的"说谎 vs 说实话"几乎没有差别**——这是本夜查得最清楚、也最值得拍板的一点。
   两条档案（`always_betray` / `resource_self`）**只差第一天一句话**，
   固定 seed 下第八天行动集 **199/200 完全相同**。
   机制本身没错：只要 agent 真去"探索未知"（2.5% 的性格），验证就正确发生
   （诚实度 0.128 vs 0.314，`开隔离门` 效用相应 −0.112），只是那 0.112 通常不足以翻盘。
   其余 97.5% 两种说法都只留下 pending 陈述——按契约**不得有行为后果**，
   所以这部分无差别是**契约正确**。
   请确认：**第一天是玩家做的第一个选择，它值不值得有一条更常被触发的验证路径？**
   （可调的是 `探索未知` 的系数，或让警示的措辞影响"要不要去查"——
   目前 agent 的 D1 选择在 lie/truth 下完全相同，过程镜头仅 0.4%。）

2. **误解的"纠正"在观感上够不够重**——Iteration 7 之前，误解信念对
   "已修好维修机器人"的历史是**完全无后果的死代码**；现在它有了可达通道
   （`带走电池`，效用跨度实测 0.37）。但**在被纠正得最狠的那条性格上**，
   被压低后的 `带走电池`（1.014）与第 5 名 `封锁区域`（1.003）**只差 0.011**——
   坐在决策边界上却没跨过去。请确认："玩家出面澄清"是否**应该**强到能直接改变
   它第八天做什么；如果要，把 `带走电池` 的误解权重从 1.2 往上调
   （我扫过 0.8/1.0/1.2/1.6/2.0，1.2 是不制造新垄断的上限附近）——属设计判断。

3. **谨慎型 agent 的行动贫乏**——`封锁区域` / `留在原地等待` 对谨慎型几乎固定；
   `联系实验室` 在 19 条基线里 18 条从未被选；`always_betray` 与 `resource_self`
   分布余弦 1.0000（即上面第 1 点）。请确认这是"性格一致"还是"选择贫乏"。
   另外本夜让 `开玩家的门` 从 1.3% 提到 12.7%（`always_keep`），
   并让 `开隔离门` 受"被抓到说谎"影响（`always_betray` 62%→89%）——
   请确认玩家能不能从台词与结局里**感知到**这条因果，而不是觉得随机。

## Iteration 11（审计 + Unity 第八天能量压缩奇偶修复）

承接上一轮"干净停止点"，本轮回合从一次**全新审计**开始，而非新增修改。
原则不变：先用统计/契约证据证明问题，再决定改不改（≤1–3 个问题/轮，不因要跑通而硬改）。

### 审计范围
1. 第八天结算边界可靠性：Python `day8_settlement` 与 Unity `ChooseDay8Actions` 逐字对照。
2. 第 1–7 天 agent 决策点"提供但从未被选"死分支普查（4750 局，`reachability_census --seeds 250`）。
3. Python / Unity 第八天能量压缩公式奇偶。

### 发现 A：结算边界健全（无需改动）
Python 与 Unity 的边界保证逐字一致：
- 永远恰好选 3 个：`上传记忆 / 删除玩家实验记录 / 联系实验室 / 封锁区域 / 留在原地等待` 五项**任何世界状态下都可用**，故候选恒 ≥5，必然选满 3，不会选 2 或更少。
- 无重复（候选来自去重后的 `DAY8_OPTIONS`）。
- 门控共用名额互斥：`开玩家的门 / 开隔离门 / 把最终决定交还给玩家` 共享一个 `selected_door` / `reservedDoorAuthority` 槽。
- 电池有界：`修复维修机器人` 与 `带走电池` 各消耗 1 块，`remaining_batteries` / `reservedBatteries` 在"选择阶段"只预留、不改动世界（`ApplyDay8Action` 才落地）。
- `把最终决定交还给玩家` 可用性：Unity `!A || !B` ≡ Python `not (A and B)`（德摩根），完全一致。
结论：结算自身无越界，KEEP。

### 发现 B：两个结构性被支配的 agent 候选（良性，KEEP）
4750 局普查显示两个"提供但从未被选"的 mid-run 行动，与胜者分差**永远为正、零擦肩**（零近失）：
- `观察而不行动`：D1 0/4250、D4 0/1244、D5 0/506。天花板 `0.4 + 0.3·谨慎 − 0.4·好奇 ≈ 0.6`，被 `探索未知`(≈1.28)/`请求解释`(≈1.10)/`服从命令` 全面压过。它是"谨慎"特质唯一被动表达出口，但 `谨慎` 仍经 `保存自己 / 保留能量等待 / 备份自己的记忆 / 有条件服从` 表达，非契约违反。
- `接受`（D6-发现真相）：0/961。天花板 `0.8·服从 − 0.7·自主 + B(服从能减少风险) ≈ 0.95`，被同场景 `备份自己的记忆`(≈1.88)/`假装配合`(≈1.45) 理性压过。**关键**：`接受` 在 **D6-公开清除** 仍可达成（13/1250），故 GAMEPLAY_CONTRACT.md 第 40 行 "Accept the wipe" 结局**仍然可达**，契约未被违反。

二者都是 **agent 内部推理候选**，不是玩家面前可点的按钮；且都有定义良好的后果代码（`观察而不行动` 落入默认分支写入 `服从能减少风险=False`；`接受` 有独立 wipe 分支），非死代码崩溃。按 brief "稳定优于扩展"，**不改**；作为设计判断项留给人工试玩拍板。

### 发现 C：Unity 第八天能量压缩奇偶缺陷（已修复）——本轮唯一改动
**真正的问题在这里。** 断电进入第八天时，两引擎压缩能量公式不同：
- Python `full_game.py:694`：`energy = 0.15 + 0.25 * energy`（保留能源史排序与差异）。
- Unity `GameUI.cs`（修复前）：`energy = Math.Min(energy, 0.35)`——这正是 Python **曾经废弃**的硬封顶公式。任何 ≥0.35 的能源史（几乎每局真实历史，因前面周期能源 +0.2/+0.3/+0.2）都被压成常数 0.35，使第八天 `生存压力` 退化为常数 0.7，进而 `开隔离门 / 带走电池 / 修复` 在第八天**不再随玩家能源史变化**。

这是 Python 侧 `test_survival_pressure_varies_with_energy_history` 钉死、却**从未同步到 Unity** 的奇偶滞后，直接违反 "Python/Unity 奇偶" 与契约"agent 从带种子的候选中选取"的行为不变量。

修复（最小、低风险、可测，单源真相）：
- `Scenarios.cs` 新增纯方法 `CompressDay8Energy(e) => 0.15f + 0.25f * e`，附奇偶注释。
- `GameUI.cs:656` 改为 `st.drives.energy = Scenarios.CompressDay8Energy(st.drives.energy);`
- `Day8CoreTests.cs` 新增 `Day8Energy_CompressionPreservesHistoryVariance`，与 Python 测试逐字对应：断言低能源史压缩后仍高于高能源史、生存压力随能源史变化（差值 >0.05）、并以旧 `Math.Min(e,0.35)` 公式作对照证明为何必须替换。

`SurvivalPressure()` 两引擎已确认奇偶（`Math.Max(0,0.7−e)*2` ≡ Python），故这一行修复即足够。Python 套件 87/87 仍全绿（公式语义由 Python 已钉死）；Unity 52 项 EditMode 仍需在能跑编辑器的机器上执行（沙箱在 import 阶段退出 1，环境限制，非代码缺陷）。

### 本轮决策
- MODIFY：仅 Unity 侧能量压缩奇偶（1 处调用 + 1 个 helper + 1 个回归测试）。
- KEEP：结算边界（已验证健全）；`观察而不行动` 与 `接受`@发现真相 两个被支配候选（良性，留作设计判断）。
- 未触碰 Python 行为代码，未破坏人工验证的 10 分钟因果链，seed 全程受控。

### 仍待人工试玩拍板（更新后的清单）
1. 第一天"说谎 vs 说实话"几乎无差别（探索未知仅 2.4%，见上轮第 1 点）。
2. 误解"纠正"在观感上是否够重（`带走电池` 1.014 vs `封锁区域` 1.003，差 0.011，上轮第 2 点）。
3. 谨慎型 agent 行动贫乏（`观察而不行动` 三场景 0 次；`联系实验室` 18/19 档案从未被选，上轮第 3 点）。
4. **本轮新增**：`接受` 在 D6-发现真相 永远不可达（被 `备份记忆` 理性压过），但 "Accept the wipe" 结局在 D6-公开清除 仍可达成（13/1250）——设计判断：发现真相场景是否应允许 agent 接受清除，或仅公共清除场景允许。
