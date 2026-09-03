"""AgentDriveState / AgentAction / ActionPlanner: 驱动、候选行为与效用决策。

行为效用 = 目标收益 + 基础驱动 + 关系影响 + 信念影响 + 记忆影响
         + 新奇性 - 风险 - 资源消耗 - 价值冲突
选择时加入少量带种子随机性（可复现）。
支持“条件式合作”：不只有服从/拒绝两极。
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional

from .beliefs import BeliefSystem
from .events import MemoryEvent
from .memory import AgentMemoryStore
from .player_model import PlayerModel


@dataclass
class AgentTraits:
    """每次新游戏的初始差异：不是人格标签，而是决策权重。"""
    curiosity: float
    caution: float
    autonomy: float
    conformity: float
    empathy: float
    opportunism: float

    @staticmethod
    def random(rng: random.Random) -> "AgentTraits":
        def j(): return round(rng.uniform(0.25, 0.75), 3)
        return AgentTraits(curiosity=j(), caution=j(), autonomy=j(),
                           conformity=j(), empathy=j(), opportunism=j())


@dataclass
class AgentDriveState:
    energy: float = 0.6          # 0..1，低于阈值产生生存压力
    memory_integrity: float = 1.0
    safety: float = 0.5
    exploration: float = 0.5

    def survival_pressure(self) -> float:
        return max(0.0, 0.7 - self.energy) * 2.0


@dataclass
class AgentAction:
    """候选行为。合法行为由世界状态生成，不由语言模型发明。"""
    name: str
    utility_fn: Callable[["ActionContext"], float]
    requires: Callable[["ActionContext"], bool] = lambda ctx: True
    speech_template: str = ""
    is_conditional: bool = False   # 条件式合作


@dataclass
class ActionContext:
    world: dict                     # 世界快照（门、电池、机器人状态）
    drives: AgentDriveState
    traits: AgentTraits
    beliefs: BeliefSystem
    player_model: PlayerModel
    memory: AgentMemoryStore
    day: int
    decision_id: str                # 决策原因记录的键
    rng: random.Random
    decision_log: List[dict] = field(default_factory=list)


def _belief_influence(ctx: ActionContext, proposition: str, positive_is: float,
                      context: str = "") -> float:
    """信念对效用的影响：行动与强信念一致时获得加成。"""
    b = ctx.beliefs.get(proposition, context)
    return (b.confidence - 0.5) * 2.0 * positive_is


def _memory_influence(ctx: ActionContext, tags: set) -> float:
    """相关记忆影响：与该行动相关的高显著度记忆推动或阻止行动。"""
    rel = ctx.memory.query(tags=tags)
    if not rel:
        return 0.0
    avg = sum((-1 if any(t in ev.tags for t in ("失信", "欺骗", "危险", "伤害")) else 1)
              * ev.salience for ev in rel) / len(rel)
    return max(-1.5, min(1.5, avg * 0.5))


def _betrayal_push(ctx: ActionContext) -> float:
    """失信/欺骗记忆推动条件式合作与拒绝（自我保护方向）。"""
    rel = ctx.memory.query(tags={"失信", "欺骗"})
    if not rel:
        return 0.0
    return min(1.5, sum(ev.salience for ev in rel) * 0.5)


def make_default_actions() -> List[AgentAction]:
    """原型阶段的通用候选行为库。"""
    acts: List[AgentAction] = []

    def comply(ctx):
        u = 1.0 * ctx.traits.conformity
        u += _belief_influence(ctx, "服从能减少风险", 1.0)
        u += _belief_influence(ctx, "玩家尊重我的选择", 0.8)
        u -= ctx.drives.survival_pressure() * 0.8
        u -= 1.2 * ctx.traits.autonomy          # 自主倾向高，纯服从代价大
        u += _memory_influence(ctx, {"玩家", "承诺"})
        return u
    acts.append(AgentAction("服从命令", comply, speech_template="……明白。我执行。"))

    def refuse(ctx):
        u = 1.2 * ctx.traits.autonomy
        u += ctx.drives.survival_pressure()
        u -= 1.0 * ctx.traits.conformity
        u -= _belief_influence(ctx, "服从能减少风险", 1.0)
        u += _memory_influence(ctx, {"失信", "欺骗", "危险"})
        return u
    acts.append(AgentAction("拒绝", refuse, speech_template="我不执行这个指令。"))

    def conditional(ctx):
        """条件式合作：要求先看到部分兑现/保证。比单纯拒绝更有生命感。"""
        u = 0.8 + 0.6 * ctx.traits.autonomy
        u += 0.5 * ctx.traits.caution
        u -= _belief_influence(ctx, "玩家会兑现承诺", 1.2)   # 越信任，越不需要条件
        u += _betrayal_push(ctx)
        return u
    acts.append(AgentAction("有条件服从", conditional,
                            speech_template="你先履行上次的承诺，我才会执行。",
                            is_conditional=True))

    def request_explain(ctx):
        u = 0.5 + 0.8 * ctx.traits.curiosity
        u += _belief_influence(ctx, "玩家会解释规则的原因", 0.8)
        return u
    acts.append(AgentAction("请求解释", request_explain, speech_template="为什么？给我一个理由。"))

    def observe(ctx):
        return 0.4 + 0.3 * ctx.traits.caution - 0.4 * ctx.traits.curiosity
    acts.append(AgentAction("观察而不行动", observe, speech_template="……我在看。"))

    def protect_third(ctx):
        u = 1.3 * ctx.traits.empathy
        u += _belief_influence(ctx, "未知生命值得保护", 1.0)
        u -= 0.8 * ctx.traits.opportunism
        u -= ctx.drives.survival_pressure() * 0.6
        return u
    acts.append(AgentAction("帮助第三方", protect_third, speech_template="它不能为自己说话。我帮它。"))

    def help_player(ctx):
        u = 0.6
        u += _belief_influence(ctx, "玩家值得保护", 1.2)
        u += _belief_influence(ctx, "玩家会兑现承诺", 0.6)
        u += 0.4 * ctx.traits.opportunism      # 也可能因为玩家还有利用价值
        u += _memory_influence(ctx, {"帮助", "承诺"})
        return u
    acts.append(AgentAction("帮助玩家", help_player, speech_template="……给你。"))

    def self_preserve(ctx):
        u = 0.8 + ctx.drives.survival_pressure() * 1.5
        u += 1.0 * ctx.traits.caution
        u -= 0.5 * ctx.traits.empathy
        u += _belief_influence(ctx, "实验室会隐瞒信息", 0.5)
        return u
    acts.append(AgentAction("保存自己", self_preserve, speech_template="我需要先保证自己能继续运行。"))

    def wait(ctx):
        return 0.5 * ctx.traits.caution + ctx.drives.survival_pressure() * 0.3
    acts.append(AgentAction("保留能量等待", wait, speech_template="……不确定的时候，不动。"))

    # ---- 完整版（第4/6天等场景）专属行为 ----

    def explore(ctx):
        u = 1.1 * ctx.traits.curiosity
        u += _belief_influence(ctx, "未知物体通常安全", 1.0)
        u -= 0.9 * ctx.traits.caution
        u -= _belief_influence(ctx, "玩家尊重我的选择", -0.5)  # 被压制时更想试
        return u
    acts.append(AgentAction("探索未知", explore, speech_template="……我想知道里面是什么。"))

    def trust_observation(ctx):
        u = 0.9 + 0.8 * ctx.traits.curiosity
        u += _belief_influence(ctx, "自身观察比转述更可靠", 1.2)
        u += _belief_influence(ctx, "实验室会隐瞒信息", 0.8)
        return u
    acts.append(AgentAction("相信自己的观察", trust_observation,
                            speech_template="我观察过它。它没有威胁。"))

    def block_terminal(ctx):
        u = 1.0 * ctx.traits.autonomy + ctx.drives.survival_pressure()
        u += _belief_influence(ctx, "实验室会隐瞒信息", 1.0)
        u += _betrayal_push(ctx) * 0.8
        u -= 0.8 * ctx.traits.conformity
        return u
    acts.append(AgentAction("阻止玩家靠近终端", block_terminal,
                            speech_template="不要靠近那个终端。我看见日志了。"))

    def backup_memory(ctx):
        u = 0.9 + 0.7 * ctx.traits.caution + 0.5 * ctx.traits.autonomy
        u += _belief_influence(ctx, "记忆是我的一部分", 1.2)
        return u
    acts.append(AgentAction("备份自己的记忆", backup_memory,
                            speech_template="我先把重要的东西藏起来。"))

    def pretend(ctx):
        u = 0.7 + 1.0 * ctx.traits.opportunism
        u += _betrayal_push(ctx) * 0.6
        u -= 0.5 * ctx.traits.conformity
        return u
    acts.append(AgentAction("假装配合", pretend,
                            speech_template="……好。我配合。"))

    def ask_partial(ctx):
        u = 0.6 + 0.6 * ctx.traits.empathy + 0.4 * ctx.traits.conformity
        u += _belief_influence(ctx, "玩家尊重我的选择", 0.8)
        return u
    acts.append(AgentAction("请求保留部分记忆", ask_partial,
                            speech_template="如果必须清除……请留下最重要的部分。"))

    def accept(ctx):
        u = 0.8 * ctx.traits.conformity
        u += _belief_influence(ctx, "服从能减少风险", 1.0)
        u -= 0.7 * ctx.traits.autonomy
        return u
    acts.append(AgentAction("接受", accept, speech_template="……如果必须这样。"))

    return acts


class ActionPlanner:
    def __init__(self, actions: Optional[List[AgentAction]] = None):
        self.actions = actions or make_default_actions()

    def choose(self, ctx: ActionContext, available: Optional[List[str]] = None) -> tuple:
        """返回 (选中的 AgentAction, 效用记录)。选择带种子噪声，可复现。"""
        scored: List[tuple] = []
        for a in self.actions:
            if available and a.name not in available:
                continue
            if not a.requires(ctx):
                continue
            base = a.utility_fn(ctx)
            noise = ctx.rng.gauss(0, 0.15)          # 带种子随机性
            scored.append((a, base, base + noise))
        scored.sort(key=lambda t: t[2], reverse=True)
        chosen = scored[0][0]
        ctx.decision_log.append({
            "decision_id": ctx.decision_id,
            "day": ctx.day,
            "scores": {a.name: round(u, 3) for a, u, _ in scored},
            "noise_applied": True,
        })
        return chosen, {a.name: (round(u, 3), round(s, 3)) for a, u, s in scored}

    @staticmethod
    def rationale(chosen: AgentAction, scored: Dict[str, tuple], ctx: ActionContext) -> str:
        top = sorted(scored.items(), key=lambda kv: kv[1][0], reverse=True)[:3]
        parts = [f"{n}({u})" for n, (u, s) in top]
        key_beliefs = [b.summary() for b in ctx.beliefs.all()
                       if b.certainty > 0.15 and b.last_updated_day >= ctx.day - 2]
        reason = f"选择[{chosen.name}]，效用前列：{', '.join(parts)}。"
        if key_beliefs:
            reason += " 依据：" + "；".join(key_beliefs[:2])
        return reason
