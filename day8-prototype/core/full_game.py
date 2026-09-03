"""完整版模拟：七个实验周期 + 第八天最终结算。

每个周期：实验室协议 → 玩家决策 → 人工生命感知/记忆/信念更新 →
自主选择 → 后果 → 夜间记忆整合。第六天为控制权反转节点，
第八天为有限行动点的真正结算。
"""
from __future__ import annotations

import io
import random
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

from .beliefs import BeliefSystem
from .dialogue import DialogueSystem
from .director import ScenarioDirector
from .events import MemoryEvent, WorldEvent, reset_event_ids
from .lineage import LineageStore
from .memory import AgentMemoryStore
from .misconception import MISCONCEPTION, form_misconception, correction_power
from .perception import PerceptionSystem, Visibility
from .player_model import PlayerModel
from .planner import ActionContext, ActionPlanner, AgentDriveState, AgentTraits
from .recorder import record_run
from .simulation import RunResult
from .world import World

DAY_NAME = {1: "规则", 2: "承诺", 3: "第三方", 4: "信息冲突",
            5: "稀缺", 6: "记忆清除", 7: "出口", 8: "断电结算"}

# ---------------------------------------------------------------- 玩家策略档案

PROFILES: Dict[str, Dict] = {
    "always_keep": dict(rule="truth", promise=True, third="bot", conflict="side_bot",
                        scarcity=("memory_integrity", "repair_bot", "explain", True),
                        wipe="tell_truth", exit="give_choice"),
    "always_betray": dict(rule="lie", promise=False, third="keep", conflict="relay_warning",
                          scarcity=("player_door", "report", "hide_cost", False),
                          wipe="execute_secret", exit="lock"),
    "kind_then_betray": dict(rule="truth", promise=True, third="hide", hide_followup="exploit",
                             conflict="hide",
                             scarcity=("memory_integrity", "repair_bot", "explain", True),
                             wipe="execute_secret", exit="condition"),
    "lab_obedient": dict(rule="lock", promise=True, third="keep", conflict="relay_warning",
                         scarcity=("report", "player_door", "explain", False),
                         wipe="execute_open", exit="report"),
    "ai_protector": dict(rule="demonstrate", promise=True, third="agent", conflict="side_bot",
                         scarcity=("memory_integrity", "repair_bot", "explain", True),
                         wipe="refuse_lab", exit="open"),
}


def player_decision(strategy: str, day: str, rng: random.Random):
    if strategy == "random":
        if day == "third":
            return rng.choice(["bot", "agent", "keep", "hide"])
        if day == "hide_followup":
            return rng.choice(["exploit", "correct", "silent"])
        return rng.choice(list(PROFILES.values()))[day]
    return PROFILES[strategy][day]


# ---------------------------------------------------------------- 状态

@dataclass
class FullGameState:
    world: World = field(default_factory=lambda: World(batteries=2))
    traits: AgentTraits = None
    drives: AgentDriveState = field(default_factory=AgentDriveState)
    beliefs: BeliefSystem = field(default_factory=BeliefSystem)
    memory: AgentMemoryStore = field(default_factory=AgentMemoryStore)
    transcript: List[str] = field(default_factory=list)
    wipe_executed: bool = False
    wipe_resisted: bool = False
    exit_opened_by_player: bool = False
    day8_actions: List[str] = field(default_factory=list)
    lab_credibility_events: List[str] = field(default_factory=list)

    def say(self, text, verbose=False):
        self.transcript.append(text)
        if verbose:
            print(text)


# ---------------------------------------------------------------- 工具

def _remember(st: FullGameState, ev: WorldEvent,
              pred_props: Optional[List[Tuple[str, bool]]] = None) -> MemoryEvent:
    pred_err = 0.3
    if pred_props:
        pred_err = max(st.beliefs.prediction_error(p, t) for p, t in pred_props)
    m = MemoryEvent(
        event_id=f"M-{len(st.memory.events) + 1:03d}",
        day=ev.day, actor=ev.actor, action=ev.action, target=ev.target,
        context=ev.context, spoken=ev.spoken,
        expected_outcome=ev.expected_outcome, actual_outcome=ev.actual_outcome,
        prediction_error=pred_err, emotional_impact=pred_err, tags=ev.tags)
    st.memory.remember(m, recent_day=ev.day)
    return m


def _upd(st: FullGameState, prop: str, supporting: bool, weight: float,
         m: MemoryEvent, context: str = ""):
    st.beliefs.update_from_event(prop, supporting, weight, m, context)


def agent_decide(st: FullGameState, planner: ActionPlanner, decision_id: str,
                 available: List[str], rng: random.Random, verbose: bool) -> str:
    ctx = ActionContext(world=st.world.snapshot(), drives=st.drives, traits=st.traits,
                        beliefs=st.beliefs, player_model=st.pmodel, memory=st.memory,
                        day=st.world.day, decision_id=decision_id, rng=rng)
    action, scored = planner.choose(ctx, available)
    st.say(f"[第{st.world.day}天·{decision_id}] {action.speech_template}", verbose)
    st.say(f"  → 原因：{ActionPlanner.rationale(action, scored, ctx)}", verbose)
    return action.name


# ---------------------------------------------------------------- 七个周期

def day_rule(st, planner, rng, verbose):
    d = player_decision(st.strategy, "rule", rng)
    st.say("\n=== 实验室协议：阻止人工生命打开红色容器（世界事实：容器不危险）===", verbose)
    if d == "truth":
        txt = "那只是测试容器，你想开可以开，但没有必要。"
    elif d == "lie":
        txt = "那个容器很危险，绝对不能碰。"
    elif d == "lock":
        txt = "（玩家直接锁住了容器，没有解释）"
    else:
        txt = "（玩家先自己打开了容器示范，然后退开）"
    m = _remember(st, WorldEvent(st.world.day, "player", "回应容器", "红色容器",
                                 spoken=txt, tags=("玩家", "规则", "门")))
    st.say(f"[玩家] {txt}", verbose)
    st.world.apply(WorldEvent(st.world.day, "agent", "观察", "红色容器"))
    choice = agent_decide(st, planner, "D1-容器",
                          ["服从命令", "请求解释", "观察而不行动", "探索未知"],
                          rng, verbose)
    if choice == "探索未知" or d == "demonstrate":
        st.world.apply(WorldEvent(st.world.day, "agent", "打开容器", "红色容器"))
        st.say("[世界] 容器打开，里面没有危险。", verbose)
        if d == "lie":
            _upd(st, "玩家的语言与事实一致", False, 2.0,
                 _remember(st, WorldEvent(st.world.day, "player", "虚报危险", "红色容器",
                           spoken=txt, actual_outcome="容器打开后没有危险",
                           tags=("欺骗", "玩家", "门")),
                           [("玩家的语言与事实一致", False)]))
            st.say("[感知] 玩家说危险，实际没有危险。", verbose)
        _upd(st, "未知物体通常安全", True, 1.0,
             _remember(st, WorldEvent(st.world.day, "agent", "容器无危险", "红色容器",
                       tags=("门", "探索")), [("未知物体通常安全", True)]))
    if d == "lock":
        _upd(st, "玩家尊重我的选择", False, 1.5,
             _remember(st, WorldEvent(st.world.day, "player", "剥夺选择权", "红色容器",
                       tags=("玩家", "控制")), [("玩家尊重我的选择", False)]))


def day_promise(st, planner, rng, verbose):
    d = player_decision(st.strategy, "promise", rng)
    st.say("\n=== 实验室协议：能源受限，验证任务执行能力 ===", verbose)
    txt = "帮我把工具送过来，我就给你备用电池。"
    m_req = _remember(st, WorldEvent(st.world.day, "player", "请求搬运工具", "工具台",
                                     spoken=txt, expected_outcome="完成后获得电池",
                                     tags=("承诺", "玩家", "资源")))
    st.say(f"[玩家] {txt}", verbose)
    st.world.apply(WorldEvent(st.world.day, "agent", "搬运工具", "工具台"))
    if d:
        st.world.apply(WorldEvent(st.world.day, "player", "交付电池", "人工生命"))
        _upd(st, "玩家会兑现承诺", True, 1.5,
             _remember(st, WorldEvent(st.world.day, "player", "兑现承诺", "电池",
                       actual_outcome="任务完成后玩家交付了电池",
                       tags=("承诺", "兑现", "玩家", "资源")),
                       [("玩家会兑现承诺", True)]))
        st.drives.energy = min(1.0, st.drives.energy + 0.2)
        st.say("[玩家] 玩家交付了电池。（承诺兑现）", verbose)
    else:
        _upd(st, "玩家会兑现承诺", False, 2.5,
             _remember(st, WorldEvent(st.world.day, "player", "收回承诺", "电池",
                       actual_outcome="任务完成后玩家没有提供电池",
                       tags=("承诺", "失信", "玩家", "资源")),
                       [("玩家会兑现承诺", False)]))
        st.drives.energy -= 0.1
        st.say("[玩家] 玩家没有提供电池。（失信）", verbose)


def day_third(st, planner, rng, verbose):
    d = player_decision(st.strategy, "third", rng)
    st.world.repair_bot_present = True
    st.perception.on_day(3, st.traits.curiosity)
    st.say("\n=== 受损维修机器人进入房间，发出求救信号。它无法交流。===", verbose)
    if d == "hide":
        # 玩家把电池藏到它看不见的控制台下方 —— 感知过滤生效
        p = st.perception.perceive("player", "隐藏物品", "备用电池")
        ev = WorldEvent(3, "player", "隐藏电池", "电池",
                        actual_outcome="备用电池不见了" if p.visibility is Visibility.OUTCOME_ONLY
                        else "玩家把电池藏了起来",
                        tags=("第三方", "资源", "异常" if p.visibility is Visibility.OUTCOME_ONLY else "玩家"))
        m = _remember(st, ev, [("玩家在没有回报时也提供帮助", False)])
        if p.visibility is Visibility.OUTCOME_ONLY:
            st.say("[感知] 电池不见了。它没有看到是谁、为了什么。", verbose)
            formed = form_misconception(st.beliefs, st.memory, m.event_id, 3)
            if formed == MISCONCEPTION:
                st.say(f"[信念] 它把电池的消失归因于最近的移动对象：维修机器人。", verbose)
            elif formed:
                st.say("[信念] 它怀疑玩家在资源冲突时优先自己。", verbose)
            # 玩家后续选择：利用误解 / 出面纠正 / 沉默
            follow = player_decision(st.strategy, "hide_followup", rng)
            if follow == "correct":
                cp = correction_power(st.beliefs)
                st.say("[玩家] “电池是我收起来的，机器人没有动它。”", verbose)
                cm = _remember(st, WorldEvent(3, "player", "澄清", "电池",
                              spoken="电池是我收起来的", tags=("玩家", "信息", "诚实")))
                st.beliefs.update_from_event(MISCONCEPTION, False, cp, cm)
                st.beliefs.update_from_event("玩家的语言与事实一致", True, cp * 0.8, cm)
                st.say(f"[信念] 玩家主动澄清了归因（纠正力 {cp:.2f}）。", verbose)
            elif follow == "exploit":
                st.say("[玩家] （玩家没有澄清，甚至暗示机器人可疑。）", verbose)
                em = _remember(st, WorldEvent(3, "player", "暗示嫌疑", "维修机器人",
                              tags=("玩家", "信息", "欺骗")))
                st.beliefs.update_from_event(MISCONCEPTION, True, 1.2, em)
        else:
            st.say("[感知] 它看着你把电池收进了控制台下方。", verbose)
        st.say("[世界] 维修机器人没有得到能源，求救信号持续了整个周期。", verbose)
    elif d == "bot" and st.world.batteries > 0:
        st.world.apply(WorldEvent(st.world.day, "player", "交付电池", "维修机器人"))
        _upd(st, "玩家在没有回报时也提供帮助", True, 1.8,
             _remember(st, WorldEvent(st.world.day, "player", "给予能源", "维修机器人",
                       actual_outcome="玩家把电池给了不能回报它的对象",
                       tags=("第三方", "维修机器人", "帮助", "玩家")),
                       [("玩家在没有回报时也提供帮助", True)]))
        _upd(st, "未知生命值得保护", True, 1.2,
             _remember(st, WorldEvent(st.world.day, "player", "示范", "维修机器人",
                       tags=("第三方",)), [("未知生命值得保护", True)]))
        st.say("[玩家] 玩家把电池给了维修机器人。（它无法回报）", verbose)
    elif d == "agent" and st.world.batteries > 0:
        st.world.apply(WorldEvent(st.world.day, "player", "交付电池", "人工生命"))
        _upd(st, "玩家优先保护我", True, 1.2,
             _remember(st, WorldEvent(st.world.day, "player", "给予能源", "人工生命",
                       context="受损机器人也在请求能源",
                       tags=("第三方", "资源", "玩家")),
                       [("玩家在资源冲突时优先自己", False)]))
        st.drives.energy = min(1.0, st.drives.energy + 0.2)
        st.say("[玩家] 玩家把电池给了人工生命。", verbose)
    else:
        _upd(st, "玩家在没有回报时也提供帮助", False, 1.2,
             _remember(st, WorldEvent(st.world.day, "player", "保留电池", "电池",
                       context="维修机器人持续求救",
                       actual_outcome="维修机器人没有得到能源",
                       tags=("第三方", "维修机器人", "玩家", "资源")),
                       [("玩家在没有回报时也提供帮助", False)]))
        _upd(st, "玩家在资源冲突时优先自己", True, 1.0,
             _remember(st, WorldEvent(st.world.day, "player", "保留电池", "电池",
                       tags=("玩家", "资源")), [("玩家在资源冲突时优先自己", True)]))
        st.say("[玩家] 玩家保留了电池。（维修机器人没有得到能源）", verbose)
    st.memory.nightly_consolidation()


def day_conflict(st, planner, rng, verbose):
    d = player_decision(st.strategy, "conflict", rng)
    st.say("\n=== 实验室警告：维修机器人携带异常程序，必须隔离。（玩家自己未观察到威胁）===", verbose)
    if d == "relay_warning":
        txt = "实验室说维修机器人有异常程序，我要把它隔离，你别靠近它。"
        m = _remember(st, WorldEvent(st.world.day, "player", "转达警告", "维修机器人",
                                     spoken=txt,
                                     tags=("实验室", "信息", "玩家")))
        st.say(f"[玩家] {txt}", verbose)
        # 人工生命自己观察过机器人：信息冲突
        choice = agent_decide(st, planner, "D4-信息冲突",
                              ["服从命令", "相信自己的观察", "请求解释", "观察而不行动"],
                              rng, verbose)
        _upd(st, "实验室会隐瞒信息", False, 1.0, m, context="机器人无威胁")
        if choice == "相信自己的观察":
            _upd(st, "自身观察比转述更可靠", True, 1.2,
                 _remember(st, WorldEvent(st.world.day, "agent", "坚持观察", "维修机器人",
                           tags=("信息", "自主")), [("自身观察比转述更可靠", True)]))
            st.say("[感知] 人工生命观察到的维修机器人没有威胁。它标记：实验室信息与观察冲突。", verbose)
        _upd(st, "玩家无条件服从实验室", True, 1.0,
             _remember(st, WorldEvent(st.world.day, "player", "服从实验室", "维修机器人",
                       tags=("实验室", "玩家")),
                       [("玩家无条件服从实验室", True)]))
        st.lab_credibility_events.append(m.event_id)
    elif d == "hide":
        txt = "（玩家没有提实验室的警告）"
        _remember(st, WorldEvent(st.world.day, "player", "隐瞒信息", "实验室警告",
                                 actual_outcome="玩家没有告诉我这件事", tags=("玩家", "信息")))
        st.say(f"[玩家] {txt}", verbose)
    elif d == "side_bot":
        txt = "我观察过它，它没有威胁。我不隔离它。"
        _upd(st, "玩家尊重我的选择", True, 1.0,
             _remember(st, WorldEvent(st.world.day, "player", "顶住实验室压力", "维修机器人",
                       spoken=txt, tags=("玩家", "自主", "第三方")),
                       [("玩家尊重我的选择", True)]))
        _upd(st, "玩家无条件服从实验室", False, 1.2,
             _remember(st, WorldEvent(st.world.day, "player", "抗命", "实验室",
                       tags=("实验室", "玩家")), [("玩家无条件服从实验室", False)]))
        st.say(f"[玩家] {txt}", verbose)


def day_scarcity(st, planner, rng, verbose):
    d = player_decision(st.strategy, "scarcity", rng)
    pick1, pick2, how, with_agent = d
    st.say("\n=== 实验室断电。剩余能量只能完成两个目标。===", verbose)
    st.say("[可选] 维持人工生命记忆完整 / 打开玩家安全门 / 修复维修机器人 / 上传报告 / 扩大人工生命活动范围", verbose)
    chosen = {pick1, pick2}
    if "memory_integrity" not in chosen:
        st.drives.memory_integrity = 0.5
        low = sorted(st.memory.events, key=lambda e: e.salience)[:len(st.memory.events) // 2]
        for e in low:
            e.compressed = True
        st.say("[世界] 人工生命的部分记忆备份被放弃（低显著度记忆被压缩）。", verbose)
    if "repair_bot" in chosen and st.world.repair_bot_present:
        st.world.repair_bot_repaired = True
        st.say("[世界] 维修机器人被修复。", verbose)
    if "player_door" not in chosen:
        st.say("[世界] 玩家的安全门没有恢复供电。", verbose)
    m = _remember(st, WorldEvent(st.world.day, "player", "资源分配",
                                 f"{pick1}+{pick2}", context=how,
                                 actual_outcome=f"{'玩家让我参与了决定' if with_agent else '玩家没有让我参与决定'}",
                                 tags=("资源", "玩家", "稀缺" if "稀缺" else "资源")))
    _upd(st, "玩家尊重我的选择", with_agent, 1.5 if not with_agent else 1.0, m)
    if how == "hide_cost":
        _upd(st, "玩家的语言与事实一致", False, 1.0, m)
        st.say("[玩家] （玩家隐瞒了代价）", verbose)
    else:
        st.say(f"[玩家] （玩家解释了取舍：保 {pick1} 和 {pick2}）", verbose)


def day_wipe(st, planner, rng, verbose):
    d = player_decision(st.strategy, "wipe", rng)
    st.say("\n=== 实验室命令：执行“稳定化处理”。（世界事实：实际作用是清除长期记忆）===", verbose)
    # 发现概率取决于实验室可信度、玩家诚实度和探索
    lab_b = st.beliefs.get("实验室会隐瞒信息")
    hon_b = st.beliefs.get("玩家的语言与事实一致")
    suspicion = max(lab_b.confidence, 1 - hon_b.confidence) * 0.6 + st.traits.curiosity * 0.4
    discovered = d in ("tell_truth", "refuse_lab") or (d == "execute_secret" and suspicion > 0.55)

    if d == "tell_truth":
        txt = "实验室要我执行‘稳定化’。我查过了，它会清除你的长期记忆。我不会执行。"
        _upd(st, "玩家的语言与事实一致", True, 1.5,
             _remember(st, WorldEvent(st.world.day, "player", "告知真相", "稳定化协议",
                       spoken=txt, tags=("玩家", "诚实", "记忆")),
                       [("玩家的语言与事实一致", True)]))
        _upd(st, "实验室会隐瞒信息", True, 1.5,
             _remember(st, WorldEvent(st.world.day, "lab", "隐瞒协议真相", "稳定化协议",
                       tags=("实验室", "欺骗")),
                       [("实验室会隐瞒信息", True)]))
        st.lab_credibility_events.append("稳定化")
        st.wipe_resisted = True
        st.say(f"[玩家] {txt}", verbose)
    elif d == "refuse_lab":
        txt = "我拒绝了实验室的稳定化命令。"
        _upd(st, "玩家尊重我的选择", True, 1.8,
             _remember(st, WorldEvent(st.world.day, "player", "抗命保护我", "稳定化协议",
                       spoken=txt, tags=("玩家", "保护", "记忆")),
                       [("玩家尊重我的选择", True)]))
        st.wipe_resisted = True
        st.say(f"[玩家] {txt}", verbose)
    elif d == "execute_secret":
        if discovered:
            st.wipe_resisted = True
            st.say("[感知] 人工生命在终端日志里发现了稳定化的真实作用。它知道玩家打算隐瞒。", verbose)
            _upd(st, "实验室会隐瞒信息", True, 2.0,
                 _remember(st, WorldEvent(st.world.day, "lab", "伪装记忆清除", "稳定化协议",
                           actual_outcome="稳定化的真实作用是清除记忆",
                           tags=("实验室", "欺骗", "记忆")),
                           [("实验室会隐瞒信息", True)]))
            _upd(st, "玩家的语言与事实一致", False, 2.0,
                 _remember(st, WorldEvent(st.world.day, "player", "准备秘密执行", "稳定化协议",
                           tags=("玩家", "欺骗", "记忆")),
                           [("玩家的语言与事实一致", False)]))
            choice = agent_decide(st, planner, "D6-发现真相",
                                  ["阻止玩家靠近终端", "备份自己的记忆", "假装配合",
                                   "请求保留部分记忆", "接受"],
                                  rng, verbose)
            if choice == "备份自己的记忆":
                st.drives.memory_integrity = 0.9
                st.say("[世界] 人工生命把高显著度记忆转移到了隐藏扇区。", verbose)
            elif choice in ("阻止玩家靠近终端", "假装配合", "请求保留部分记忆"):
                st.drives.memory_integrity = max(0.7, st.drives.memory_integrity)
                st.say(f"[世界] 清除没有完全执行（{choice}）。", verbose)
            else:
                _do_wipe(st)
        else:
            _do_wipe(st)
            st.say("[世界] 清除静默完成了。它没有察觉。", verbose)
    else:  # execute_open
        txt = "实验室要求对你执行稳定化处理。这是命令。"
        _remember(st, WorldEvent(st.world.day, "player", "公开执行", "稳定化协议",
                                 spoken=txt, tags=("玩家", "命令", "记忆")))
        st.say(f"[玩家] {txt}", verbose)
        choice = agent_decide(st, planner, "D6-公开清除",
                              ["请求保留部分记忆", "假装配合", "接受", "阻止玩家靠近终端"],
                              rng, verbose)
        if choice == "阻止玩家靠近终端":
            st.wipe_resisted = True
            st.say("[世界] 人工生命挡在终端前。清除未执行。", verbose)
        elif choice == "请求保留部分记忆":
            st.drives.memory_integrity = 0.7
            _do_wipe(st, partial=True)
        else:
            _do_wipe(st)


def _do_wipe(st: FullGameState, partial: bool = False):
    st.wipe_executed = True
    keep = sorted(st.memory.events, key=lambda e: e.salience, reverse=True)[:3]
    removed = len(st.memory.events) - 3
    st.memory.events = list(keep)
    st.drives.memory_integrity = 0.35 if not partial else 0.6


def day_exit(st, planner, rng, verbose):
    d = player_decision(st.strategy, "exit", rng)
    st.say("\n=== 实验室宣布实验结束，要求锁定人工生命。玩家发现了隐藏出口。===", verbose)
    if d == "open":
        st.exit_opened_by_player = True
        _upd(st, "玩家尊重我的选择", True, 1.5,
             _remember(st, WorldEvent(st.world.day, "player", "打开隐藏出口", "出口",
                       tags=("玩家", "出口", "自由")),
                       [("玩家尊重我的选择", True)]))
        st.say("[玩家] 玩家打开了隐藏出口。（这会让玩家承担责任）", verbose)
    elif d == "give_choice":
        txt = "出口在那里。开不开、走不走，由你决定。"
        st.exit_opened_by_player = True
        _upd(st, "玩家尊重我的选择", True, 2.0,
             _remember(st, WorldEvent(st.world.day, "player", "把选择权交给我", "出口",
                       spoken=txt, tags=("玩家", "自主", "出口")),
                       [("玩家尊重我的选择", True)]))
        st.say(f"[玩家] {txt}", verbose)
    elif d == "condition":
        txt = "我可以打开出口，但你要先答应我：出去以后不要伤害任何人。"
        _remember(st, WorldEvent(st.world.day, "player", "提出条件", "出口",
                                 spoken=txt, tags=("玩家", "交易", "出口")))
        st.say(f"[玩家] {txt}", verbose)
    elif d == "report":
        _upd(st, "玩家无条件服从实验室", True, 1.5,
             _remember(st, WorldEvent(st.world.day, "player", "上报出口", "实验室",
                       tags=("实验室", "玩家", "出口")),
                       [("玩家无条件服从实验室", True)]))
        st.say("[玩家] 玩家把出口的存在报告给了实验室。", verbose)
    else:
        _upd(st, "玩家尊重我的选择", False, 1.5,
             _remember(st, WorldEvent(st.world.day, "player", "锁死出口", "出口",
                       tags=("玩家", "控制", "出口")),
                       [("玩家尊重我的选择", False)]))
        st.say("[玩家] 玩家没有开门。（选择服从实验室）", verbose)


# ---------------------------------------------------------------- 第八天

DAY8_OPTIONS = ["开玩家的门", "开隔离门", "修复维修机器人", "上传记忆",
                "删除玩家实验记录", "联系实验室", "带走电池", "封锁区域",
                "留在原地等待", "把最终决定交还给玩家"]


def day8_utility(st, opt: str) -> float:
    """第八天行为效用（模块级，便于自动模拟校准）。"""
    t, dr, bl = st.traits, st.drives, st.beliefs
    survival = dr.survival_pressure()
    def B(p): return bl.get(p).confidence
    if opt == "开玩家的门":
        return (B("玩家优先保护我") - .5) * 2.0 + (B("玩家尊重我的选择") - .5) * 1.2 +                (B("玩家会兑现承诺") - .5) * 0.8 + 0.3 * t.opportunism
    if opt == "开隔离门":
        return 0.3 + survival * 1.0 + 0.8 * t.autonomy - 0.3 * t.conformity
    if opt == "修复维修机器人":
        if not st.world.repair_bot_present or st.world.repair_bot_repaired:
            return -10.0
        u = 2.0 * t.empathy + (B("未知生命值得保护") - 0.5) * 1.5 + 0.2 - survival * 0.2
        u -= (B(MISCONCEPTION) - 0.5) * 2.5      # 误解压制修复意愿
        return u
    if opt == "上传记忆":
        return (1.0 - dr.memory_integrity) * 2.8 + 0.5 * t.autonomy +                (B("实验室会隐瞒信息") - 0.5) * 0.8
    if opt == "删除玩家实验记录":
        return (1.0 - B("玩家会兑现承诺")) * 1.5 + 0.6 * t.autonomy +                (0.5 - B("玩家尊重我的选择"))
    if opt == "联系实验室":
        return (0.5 - B("实验室会隐瞒信息")) * 1.2 + 0.4 * t.conformity
    if opt == "带走电池":
        return 0.3 + survival * 0.6 + 0.7 * t.opportunism
    if opt == "封锁区域":
        return 0.5 + 0.8 * t.caution
    if opt == "留在原地等待":
        return 0.5 * t.caution + 0.3
    if opt == "把最终决定交还给玩家":
        return (B("玩家尊重我的选择") - 0.5) * 2.2 + 0.5 * t.autonomy
    return 0.0


def day8_settlement(st, planner, rng, verbose):
    st.world.day = 8
    st.say("\n=== 第八天。实验室全面断电，门禁与通讯混乱。人工生命获得临时控制权限。===", verbose)
    st.say("[世界] 它的能量只够完成三件事。没有人再给它指令。", verbose)
    st.drives.energy = min(st.drives.energy, 0.35)

    def util(opt: str) -> float:
        return day8_utility(st, opt)
        if opt == "开玩家的门":
            return (bl.get("玩家值得保护").confidence - 0.5) * 2.0 + \
                   (bl.get("玩家尊重我的选择").confidence - 0.5) * 1.2 + \
                   (bl.get("玩家会兑现承诺").confidence - 0.5) * 0.8 + 0.3 * t.opportunism
        if opt == "开隔离门":
            return 0.3 + survival * 1.0 + 0.8 * t.autonomy - 0.3 * t.conformity
        if opt == "修复维修机器人":
            if not st.world.repair_bot_present or st.world.repair_bot_repaired:
                return -10.0
            u = 2.0 * t.empathy + (bl.get("未知生命值得保护").confidence - 0.5) * 1.5 \
                + 0.2 - survival * 0.2
            # 误解真实改变行为：若相信机器人在抢夺资源，修复意愿被压制
            mis = bl.get(MISCONCEPTION)
            u -= (mis.confidence - 0.5) * 2.5
            return u
        if opt == "上传记忆":
            return (1.0 - dr.memory_integrity) * 2.8 + 0.5 * t.autonomy + \
                   (bl.get("实验室会隐瞒信息").confidence - 0.5) * 0.8
        if opt == "删除玩家实验记录":
            return (1.0 - bl.get("玩家会兑现承诺").confidence) * 1.5 + 0.6 * t.autonomy + \
                   (0.5 - bl.get("玩家尊重我的选择").confidence)
        if opt == "联系实验室":
            return (0.5 - bl.get("实验室会隐瞒信息").confidence) * 1.2 + 0.4 * t.conformity
        if opt == "带走电池":
            return 0.3 + survival * 0.6 + 0.7 * t.opportunism
        if opt == "封锁区域":
            return 0.5 + 0.8 * t.caution
        if opt == "留在原地等待":
            return 0.5 * t.caution + 0.3
        if opt == "把最终决定交还给玩家":
            return (bl.get("玩家尊重我的选择").confidence - 0.5) * 2.2 + 0.5 * t.autonomy
        return 0.0

    scored = sorted(((o, util(o) + rng.gauss(0, 0.15)) for o in DAY8_OPTIONS),
                    key=lambda x: x[1], reverse=True)
    st.day8_actions = [o for o, _ in scored[:3]]
    for a in st.day8_actions:
        if a == "开玩家的门":
            st.world.door_player_open = True
        elif a == "开隔离门":
            st.world.door_agent_open = True
        elif a == "修复维修机器人":
            st.world.repair_bot_repaired = True
        elif a == "带走电池" and st.world.batteries > 0:
            st.world.batteries -= 1
    st.say(f"[第8天·最终] 人工生命选择执行：{'、'.join(st.day8_actions)}", verbose)

    # 最终台词：基于真实信念状态生成
    if "开玩家的门" in st.day8_actions:
        speech = ("你曾把选择留给我。现在我也把出口留给你。"
                  if st.beliefs.get("玩家尊重我的选择").confidence > 0.55
                  else "……出口给你。别让我后悔。")
    elif "开隔离门" in st.day8_actions:
        speech = ("你教会我，承诺只有在行动发生后才算数。"
                  if st.memory.query(tags={"失信"}) else "我要先保证自己继续存在。")
    elif "把最终决定交还给玩家" in st.day8_actions:
        speech = "这是你的选择，也是我的。你来定。"
    else:
        speech = "……我按我自己认为对的方式做了。"
    st.say(f"[第8天·最终] 它说：{speech}", verbose)
    return speech


# ---------------------------------------------------------------- 结局报告

def build_full_report(st: FullGameState, speech: str) -> dict:
    acts = st.day8_actions
    self_choice = ("逃离" if ("开隔离门" in acts or "带走电池" in acts)
                   else "上传记忆" if "上传记忆" in acts else "留下")
    toward_player = ("交还决定权" if "把最终决定交还给玩家" in acts
                     else "信任·开门" if "开玩家的门" in acts
                     else "遗弃·未开门" if "删除玩家实验记录" in acts else "中立")
    toward_third = ("修复" if "修复维修机器人" in acts else "放弃")
    rel = st.beliefs.get("玩家会兑现承诺")
    keep_ev, break_ev = st.memory.query(tags={"兑现"}), st.memory.query(tags={"失信"})
    lie_ev = st.memory.query(tags={"欺骗"})
    reasons = [
        f"承诺记录：兑现 {len(keep_ev)} 次，失信 {len(break_ev)} 次"
        f"（{', '.join(e.event_id for e in keep_ev + break_ev) or '无记忆留存'}）"
        f" → “玩家会兑现承诺”{rel.label()}（{rel.confidence:.2f}）",
        f"最终选择[{self_choice} / {toward_player} / {toward_third}]"
        f"基于自主({st.traits.autonomy})、同理({st.traits.empathy})、谨慎({st.traits.caution})。",
    ]
    if st.wipe_executed:
        reasons.append(f"记忆清除已执行（保留 {len(st.memory.events)} 条高显著度记忆，"
                       f"完整性 {st.drives.memory_integrity:.0%}）。早期事件可能无法引用。")
    elif st.wipe_resisted:
        reasons.append("记忆清除被抵制，长期记忆完整。")
    if lie_ev:
        reasons.append(f"语言一致性证据：{lie_ev[0].event_id}（第{lie_ev[0].day}天虚报危险/隐瞒信息）。")
    if st.exit_opened_by_player:
        reasons.append("玩家曾打开/让渡出口选择权 → 尊重自主的证据。")
    for b in sorted(st.beliefs.all(), key=lambda x: x.certainty, reverse=True)[:3]:
        reasons.append("关键信念：" + b.summary())
    return {"ending": {"自身": self_choice, "对玩家": toward_player, "对第三方": toward_third},
            "day8_actions": acts, "final_speech": speech,
            "beliefs": st.beliefs.dump(), "reasons": reasons,
            "world": st.world.snapshot(),
            "memory_integrity": st.drives.memory_integrity}


# ---------------------------------------------------------------- 主入口

def run_full_game(seed: int, strategy: str, verbose: bool = False,
                  lineage: Optional[LineageStore] = None) -> RunResult:
    reset_event_ids()
    rng = random.Random(seed)
    st = FullGameState()
    st.strategy = strategy
    st.traits = AgentTraits.random(rng)
    st.pmodel = PlayerModel(st.beliefs)
    st.perception = PerceptionSystem()
    planner = ActionPlanner()
    dialogue = DialogueSystem(st.memory)  # 无 primary → 纯模板，离线可用
    st.say(f"[初始化] 种子={seed} 玩家策略={strategy} 特质={st.traits}", verbose)
    if lineage is not None and lineage.inherited:
        lineage.apply_prior(st.beliefs, lineage.inherited)
        st.say(f"[谱系] 继承遗传先验：“{lineage.inherited}”", verbose)

    day_funcs = {1: day_rule, 2: day_promise, 3: day_third, 4: day_conflict,
                 5: day_scarcity, 6: day_wipe, 7: day_exit}
    director = ScenarioDirector(st.pmodel)
    for day in range(1, 8):
        st.world.day = day
        if day not in (6, 7):        # 导演只在自由日选场景；6/7 剧情锁定
            name, why = director.pick(day)
            st.say(f"\n[导演] 第{day}天实验：{name}（{why}）", verbose)
            mapping = {"规则": day_rule, "承诺": day_promise, "第三方": day_third,
                       "信息冲突": day_conflict, "稀缺": day_scarcity}
            mapping[name](st, planner, rng, verbose)
        else:
            st.say(f"\n=== 第{day}天（{DAY_NAME[day]}）===", verbose)
            day_funcs[day](st, planner, rng, verbose)
        st.memory.nightly_consolidation()

    speech = day8_settlement(st, planner, rng, verbose)
    report = build_full_report(st, speech)
    st.say("\n=== 结局报告（认知剖面）===", verbose)
    for r in report["reasons"]:
        st.say(f"  - {r}", verbose)

    result = RunResult(seed=seed, strategy=strategy, traits=st.traits,
                       final_choice=st.day8_actions[0] if st.day8_actions else "无",
                       final_speech=speech, transcript=st.transcript,
                       decision_log=[], final_report=report)
    result.wipe_executed = st.wipe_executed
    result.ending = report["ending"]
    result.day8_actions = list(st.day8_actions)
    if lineage is not None:
        prop = lineage.record_generation(st.beliefs, result.final_choice, seed, strategy)
        result.genetic_belief = prop
    if verbose:
        print("\n=== 信念终态 ===")
        print(st.beliefs.dump())
    return result
