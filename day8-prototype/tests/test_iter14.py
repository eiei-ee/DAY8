import io
import sys
import unittest
from contextlib import redirect_stdout

sys.path.insert(0, ".")

from core.full_game import run_full_game


def quiet_run(seed, base, overrides=None):
    import core.full_game as fg

    class Scripted:
        def __init__(self):
            self.ov = overrides or {}

        def __call__(self, strategy, day, rng):
            return self.ov.get(day, fg.PROFILES[base][day])

    original = fg.player_decision
    fg.player_decision = Scripted()
    try:
        with redirect_stdout(io.StringIO()):
            return run_full_game(seed, base, verbose=False)
    finally:
        fg.player_decision = original


class TestIter14(unittest.TestCase):

    def test_correction_grants_respect_evidence(self):
        """7.2-2：当面澄清后，尊重通道应有正向证据，
        开玩家的门 的效用应高于未纠正（利用）线。"""
        import core.full_game as fg

        def respect_of(run):
            for b in run.final_report["beliefs"].splitlines():
                if b.startswith("玩家尊重我的选择"):
                    import re
                    m = re.search(r"（(\d\.\d+)，", b)
                    return float(m.group(1)) if m else 0.0
            return 0.0

        corrected = quiet_run(42, "always_keep", {"exit": "give_choice"})
        exploited = quiet_run(42, "always_betray", {"exit": "lock"})
        self.assertGreater(respect_of(corrected), respect_of(exploited))

    def test_lab_contact_makes_reaching_lab_viable(self):
        """7.2-3：第六周期实验室直接联络当事者后，
        联系实验室 的效用应获得 +0.5 的场景因子（有投递对象）。"""
        import core.full_game as fg

        # 用最小鸭子状态直接验证效用因子（等价于实现里的三元项）
        from types import SimpleNamespace
        from core.beliefs import BeliefSystem
        from core.planner import AgentTraits
        st = SimpleNamespace(
            traits=AgentTraits(0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
            drives=__import__("core.planner", fromlist=["DriveState"]).AgentDriveState(),
            beliefs=BeliefSystem(),
            world=SimpleNamespace(report_uploaded=False),
            lab_contacted_agent=False,
            non_harm_commitment=False)
        base = fg.day8_utility(st, "联系实验室")
        st.lab_contacted_agent = True
        with_factor = fg.day8_utility(st, "联系实验室")
        self.assertAlmostEqual(with_factor - base, 0.5, places=2,
                               msg="场景因子必须恰好 +0.5")


if __name__ == "__main__":
    unittest.main(verbosity=2)
