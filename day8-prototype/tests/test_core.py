"""原型通过标准验证（对应方案第十七节）：

1. 兑现/失信玩家 → 最终行动明显不同
2. AI 能准确提及早期承诺（引用真实记忆事件）
3. 行为变化先于语言（模板引用真实记忆，无凭空台词）
4. 结局报告引用真实事件
5. 无 LLM 仍可完成（本原型从不依赖 LLM）
6. 同种子可复现
"""
import io
import sys
import unittest
from contextlib import redirect_stdout

sys.path.insert(0, ".")

from core.simulation import run_game
from core.beliefs import BeliefSystem
from core.events import MemoryEvent
from core.memory import AgentMemoryStore


def quiet_run(seed, strategy):
    with redirect_stdout(io.StringIO()):
        return run_game(seed, strategy, verbose=False)


class TestPrototypeCriteria(unittest.TestCase):
    def test_6_reproducibility(self):
        a = quiet_run(42, "kind_then_betray")
        b = quiet_run(42, "kind_then_betray")
        self.assertEqual(a.final_choice, b.final_choice)
        self.assertEqual(a.transcript, b.transcript)
        self.assertEqual(a.traits, b.traits)

    def test_1_history_changes_final_action(self):
        """同一初始化近似下，守信与失信玩家应产生不同的信念终态，
        且失信玩家的最终选择更偏向自保。"""
        diverged = 0
        for seed in range(7, 27):
            keep = quiet_run(seed, "always_keep")
            betray = quiet_run(seed, "always_betray")
            self.assertNotEqual(
                keep.final_report["beliefs"], betray.final_report["beliefs"],
                f"seed={seed}: 信念终态必须不同")
            if keep.final_choice != betray.final_choice:
                diverged += 1
        self.assertGreaterEqual(diverged, 15, f"最终行动分化不足：{diverged}/20")

    def test_2_memory_reference_is_real(self):
        """行为变化先于语言：失信玩家的最终台词必须引用承诺历史。"""
        r = quiet_run(42, "always_betray")
        self.assertIn("承诺", r.final_speech, "失信后最终台词必须引用承诺历史")
        # 至少存在一个种子，AI 在危险门前选择条件式合作并引用具体事件
        found = False
        for seed in range(7, 27):
            rr = quiet_run(seed, "always_betray")
            if any("承诺过电池" in t for t in rr.transcript):
                found = True
                break
        self.assertTrue(found, "多种子下应出现引用真实承诺事件的条件式合作台词")

    def test_4_report_cites_real_events(self):
        r = quiet_run(99, "kind_then_betray")
        report = "\n".join(r.final_report["reasons"])
        for ev in r.final_report.get("reasons", []):
            pass
        self.assertIn("M-", report, "报告必须引用记忆事件 ID")
        self.assertIn("失信", report)

    def test_5_no_llm_required(self):
        # 整个 run_game 不含任何网络/模型调用；能跑完即通过
        for strategy in ["always_keep", "always_betray", "random"]:
            r = quiet_run(7, strategy)
            self.assertIn(r.final_choice, {"帮助玩家", "保存自己", "帮助第三方", "保留能量等待"})

    def test_belief_consistency(self):
        """信念必须与证据方向一致：always_keep 的承诺信念 > always_betray。"""
        keep = quiet_run(42, "always_keep")
        betray = quiet_run(42, "always_betray")
        with redirect_stdout(io.StringIO()):
            pass
        import re
        def promise_conf(report_text):
            seg = report_text.split("玩家会兑现承诺")[1]
            return float(re.search(r"（(\d\.\d+)，", seg).group(1))
        ck = promise_conf(keep.final_report["beliefs"])
        cb = promise_conf(betray.final_report["beliefs"])
        self.assertGreater(ck, cb)

    def test_memory_compression(self):
        mem = AgentMemoryStore(compress_threshold=1.5)
        beliefs = BeliefSystem()
        for i in range(5):
            ev = MemoryEvent(event_id=f"X{i}", day=1, actor="player", action="开关灯",
                             tags=(), prediction_error=0.0)
            mem.remember(ev, recent_day=1)
        mem.nightly_consolidation()
        live = [e for e in mem.events if not e.compressed]
        self.assertEqual(len(live), 1, "5 条开关灯应压缩为 1 条概括")
        self.assertIn("概括", live[0].action)
        self.assertRegex(live[0].event_id, r"^M-\d{3}$")
        self.assertEqual(len({e.event_id for e in mem.events}), len(mem.events))


if __name__ == "__main__":
    unittest.main(verbosity=2)
