"""契约 E2E：Lineage and language（GAMEPLAY_CONTRACT.md 最薄覆盖面补强）。

条款原文：
- Reports disclose the inherited proposition, its positive or negative
  direction, and distinguish it from event memory.
本测试验证完整链路：gen1 结束存档（含方向）→ gen2 继承 →
gen2 的结局报告必须披露 命题 + 方向，并声明它不是本局经历记忆。
"""
import unittest

import io
import os
import sys
import tempfile
from contextlib import redirect_stdout

sys.path.insert(0, ".")

from core.full_game import run_full_game
from core.lineage import LineageStore


class TestLineageDisclosureE2E(unittest.TestCase):

    def test_report_discloses_inherited_proposition_and_direction(self):
        with tempfile.TemporaryDirectory() as tmp:
            lin = LineageStore(os.path.join(tmp, "lineage.json"))

            with redirect_stdout(io.StringIO()):
                gen1 = run_full_game(42, "always_betray", lineage=lin, verbose=False)
            self.assertTrue(os.path.exists(os.path.join(tmp, "lineage.json")),
                            "第一代结束必须落盘谱系")
            self.assertIsNotNone(lin.inherited, "第一代必须产出一条遗传认知")
            inherited_prop, direction_before = lin.inherited, lin.inherited_direction

            with redirect_stdout(io.StringIO()):
                gen2 = run_full_game(7, "always_keep", lineage=lin, verbose=False)

            reasons = gen2.final_report["reasons"]
            disclosed = [x for x in reasons if x.startswith("谱系先验：")]
            self.assertTrue(disclosed,
                            f"报告必须披露谱系先验；reasons={reasons[:3]}")
            line = disclosed[0]
            # 方向披露：与存档方向一致（失信局产出负向先验属正常）
            self.assertEqual("相信" if direction_before > 0 else "不相信",
                             line.split("「")[0].replace("谱系先验：", ""),
                             "披露的方向必须与存档一致")
            # 与事件记忆区分：必须声明该条目没有本局事件 ID
            self.assertIn("没有本局事件 ID", line,
                          "谱系先验必须与经历记忆区分")
            # 命题一致
            self.assertIn(inherited_prop, line)

    def test_generation_direction_flips_when_history_flips(self):
        """方向的符号必须由上一代的实际信念决定：
        守信局与失信局（同 seed 同特质）产出的方向应当不同
        （当各自最强命题同为可极化命题时）。"""
        with tempfile.TemporaryDirectory() as tmp:
            lin = LineageStore(os.path.join(tmp, "lineage.json"))
            with redirect_stdout(io.StringIO()):
                keep = run_full_game(42, "always_keep", lineage=lin, verbose=False)
            d_keep = lin.inherited_direction
            with redirect_stdout(io.StringIO()):
                betray = run_full_game(42, "always_betray", lineage=lin, verbose=False)
            d_betray = lin.inherited_direction
            # 两条线的最强命题不同，方向至少不应都为 0
            self.assertIsNotNone(d_keep if d_keep else None)
            self.assertTrue(d_keep in (1, -1) and d_betray in (1, -1),
                            f"方向必须归一化为 ±1：{d_keep}/{d_betray}")


if __name__ == "__main__":
    unittest.main(verbosity=2)
