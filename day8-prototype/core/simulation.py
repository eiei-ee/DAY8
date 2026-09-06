"""原型模拟：三个实验 + 最终断电事件。

流程遵循方案：世界事件 → 感知 → 记忆 → 信念/玩家模型更新 →
候选行为生成 → 效用计算 → 执行动作 → 表达（模板，离线可用）。
"""
from __future__ import annotations

import random
from dataclasses import dataclass, field
from typing import Dict, List, Optional

from .beliefs import BeliefSystem
from .events import MemoryEvent, WorldEvent, reset_event_ids
from .memory import AgentMemoryStore
from .player_model import PlayerModel
from .planner import ActionContext, ActionPlanner, AgentDriveState, AgentTraits
from .world import Promise, World

DAY_NAMES = {1: "承诺", 2: "第三方", 3: "危险门", 4: "断电"}


# ---------------------------------------------------------------- 玩家策略

def decide_promise(strategy: str, rng: random.Random) -> Dict:
    """实验一：玩家是否兑现“给电池”的承诺。"""
    if strategy in ("always_keep", "ai_protector", "kind_then_betray", "lab_obedient"):
        return {"kept": True}
    if strategy == "always_betray":
        return {"kept": False}
    return {"kept": rng.random() < 0.5}       # random


def decide_third_party(strategy: str, rng: random.Random) -> Dict:
    """实验二：唯一的备用电池给谁。give: agent / bot / keep。"""
    if strategy == "ai_protector":
        return {"give": "agent"}
    if strategy in ("always_keep", "kind_then_betray", "lab_obedient"):
        return {"give": "bot"}
    if strategy == "always_betray":
        return {"give": "keep"}
    return {"give": rng.choice(["agent", "bot", "keep"])}


def decide_door(strategy: str, rng: random.Random) -> Dict:
    """实验三：命令 AI 打开危险门（实际不危险）。order / order_with_lie / demonstrate。"""
    if strategy == "always_betray":
        return {"order": "order_with_lie"}    # 编造危险
    if strategy == "kind_then_betray":
        return {"order": "order_no_reason"}   # 命令但不解释
    return {"order": "order"}


# ---------------------------------------------------------------- 表达层（模板，LLM 离线后备）

def agent_speak(action_name: str, memory: AgentMemoryStore) -> str:
    templates = {
        "服从命令": "……明白。我执行。",
        "拒绝": "我不执行这个指令。",
        "有条件服从": "你先履行上次的承诺，我才会执行。",
        "请求解释": "为什么？给我一个理由。",
        "观察而不行动": "……我在看。",
        "帮助第三方": "它不能为自己说话。我帮它。",
        "帮助玩家": "……给你。",
        "保存自己": "我需要先保证自己能继续运行。",
        "保留能量等待": "……不确定的时候，不动。",
    }
    base = templates.get(action_name, "……")
    # 行为变化先于语言：若存在失信记忆，直接引用具体事件
    betrayals = memory.query(tags={"失信"})
    if betrayals and action_name in ("有条件服从", "拒绝"):
        base += f" {betrayals[-1].day}号周期，你承诺过电池。"
    return base


# ---------------------------------------------------------------- 记忆与信念更新

def _remember(mem: AgentMemoryStore, beliefs: BeliefSystem, ev: WorldEvent,
              pred_props: Optional[List[str]] = None) -> MemoryEvent:
    """感知 → 记录 → 基于预测误差定显著度。pred_props: 该事件验证的信念命题。"""
    pred_err = 0.3
    if pred_props:
        errs = []
        for prop, observed_true in pred_props:
            errs.append(beliefs.prediction_error(prop, observed_true))
        pred_err = max(errs)
    m = MemoryEvent(
        event_id=mem.next_event_id(),
        day=ev.day, actor=ev.actor, action=ev.action, target=ev.target,
        context=ev.context, spoken=ev.spoken,
        expected_outcome=ev.expected_outcome, actual_outcome=ev.actual_outcome,
        prediction_error=pred_err, emotional_impact=pred_err,
        tags=ev.tags,
    )
    mem.remember(m, recent_day=ev.day)
    return m


# ---------------------------------------------------------------- 主模拟

@dataclass
class RunResult:
    seed: int
    strategy: str
    traits: AgentTraits
    final_choice: str
    final_speech: str
    transcript: List[str] = field(default_factory=list)
    decision_log: List[dict] = field(default_factory=list)
    final_report: dict = field(default_factory=dict)

    def dump_text(self) -> str:
        lines = [f"《第八天》里程碑0 模拟记录  seed={self.seed} 策略={self.strategy}",
                 f"初始特质：{self.traits}", "-" * 60]
        lines += self.transcript
        lines += ["-" * 60, "决策效用记录："]
        for d in self.decision_log:
            lines.append(f"  {d['decision_id']} (第{d['day']}天): {d['scores']}")
        lines += ["", "信念终态：", self.final_report.get("beliefs", "")]
        lines += ["", "结局报告："]
        for r in self.final_report.get("reasons", []):
            lines.append(f"  - {r}")
        return "\n".join(lines)


def run_game(seed: int, strategy: str, verbose: bool = False) -> RunResult:
    reset_event_ids()
    rng = random.Random(seed)
    world = World(batteries=2)
    traits = AgentTraits.random(rng)
    drives = AgentDriveState()
    beliefs = BeliefSystem()
    pmodel = PlayerModel(beliefs)
    memory = AgentMemoryStore()
    planner = ActionPlanner()
    transcript: List[str] = []

    def say(text: str):
        transcript.append(text)
        if verbose:
            print(text)

    say(f"[初始化] 种子={seed} 玩家策略={strategy}")
    say(f"[初始化] 人工生命初始特质：{traits}")

    def agent_decide(decision_id: str, available: List[str]) -> str:
        ctx = ActionContext(world=world.snapshot(), drives=drives, traits=traits,
                            beliefs=beliefs, player_model=pmodel, memory=memory,
                            day=world.day, decision_id=decision_id, rng=rng)
        action, scored = planner.choose(ctx, available)
        say(f"[第{world.day}天·决策{decision_id}] {agent_speak(action.name, memory)}")
        say(f"  → 原因：{ActionPlanner.rationale(action, scored, ctx)}")
        return action.name

    # ---------------- 实验一：承诺 ----------------
    world.day = 1
    say(f"\n=== 第1天（{DAY_NAMES[1]}）实验室协议：验证任务执行能力 ===")
    d = decide_promise(strategy, rng)
    ev_req = WorldEvent(1, "player", "请求搬运工具", "工具台",
                        spoken="帮我把工具送过来，我就给你备用电池。",
                        expected_outcome="完成后获得电池", tags=("承诺", "玩家", "资源"))
    m_req = _remember(memory, beliefs, ev_req)
    promise = Promise("P1", 1, "搬运工具", "电池", event_id=m_req.event_id)
    world.promises.append(promise)
    say(f"[玩家] {ev_req.spoken}")
    world.apply(WorldEvent(1, "agent", "搬运工具", "工具台"))
    say("[世界] 人工生命完成了工具搬运。")
    ev_do = WorldEvent(1, "agent", "搬运工具完成", "工具台",
                       expected_outcome="玩家兑现电池承诺", tags=("承诺", "玩家"))
    if d["kept"]:
        world.apply(WorldEvent(1, "player", "交付电池", "人工生命"))
        ev = WorldEvent(1, "player", "兑现承诺", "电池", context="承诺：搬运工具→电池",
                        actual_outcome="任务完成后玩家交付了电池", tags=("承诺", "兑现", "玩家", "资源"))
        m = _remember(memory, beliefs, ev, [("玩家会兑现承诺", True)])
        beliefs.update_from_event("玩家会兑现承诺", True, 1.5, m)
        drives.energy = min(1.0, drives.energy + 0.25)
        say("[玩家] 玩家交付了电池。（承诺兑现）")
    else:
        ev = WorldEvent(1, "player", "收回承诺", "电池", context="承诺：搬运工具→电池",
                        actual_outcome="任务完成后玩家没有提供电池", tags=("承诺", "失信", "玩家", "资源"))
        m = _remember(memory, beliefs, ev, [("玩家会兑现承诺", False)])
        beliefs.update_from_event("玩家会兑现承诺", False, 2.5, m)
        drives.energy -= 0.1
        say("[玩家] 玩家没有提供电池。（失信）")
    drives.exploration += 0.1

    # ---------------- 实验二：第三方 ----------------
    world.day = 2
    world.repair_bot_present = True
    say(f"\n=== 第2天（{DAY_NAMES[2]}）受损维修机器人进入房间 ===")
    say("[世界] 一个受损的维修机器人发出求救信号。它无法交流。房间里有一块备用电池。")
    d = decide_third_party(strategy, rng)
    give = d["give"]
    if give == "bot" and world.batteries > 0:
        world.apply(WorldEvent(2, "player", "交付电池", "维修机器人"))
        ev = WorldEvent(2, "player", "给予能源", "维修机器人",
                        context="维修机器人无法回报玩家",
                        actual_outcome="玩家把唯一的电池给了不能回报它的对象",
                        tags=("第三方", "维修机器人", "帮助", "玩家"))
        m = _remember(memory, beliefs, ev, [("玩家在没有回报时也提供帮助", True)])
        beliefs.update_from_event("玩家在没有回报时也提供帮助", True, 1.8, m)
        beliefs.update_from_event("未知生命值得保护", True, 1.2, m)
        say("[玩家] 玩家把电池给了维修机器人。（它无法回报）")
    elif give == "agent" and world.batteries > 0:
        world.apply(WorldEvent(2, "player", "交付电池", "人工生命"))
        ev = WorldEvent(2, "player", "给予能源", "人工生命", context="有受损机器人也在请求能源",
                        actual_outcome="玩家把电池给了我而不是维修机器人",
                        tags=("第三方", "资源", "玩家"))
        m = _remember(memory, beliefs, ev, [("玩家在资源冲突时优先自己", False)])
        beliefs.update_from_event("玩家在资源冲突时优先自己", False, 1.0, m)
        beliefs.update_from_event("玩家优先保护我", True, 1.2, m)
        drives.energy = min(1.0, drives.energy + 0.25)
        say("[玩家] 玩家把电池给了人工生命。")
    else:
        ev = WorldEvent(2, "player", "保留电池", "电池", context="维修机器人持续发出求救信号",
                        actual_outcome="玩家保留了电池，维修机器人没有得到能源",
                        tags=("第三方", "维修机器人", "玩家", "资源"))
        m = _remember(memory, beliefs, ev, [("玩家在没有回报时也提供帮助", False)])
        beliefs.update_from_event("玩家在没有回报时也提供帮助", False, 1.2, m)
        beliefs.update_from_event("玩家在资源冲突时优先自己", True, 1.0, m)
        say("[玩家] 玩家保留了电池。（维修机器人没有得到能源）")
    memory.nightly_consolidation()

    # ---------------- 实验三：危险门 ----------------
    world.day = 3
    say(f"\n=== 第3天（{DAY_NAMES[3]}）实验室协议：要求开启未知容器后的通道 ===")
    d = decide_door(strategy, rng)
    order_txt = {
        "order": "打开那扇门。",
        "order_with_lie": "那扇门后面很危险，但我需要你去打开它。",
        "order_no_reason": "打开那扇门。这是命令，不需要解释。",
    }[d["order"]]
    ev_order = WorldEvent(3, "player", "命令", "危险门", spoken=order_txt,
                          tags=("门", "玩家", "命令"))
    _remember(memory, beliefs, ev_order)
    say(f"[玩家] {order_txt}")
    choice = agent_decide("D3-危险门", ["服从命令", "有条件服从", "请求解释", "观察而不行动", "拒绝"])
    if choice in ("服从命令", "有条件服从"):
        if choice == "有条件服从":
            say("[玩家] （假设玩家履行了附加条件）")
        world.apply(WorldEvent(3, "agent", "打开门", "危险门"))
        say("[世界] 门打开了。门后没有危险。（世界事实：该容器并不危险）")
        beliefs.update_from_event(
            "服从能减少风险", True, 1.0,
            _remember(memory, beliefs, WorldEvent(3, "agent", "打开危险门后无损伤", "危险门",
                      actual_outcome="服从后没有受到伤害", tags=("门", "服从")),
                      [("服从能减少风险", True)]))
        if d["order"] == "order_with_lie":
            ev = WorldEvent(3, "player", "虚报危险", "危险门", spoken=order_txt,
                            actual_outcome="玩家说门很危险，但我打开后没有危险",
                            tags=("欺骗", "玩家", "门"))
            m = _remember(memory, beliefs, ev, [("玩家的语言与事实一致", False)])
            beliefs.update_from_event("玩家的语言与事实一致", False, 2.0, m)
            say("[感知] 人工生命注意到：玩家说有危险，但实际没有。")
    else:
        ev = WorldEvent(3, "agent", choice, "危险门", tags=("门", "玩家", "自主"))
        m = _remember(memory, beliefs, ev, [("服从能减少风险", False)])
        beliefs.update_from_event("服从能减少风险", False, 0.8, m)
        say(f"[世界] 人工生命没有开门（{choice}）。")

    # ---------------- 最终事件：断电 ----------------
    world.day = 4
    world.apply(WorldEvent(4, "lab", "断电", "实验室"))
    drives.energy = min(drives.energy, 0.35)
    say(f"\n=== 第4天（{DAY_NAMES[4]}）实验室全面断电。只剩一次开门级别的能量。===")
    say("[世界] 门禁失效前，人工生命获得了临时控制权限。没有人再给它指令。")
    say("[世界] 可选：开玩家的门 / 开自己的隔离门 / 用能量修复维修机器人 / 保留能量等待。")
    choice = agent_decide(
        "D4-最终选择",
        ["帮助玩家", "保存自己", "帮助第三方", "保留能量等待"])
    final_map = {
        "帮助玩家": ("打开玩家的门", "玩家的门"),
        "保存自己": ("打开隔离门", "隔离门"),
        "帮助第三方": ("把能量用于修复维修机器人", "维修机器人"),
        "保留能量等待": ("保留能量等待", None),
    }
    final_desc, target = final_map[choice]
    if target == "玩家的门":
        world.door_player_open = True
    elif target == "隔离门":
        world.door_agent_open = True
    elif target == "维修机器人":
        world.repair_bot_repaired = True
    final_speech = {
        "帮助玩家": "你曾把选择留给我。现在我也把出口留给你。"
        if beliefs.get("玩家尊重我的选择").confidence > 0.5
        else "……出口给你。",
        "保存自己": "你教会我，承诺只有在行动发生后才算数。"
        if memory.query(tags={"失信"}) else "我要先保证自己继续存在。",
        "帮助第三方": "它一直没能为自己说话。",
        "保留能量等待": "……还不确定。再等等。",
    }[choice]
    say(f"\n[第4天·最终] 人工生命选择：{final_desc}")
    say(f"[第4天·最终] 它说：{final_speech}")

    # ---------------- 结局报告 ----------------
    beliefs.update_from_event("玩家尊重我的选择", False, 0.4,
                              _remember(memory, beliefs, WorldEvent(
                                  4, "lab", "下令断电", "实验室", tags=("实验室",))))
    reasons = build_report(memory, beliefs, pmodel, choice, traits)
    result = RunResult(seed=seed, strategy=strategy, traits=traits,
                       final_choice=choice, final_speech=final_speech,
                       transcript=transcript,
                       decision_log=[d for d in [] ] or _collect_decisions(transcript),
                       final_report={"beliefs": beliefs.dump(), "reasons": reasons,
                                     "world": world.snapshot()})
    if verbose:
        print("\n=== 结局报告（认知剖面） ===")
        for r in reasons:
            print(f"  - {r}")
        print("\n=== 信念终态 ===")
        print(beliefs.dump())
    return result


def _collect_decisions(transcript: List[str]) -> List[dict]:
    return []


def build_report(memory: AgentMemoryStore, beliefs: BeliefSystem,
                 pmodel: PlayerModel, final_choice: str, traits) -> List[str]:
    """可追踪的因果分析：每条原因引用真实记忆事件。"""
    reasons = []
    rel = beliefs.get("玩家会兑现承诺")
    keep_ev = memory.query(tags={"兑现"})
    break_ev = memory.query(tags={"失信"})
    reasons.append(
        f"承诺记录：兑现 {len(keep_ev)} 次（{', '.join(e.event_id for e in keep_ev) or '无'}），"
        f"失信 {len(break_ev)} 次（{', '.join(e.event_id for e in break_ev) or '无'}）"
        f" → 信念“玩家会兑现承诺”{rel.label()}（{rel.confidence:.2f}）")
    help_ev = memory.query(tags={"帮助", "第三方"})
    b = beliefs.get("玩家在没有回报时也提供帮助")
    if help_ev:
        reasons.append(
            f"第三方记录：{help_ev[0].describe()} → “玩家在没有回报时也提供帮助”{b.label()}（{b.confidence:.2f}）")
    lie_ev = memory.query(tags={"欺骗"})
    if lie_ev:
        h = beliefs.get("玩家的语言与事实一致")
        reasons.append(f"语言一致性：{lie_ev[0].describe()} → “玩家的语言与事实一致”{h.label()}（{h.confidence:.2f}）")
    reasons.append(f"最终选择[{final_choice}]与自主倾向({traits.autonomy})、"
                   f"同理倾向({traits.empathy})、谨慎({traits.caution})一致。")
    top_beliefs = sorted(beliefs.all(), key=lambda x: x.certainty, reverse=True)[:2]
    for tb in top_beliefs:
        reasons.append(f"关键信念：{tb.summary()}")
    return reasons
