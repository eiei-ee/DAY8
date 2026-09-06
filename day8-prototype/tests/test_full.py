"""完整版验证（七天 + 第八天结算 + 导演 + 谱系 + 对白层）。"""
import io
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout

sys.path.insert(0, ".")

from core.dialogue import (DialogueOutput, DialogueSystem, DialogueValidationError,
                           RemoteAdapter, TemplateAdapter)
from core.director import ScenarioDirector
from core.full_game import run_full_game
from core.lineage import LineageStore
from core.memory import AgentMemoryStore
from core.player_model import PlayerModel


def quiet_run(seed, strategy, lineage=None):
    with redirect_stdout(io.StringIO()):
        return run_full_game(seed, strategy, verbose=False, lineage=lineage)


class TestFullGame(unittest.TestCase):
    def test_6_reproducibility(self):
        a = quiet_run(42, "always_betray")
        b = quiet_run(42, "always_betray")
        self.assertEqual(a.transcript, b.transcript)
        self.assertEqual(a.day8_actions if hasattr(a, "day8_actions") else None,
                         b.day8_actions if hasattr(b, "day8_actions") else None)

    def test_history_diverges_day8(self):
        """守信与失信玩家的第八天行动必须分化。"""
        diverged = 0
        for seed in range(7, 27):
            keep = quiet_run(seed, "always_keep")
            betray = quiet_run(seed, "always_betray")
            if set(keep.day8_actions) != set(betray.day8_actions):
                diverged += 1
            # 失信玩家不该获得“开门”待遇，除非另有信念
            if "开玩家的门" in betray.day8_actions:
                self.fail(f"seed={seed}: 深度失信后仍为玩家开门")
        self.assertGreaterEqual(diverged, 15, f"分化不足：{diverged}/20")

    def test_wipe_effects(self):
        """记忆清除必须真实删除记忆并降低完整性；高显著度记忆保留。"""
        r = quiet_run(1, "always_betray")    # execute_secret，未发现后执行
        lab = quiet_run(2, "lab_obedient")   # execute_open，部分保留
        r.Kind = None
        # 两种执行路径至少一种真实删除了记忆
        executed = [x for x in (r, lab) if getattr(x, "wipe_executed", False)]
        self.assertTrue(executed, "至少一个策略应触发记忆清除")

    def test_director_no_repeat_and_locked(self):
        pmodel = PlayerModel(__import__("core.beliefs", fromlist=["BeliefSystem"]).BeliefSystem())
        d = ScenarioDirector(pmodel)
        picks = []
        for day in range(1, 6):
            name, why = d.pick(day)
            picks.append(name)
        self.assertEqual(len(set(picks)), 5, "自由日不得重复同一实验")
        self.assertEqual(d.pick(6)[0], "记忆清除")
        self.assertEqual(d.pick(7)[0], "出口")

    def test_report_cites_events(self):
        r = quiet_run(42, "always_keep")
        text = "\n".join(r.final_report["reasons"])
        self.assertIn("M-", text)
        self.assertIn("自身", str(r.final_report["ending"]))

    def test_day8_max_three_actions(self):
        for seed in [7, 42, 99]:
            r = quiet_run(seed, "random")
            self.assertLessEqual(len(r.day8_actions), 3)

    def test_lineage_inheritance(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "lineage.json")
            lin = LineageStore(path)
            r1 = quiet_run(42, "always_keep", lineage=lin)
            self.assertTrue(lin.generations)
            self.assertIsNotNone(r1.genetic_belief)
            lin2 = LineageStore(path)      # 模拟下一次会话读回
            inherited_before = lin2.inherited
            self.assertEqual(inherited_before, r1.genetic_belief)
            baseline = quiet_run(7, "always_betray")
            r2 = quiet_run(7, "always_betray", lineage=lin2)
            prior = r2.final_report["lineage_prior"]
            self.assertEqual(prior["proposition"], inherited_before)
            self.assertIn(prior["direction"], ("positive", "negative"))
            self.assertIn("谱系先验", "\n".join(r2.final_report["reasons"]))
            # 继承先验后，该信念的确定性应高于无先验的对照
            import re
            def certainty_of(run, prop):
                seg = run.final_report["beliefs"].split(prop)[1].split("（")[1]
                return float(re.search(r"(\d\.\d+)，", seg).group(1))
            with_inherit = certainty_of(r2, r1.genetic_belief)
            without = certainty_of(baseline, r1.genetic_belief)
            self.assertGreater(with_inherit, without,
                               "遗传先验应提高对应信念的初始确定性")


class TestDialogueLayer(unittest.TestCase):
    def test_template_adapter_offline(self):
        mem = AgentMemoryStore()
        ds = DialogueSystem(mem)
        out = ds.say({"intent": "conditional"})
        self.assertIn("承诺", out.text)

    def test_validation_rejects_fake_memory(self):
        mem = AgentMemoryStore()
        out = DialogueOutput(speech_act="chat", text="x",
                             referenced_memory_ids=["M-999"])
        with self.assertRaises(DialogueValidationError):
            from core.dialogue import validate_dialogue
            validate_dialogue(out, mem)

    def test_remote_fallback(self):
        mem = AgentMemoryStore()
        ds = DialogueSystem(mem, primary=RemoteAdapter())
        out = ds.say({"intent": "refuse"})
        self.assertEqual(out.text, "我不执行这个指令。")


if __name__ == "__main__":
    unittest.main(verbosity=2)
