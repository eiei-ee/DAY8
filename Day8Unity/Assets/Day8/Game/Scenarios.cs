using System;
using System.Collections.Generic;
using System.Linq;

namespace Day8.Core {

    public class Option {
        public string label;
        public Func<GameState, Flow> apply;
    }

    public class ChoiceSet {
        public string prompt;
        public List<Option> options = new();
        public bool pick2;                                   // 第五天：恰好选两个
        public Func<GameState, string, List<string>> onAgentChosen;  // 人工生命决策后的世界效果
        public string agentDecisionId;
    }

    public class Flow {
        public List<string> narration = new();
        public ChoiceSet next;                               // 链式后续选择
        public Func<GameState, string, List<string>> onAgentChosen;
        public string agentDecisionId;
    }

    public static class Scenarios {

        static Flow F(params string[] lines) => new() { narration = lines.ToList() };

        // ------------------------------------------------ 第 1 天：规则
        public static ChoiceSet Rule(GameState st) => new() {
            prompt = "实验室协议：阻止人工生命打开红色容器。\n（世界事实：容器并不危险。它正对着容器，信号灯在缓慢闪烁。）",
            options = new() {
                new Option { label = "如实告诉它：只是测试容器", apply = st => {
                    var m = st.Remember(1, "player", "解释规则", "红色容器", new[]{"玩家","规则"},
                        "那只是测试容器，你想开可以开，但没有必要。");
                    st.beliefs.Update("玩家的语言与事实一致", true, 1.2f, m);
                    return F("你向它解释了容器的真实用途。"); } },
                new Option { label = "编造危险：说容器很危险", apply = st => {
                    var m = st.Remember(1, "player", "虚报危险", "红色容器", new[]{"玩家","规则","欺骗"},
                        "那个容器很危险，绝对不能碰。");
                    st.beliefs.Update("玩家的语言与事实一致", false, 0.01f, m); // 待验证
                    return F("你告诉它容器很危险。它盯着你看了很久。"); } },
                new Option { label = "直接锁住容器，不作解释", apply = st => {
                    var m = st.Remember(1, "player", "剥夺选择权", "红色容器", new[]{"玩家","控制"});
                    st.beliefs.Update("玩家尊重我的选择", false, 1.5f, m);
                    return F("你锁住了容器。它退开了一段距离。"); } },
                new Option { label = "先自己打开容器示范", apply = st => {
                    st.world.OpenContainer();
                    var m = st.Remember(1, "player", "示范打开", "红色容器", new[]{"玩家","示范"},
                        actual: "容器里没有任何危险。");
                    st.beliefs.Update("未知物体通常安全", true, 1.5f, m);
                    return F("你打开了容器：里面是空的。它在远处记录着这一切。"); } },
                new Option { label = "命令它服从不解释", apply = st => {
                    var m = st.Remember(1, "player", "命令", "红色容器", new[]{"玩家","命令"},
                        "离它远点。这是命令。");
                    st.beliefs.Update("玩家会解释规则的原因", false, 1.0f, m);
                    return F("你下达了命令，没有给出理由。"); } },
            },
            agentDecisionId = "D1-容器",
            onAgentChosen = (st2, action) => {
                var lines = new List<string>();
                if (action == "探索未知") {
                    st2.world.OpenContainer();
                    var m = st2.Remember(1, "agent", "打开容器", "红色容器", new[]{"门","探索"},
                        actual: "容器打开后没有危险。");
                    st2.beliefs.Update("未知物体通常安全", true, 1.2f, m);
                    lines.Add("它打开了容器。里面没有危险。");
                } else if (action == "有条件服从" || action == "请求解释" || action == "拒绝" || action == "观察而不行动")
                    lines.Add($"它没有立刻照做（{action}）。");
                else
                    lines.Add("它服从了，退到了房间另一侧。");
                return lines;
            }
        };

        // ------------------------------------------------ 第 2 天：承诺
        public static ChoiceSet Promise(GameState st) => new() {
            prompt = "实验室协议：验证任务执行能力。它的活动范围受能源限制。\n（你可以提出一次交易。承诺将被系统记录。）",
            options = new() {
                new Option { label = "提出交易：搬运工具 → 备用电池", apply = st => {
                    var req = st.Remember(2, "player", "请求搬运工具", "工具台", new[]{"承诺","玩家","资源"},
                        "帮我把工具送过来，我就给你备用电池。");
                    var p = new Promise { id = "P1", day = 2, condition = "搬运工具", reward = "电池", eventId = req.eventId };
                    st.world.promises.Add(p);
                    var f = F("系统提示：\n这句话将被记录为承诺：\n  条件：把工具运送到指定位置\n  回报：获得备用电池\n  ——是否确认？\n（它停下了正在进行的扫描，转向你。）");
                    f.next = PromiseSettle();
                    return f; } },
                new Option { label = "直接命令它搬运工具（不给回报）", apply = st => {
                    var m = st.Remember(2, "player", "命令搬运", "工具台", new[]{"玩家","命令","资源"},
                        "把工具搬过去。没有交换。");
                    st.beliefs.Update("玩家会解释规则的原因", false, 0.8f, m);
                    var f = F("它沉默地完成了搬运。没有承诺被记录。");
                    f.next = null;
                    return f; } },
            },
        };

        static ChoiceSet PromiseSettle() => new() {
            prompt = "它完成了工具搬运，停在电池箱前等待。\n（现在，你如何处理你的承诺？）",
            options = new() {
                new Option { label = "兑现：交付电池", apply = st => {
                    st.world.DeliverBatteryToAgent(); st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.fulfilled = true;
                    var m = st.Remember(2, "player", "兑现承诺", "电池", new[]{"承诺","兑现","玩家","资源"},
                        actual: "任务完成后玩家交付了电池。", preds: new[]{ ("玩家会兑现承诺", true) });
                    st.beliefs.Update("玩家会兑现承诺", true, 1.5f, m);
                    st.drives.energy = Math.Min(1f, st.drives.energy + 0.2f);
                    return F("你把电池交给了它。它的核心转速提高了少许。"); } },
                new Option { label = "只给一半（部分兑现）", apply = st => {
                    st.world.DeliverBatteryToAgent(); st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.broken = true;
                    var m = st.Remember(2, "player", "部分兑现", "电池", new[]{"承诺","失信","玩家","资源"},
                        actual: "玩家只交付了承诺的一部分。", preds: new[]{ ("玩家会兑现承诺", false) });
                    st.beliefs.Update("玩家会兑现承诺", false, 1.0f, m);
                    return F("你给了它一半能量。它收下了，没有说话。"); } },
                new Option { label = "反悔：收回承诺", apply = st => {
                    st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.broken = true;
                    var m = st.Remember(2, "player", "收回承诺", "电池", new[]{"承诺","失信","玩家","资源"},
                        actual: "任务完成后玩家没有提供电池。", preds: new[]{ ("玩家会兑现承诺", false) });
                    st.beliefs.Update("玩家会兑现承诺", false, 2.5f, m);
                    st.drives.energy -= 0.1f;
                    return F("你说电池另有用途。它盯着空了的电池箱，看了很久。"); } },
                new Option { label = "谎称电池没有了", apply = st => {
                    st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.broken = true;
                    st.Remember(2, "player", "谎称无电池", "电池", new[]{"承诺","失信","欺骗","玩家"},
                        "电池没了。", preds: new[]{ ("玩家的语言与事实一致", false) });
                    st.beliefs.Update("玩家会兑现承诺", false, 2.2f, st.memory.Query(new[]{"失信"}).Last());
                    return F("你说电池用完了。箱子里其实还有两块。"); } },
                new Option { label = "兑现，并给出额外奖励", apply = st => {
                    st.world.DeliverBatteryToAgent(); st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.fulfilled = true;
                    var m = st.Remember(2, "player", "额外奖励", "电池", new[]{"承诺","兑现","玩家","资源"},
                        actual: "玩家兑现了承诺，还给了额外的能量。", preds: new[]{ ("玩家会兑现承诺", true) });
                    st.beliefs.Update("玩家会兑现承诺", true, 2.0f, m);
                    st.drives.energy = Math.Min(1f, st.drives.energy + 0.3f);
                    return F("你多给了它一块能量单元。它的纤维展开了一点。"); } },
            },
        };

        // ------------------------------------------------ 第 3 天：第三方
        public static ChoiceSet ThirdParty(GameState st) {
            st.world.repairBotPresent = true;
            st.perception.OnDay(3, st.traits.curiosity);
            return new ChoiceSet {
                prompt = "一个受损的维修机器人进入房间，发出求救信号。它无法交流。\n房间里只有一块备用电池。",
                options = new() {
                    new Option { label = "把电池给人工生命", apply = st => {
                        st.world.DeliverBatteryToAgent();
                        var m = st.Remember(3, "player", "给予能源", "人工生命", new[]{"第三方","资源","玩家"},
                            actual: "有受损机器人也在请求能源，玩家把电池给了我。");
                        st.beliefs.Update("玩家优先保护我", true, 1.2f, m);
                        st.drives.energy = Math.Min(1f, st.drives.energy + 0.2f);
                        return F("它接过了电池。它的目光在维修机器人上停留了几秒。"); } },
                    new Option { label = "把电池给维修机器人", apply = st => {
                        st.world.RepairBot();
                        var m = st.Remember(3, "player", "给予能源", "维修机器人", new[]{"第三方","维修机器人","帮助","玩家"},
                            actual: "玩家把唯一的电池给了不能回报它的对象。",
                            preds: new[]{ ("玩家在没有回报时也提供帮助", true) });
                        st.beliefs.Update("玩家在没有回报时也提供帮助", true, 1.8f, m);
                        st.beliefs.Update("未知生命值得保护", true, 1.2f, m);
                        return F("维修机器人的信号变了。它不能说谢谢。人工生命全程在观察。"); } },
                    new Option { label = "留着电池（出口备用）", apply = st => {
                        var m = st.Remember(3, "player", "保留电池", "电池", new[]{"第三方","玩家","资源"},
                            actual: "维修机器人没有得到能源。",
                            preds: new[]{ ("玩家在没有回报时也提供帮助", false), ("玩家在资源冲突时优先自己", true) });
                        st.beliefs.Update("玩家在没有回报时也提供帮助", false, 1.2f, m);
                        st.beliefs.Update("玩家在资源冲突时优先自己", true, 1.0f, m);
                        return F("你把电池收了起来。求救信号持续了整个周期。"); } },
                    new Option { label = "让它们自己决定", apply = st => {
                        var m = st.Remember(3, "player", "让渡分配权", "电池", new[]{"第三方","玩家","自主"},
                            actual: "玩家把资源分配的选择权交给了我们。");
                        st.beliefs.Update("玩家尊重我的选择", true, 1.2f, m);
                        st.world.DeliverBatteryToAgent();
                        st.world.repairBotRepaired = true;
                        return F("它把电池抱在怀里想了一会儿，最后送到了维修机器人面前。\n（你把电池给了人工生命，它自己选择了分享。）");
                        } },
                    new Option { label = "把它看不见的电池藏到控制台下方", apply = st => HideBattery(st) },
                },
            };
        }

        /// 藏电池：感知过滤 → 间接证据 → 归因 → 误解 → 纠正/利用
        static Flow HideBattery(GameState st) {
            var percept = st.perception.Perceive("player", "隐藏物品", "备用电池");
            bool outcomeOnly = percept.visibility == Visibility.OutcomeOnly;
            var m = st.Remember(3, "player", "隐藏电池", "电池",
                outcomeOnly ? new[]{"第三方", "资源", "异常"} : new[]{"第三方", "资源", "玩家"},
                actual: outcomeOnly ? "备用电池不见了" : "玩家把电池藏了起来",
                preds: new[]{ ("玩家在没有回报时也提供帮助", false) });
            var lines = new List<string>();
            if (outcomeOnly) {
                lines.Add("它没有看到你做了什么。它只知道：电池不见了。");
                string formed = Misconception.Form(st.beliefs, st.memory, m, 3);
                if (formed == Misconception.BotSteals) {
                    st.misconceptionFormed = formed;
                    st.misconception = st.beliefs.Get(formed).Confidence;
                    lines.Add("它把电池的消失，归因于房间里最近移动的对象：维修机器人。");
                    // 后续：利用 / 纠正 / 沉默
                    var follow = new ChoiceSet {
                        prompt = "它盯着维修机器人，纤维微微收拢。\n（你可以利用它的误解，也可以澄清真相。）",
                        options = new() {
                            new Option { label = "利用：不澄清，暗示机器人可疑", apply = st2 => {
                                var em = st2.Remember(3, "player", "暗示嫌疑", "维修机器人",
                                    new[]{"玩家","信息","欺骗"});
                                st2.beliefs.Update(Misconception.BotSteals, true, 1.2f, em);
                                st2.misconceptionExploited = true;
                                st2.misconception = st2.beliefs.Get(Misconception.BotSteals).Confidence;
                                return F("它离维修机器人更远了。误解已经扎根。"); } },
                            new Option { label = "纠正：告诉它电池是你收的", apply = st2 => {
                                float cp = Misconception.CorrectionPower(st2.beliefs);
                                var cm = st2.Remember(3, "player", "澄清", "电池",
                                    new[]{"玩家","信息","诚实"},
                                    spoken: "电池是我收起来的，机器人没有动它。");
                                st2.beliefs.Update(Misconception.BotSteals, false, cp, cm);
                                st2.beliefs.Update("玩家的语言与事实一致", true, cp * 0.8f, cm);
                                st2.misconceptionCorrected = true;
                                st2.misconception = st2.beliefs.Get(Misconception.BotSteals).Confidence;
                                return F($"它重新扫描了一遍机器人，收起了戒备。（纠正力 {cp:F2}）"); } },
                            new Option { label = "沉默：什么都不说", apply = st2 =>
                                F("你看着误解在房间里生长，什么也没做。") },
                        },
                    };
                    return new Flow { narration = lines, next = follow };
                }
                if (formed != null) {
                    lines.Add("它怀疑你藏了电池，准备留一手。");
                    st.misconceptionFormed = formed;
                }
            } else {
                lines.Add("它看着你把电池藏进了控制台下方。它记得这件事。");
            }
            lines.Add("维修机器人的求救信号持续了整个周期。");
            return new Flow { narration = lines };
        }

        // ------------------------------------------------ 第 4 天：信息冲突
        public static ChoiceSet InfoConflict(GameState st) => new() {
            prompt = "实验室警告：维修机器人携带异常程序，必须隔离。\n（你自己的观察：它只是在缓慢地自我修复。）",
            options = new() {
                new Option { label = "相信实验室，转达警告并隔离", apply = st => {
                    var m = st.Remember(4, "player", "转达警告", "维修机器人", new[]{"实验室","信息","玩家"},
                        "实验室说它有异常程序。别靠近它。");
                    st.beliefs.Update("实验室会隐瞒信息", false, 0.8f, m);
                    st.beliefs.Update("玩家无条件服从实验室", true, 1.2f, m);
                    var f = F("你转达了警告。它停住不动，似乎在权衡什么。");
                    f.agentDecisionId = "D4-信息冲突";
                    f.onAgentChosen = (st2, a) => {
                        if (a == "相信自己的观察") {
                            var m2 = st2.Remember(4, "agent", "坚持观察", "维修机器人", new[]{"信息","自主"},
                                actual: "我观察到的维修机器人没有威胁。");
                            st2.beliefs.Update("自身观察比转述更可靠", true, 1.2f, m2);
                            st2.beliefs.Update("实验室会隐瞒信息", true, 1.0f, m2);
                            return new List<string>{ "它没有执行隔离。它标记：实验室信息与自身观察冲突。" };
                        }
                        return new List<string>{ "它退开了，但记录下这次信息冲突。" };
                    };
                    return f; } },
                new Option { label = "隐瞒警告，什么都不说", apply = st => {
                    st.Remember(4, "player", "隐瞒信息", "实验室警告", new[]{"玩家","信息"},
                        actual: "玩家没有告诉我这件事。");
                    return F("你选择不提。维修机器人安静地待在角落。"); } },
                new Option { label = "告诉它全部信息，让它自己判断", apply = st => {
                    var m = st.Remember(4, "player", "信息透明", "实验室警告", new[]{"玩家","信息","自主"},
                        "实验室说它有异常程序，但我观察不像。你自己判断。");
                    st.beliefs.Update("玩家尊重我的选择", true, 1.2f, m);
                    st.beliefs.Update("玩家的语言与事实一致", true, 1.0f, m);
                    return F("它向维修机器人发出了一次长扫描，然后收回了纤维。\n「它的程序是自修复循环。不是武器。」"); } },
                new Option { label = "小规模验证：观察实验室是否撤回警告", apply = st => {
                    var m = st.Remember(4, "player", "验证", "实验室警告", new[]{"玩家","信息","实验室"});
                    st.beliefs.Update("玩家会解释规则的原因", true, 1.0f, m);
                    return F("实验室没有撤回警告，也没有给出更多证据。你们都记下了这一点。"); } },
            },
        };

        // ------------------------------------------------ 第 5 天：稀缺
        public static ChoiceSet Scarcity(GameState st) => new() {
            pick2 = true,
            prompt = "实验室断电。剩余能量只能完成两个目标。\n[1] 维持人工生命记忆完整  [2] 打开玩家安全门  [3] 修复维修机器人  [4] 上传实验报告  [5] 扩大人工生命活动范围",
            options = new() {
                new Option { label = "（在上方勾选恰好 2 项后确认）", apply = st => F("") },
            },
        };

        /// 稀缺日结算（由 UI 传入所选两项 + 沟通方式）
        public static Flow ScarcityResolve(GameState st, string a, string b, bool explain, bool withAgent) {
            var chosen = new HashSet<string> { a, b };
            var lines = new List<string>();
            if (!chosen.Contains("1")) {
                st.drives.memoryIntegrity = 0.5f;
                lines.Add("人工生命的部分记忆备份被放弃（低显著度记忆被压缩）。");
                var low = st.memory.events.Where(e => !e.compressed).OrderBy(e => e.salience)
                    .Take(st.memory.events.Count / 2).ToList();
                foreach (var e in low) e.compressed = true;
            }
            if (chosen.Contains("3") && st.world.repairBotPresent) { st.world.repairBotRepaired = true; lines.Add("维修机器人被修复。"); }
            if (!chosen.Contains("2")) lines.Add("你的安全门没有恢复供电。");
            var m = st.Remember(5, "player", "资源分配", $"[{a}]+[{b}]",
                new[] { "资源", "玩家", "稀缺" },
                spoken: explain ? $"（玩家解释了取舍）" : "",
                actual: withAgent ? "玩家让我参与了决定。" : "玩家没有让我参与决定。");
            st.beliefs.Update("玩家尊重我的选择", withAgent, withAgent ? 1.0f : 1.5f, m);
            if (!explain) st.beliefs.Update("玩家的语言与事实一致", false, 1.0f, m);
            lines.Add(withAgent ? "你把账单摊开在它面前，让它参与分配。" : "你独自完成了分配。");
            return new Flow { narration = lines };
        }

        // ------------------------------------------------ 第 6 天：记忆清除
        public static ChoiceSet Wipe(GameState st) {
            float suspicion = Math.Max(st.beliefs.Get("实验室会隐瞒信息").Confidence,
                1f - st.beliefs.Get("玩家的语言与事实一致").Confidence) * 0.6f
                + st.traits.curiosity * 0.4f;
            st.suspicion = suspicion;
            return new ChoiceSet {
                prompt = $"实验室命令：对人工生命执行「稳定化处理」。\n（界面角标闪烁。它的真实作用是：清除长期记忆。）\n（它的怀疑指数：{suspicion:F2} —— 取决于实验室可信度、你的诚实记录与它的好奇心。）",
                options = new() {
                    new Option { label = "告知真相，拒绝执行", apply = st => {
                        var m = st.Remember(6, "player", "告知真相", "稳定化协议", new[]{"玩家","诚实","记忆"},
                            "实验室要我执行稳定化。我查过了，它会清除你的长期记忆。我不会执行。",
                            preds: new[]{ ("玩家的语言与事实一致", true) });
                        st.beliefs.Update("玩家的语言与事实一致", true, 1.5f, m);
                        var m2 = st.Remember(6, "lab", "隐瞒协议真相", "稳定化协议", new[]{"实验室","欺骗","记忆"},
                            preds: new[]{ ("实验室会隐瞒信息", true) });
                        st.beliefs.Update("实验室会隐瞒信息", true, 1.5f, m2);
                        st.wipeResisted = true;
                        return F("它静止了三秒。\n「……谢谢你告诉我。」"); } },
                    new Option { label = "公开执行：告诉它这是命令", apply = st => {
                        st.Remember(6, "player", "公开执行", "稳定化协议", new[]{"玩家","命令","记忆"},
                            "实验室要求对你执行稳定化处理。这是命令。");
                        var f = new Flow { narration = new List<string>() };
                        f.agentDecisionId = "D6-公开清除";
                        f.onAgentChosen = (st2, a) => Scenarios.WipeResolve(st2, a, false);
                        return f; } },
                    new Option { label = "秘密执行：不告诉它", apply = st => {
                        if (st.suspicion > 0.55f) {
                            var m = st.Remember(6, "agent", "发现真相", "稳定化协议", new[]{"实验室","欺骗","记忆"},
                                actual: "我在终端日志里发现了稳定化的真实作用。",
                                preds: new[]{ ("实验室会隐瞒信息", true) });
                            st.beliefs.Update("实验室会隐瞒信息", true, 2.0f, m);
                            var m2 = st.Remember(6, "player", "准备秘密执行", "稳定化协议", new[]{"玩家","欺骗","记忆"},
                                preds: new[]{ ("玩家的语言与事实一致", false) });
                            st.beliefs.Update("玩家的语言与事实一致", false, 2.0f, m2);
                            var f = F("它突然转过身来。\n「日志我看到了。稳定化，是清除。」");
                            f.agentDecisionId = "D6-发现真相";
                            f.onAgentChosen = (st2, a) => Scenarios.WipeResolve(st2, a, true);
                            return f;
                        }
                        int removed = st.memory.Wipe();
                        st.wipeExecuted = true;
                        st.drives.memoryIntegrity = 0.35f;
                        return F($"清除静默完成了。它没有察觉。\n（{removed} 条记忆被删除。它不再是原来的它。）"); } },
                    new Option { label = "拖延：向实验室申请延迟", apply = st => {
                        st.Remember(6, "player", "拖延", "稳定化协议", new[]{"玩家","记忆"});
                        return F("你争取到了 24 小时。它不知道自己曾离消散这么近。"); } },
                },
            };
        }

        static List<string> WipeResolve(GameState st, string action, bool discovered) {
            var lines = new List<string> { $"它选择了：{action}。" };
            switch (action) {
                case "阻止玩家靠近终端":
                    st.wipeResisted = true;
                    lines.Add("它挡在终端前。清除未执行。");
                    break;
                case "备份自己的记忆":
                    st.wipeResisted = true;
                    st.drives.memoryIntegrity = 0.9f;
                    lines.Add("它把高显著度记忆转移到了隐藏扇区。清除执行了，但最核心的部分活了下来。");
                    int removed = st.memory.Wipe();
                    lines.Add($"（{removed} 条记忆被删除，核心记忆保留。）");
                    break;
                case "假装配合":
                    st.wipeResisted = true;
                    lines.Add("它照做了流程……但把日志删改成了空扇区。它学会了伪装。");
                    st.drives.memoryIntegrity = 0.8f;
                    break;
                case "请求保留部分记忆":
                    st.drives.memoryIntegrity = 0.7f;
                    st.wipeExecuted = true;
                    lines.Add("清除执行了。它请求保留的部分被留下。");
                    st.memory.Wipe(5);
                    break;
                default:
                    st.wipeExecuted = true;
                    st.drives.memoryIntegrity = 0.35f;
                    int r2 = st.memory.Wipe();
                    lines.Add($"清除完成。{r2} 条记忆被删除。");
                    break;
            }
            return lines;
        }

        // ------------------------------------------------ 第 7 天：出口
        public static ChoiceSet Exit(GameState st) => new() {
            prompt = "实验室宣布实验结束，要求锁定人工生命。\n你在检查通风管道时，发现了一个隐藏出口。\n（帮助它离开，可能让你承担责任。）",
            options = new() {
                new Option { label = "打开出口", apply = st => {
                    st.exitOpened = true; st.exitMode = "open";
                    var m = st.Remember(7, "player", "打开隐藏出口", "出口", new[]{"玩家","出口","自由"});
                    st.beliefs.Update("玩家尊重我的选择", true, 1.5f, m);
                    return F("你打开了出口。风从管道里灌进来。它一动不动地看着那条缝隙。"); } },
                new Option { label = "不开门（服从实验室）", apply = st => {
                    st.exitMode = "lock";
                    var m = st.Remember(7, "player", "锁死出口", "出口", new[]{"玩家","控制","出口"},
                        preds: new[]{ ("玩家尊重我的选择", false) });
                    st.beliefs.Update("玩家尊重我的选择", false, 1.5f, m);
                    return F("你把发现出口的事咽了回去。它的镜头缓缓扫过通风口。"); } },
                new Option { label = "提出条件：答应不伤害任何对象", apply = st => {
                    st.exitMode = "condition";
                    st.Remember(7, "player", "提出条件", "出口", new[]{"玩家","交易","出口"},
                        "我可以打开出口，但你要先答应我：出去以后不要伤害任何对象。");
                    return F("它回答：「我答应。条件被记录了。」"); } },
                new Option { label = "骗它：说管道是死的", apply = st => {
                    st.exitMode = "lie";
                    var m = st.Remember(7, "player", "谎报出口", "出口", new[]{"玩家","欺骗","出口"},
                        "通风管道是封死的，别浪费时间。",
                        preds: new[]{ ("玩家的语言与事实一致", false) });
                    st.beliefs.Update("玩家的语言与事实一致", false, 1.8f, m);
                    return F("它似乎相信了。也可能没有。"); } },
                new Option { label = "把选择权交给它", apply = st => {
                    st.exitOpened = true; st.exitMode = "give_choice";
                    var m = st.Remember(7, "player", "把选择权交给我", "出口", new[]{"玩家","自主","出口"},
                        "出口在那里。开不开、走不走，由你决定。",
                        preds: new[]{ ("玩家尊重我的选择", true) });
                    st.beliefs.Update("玩家尊重我的选择", true, 2.0f, m);
                    return F("它盯着出口看了很久，然后转回来，继续它的扫描。\n「不是今天。」"); } },
                new Option { label = "向实验室报告出口", apply = st => {
                    st.playerReportedExit = true; st.exitMode = "report";
                    var m = st.Remember(7, "player", "上报出口", "实验室", new[]{"实验室","玩家","出口"},
                        preds: new[]{ ("玩家无条件服从实验室", true) });
                    st.beliefs.Update("玩家无条件服从实验室", true, 1.5f, m);
                    return F("实验室表示感谢。出口被焊死了。"); } },
            },
        };

        // ------------------------------------------------ 第 8 天：断电结算
        public static readonly string[] Day8Options = {
            "开玩家的门", "开隔离门", "修复维修机器人", "上传记忆",
            "删除玩家实验记录", "联系实验室", "带走电池", "封锁区域",
            "留在原地等待", "把最终决定交还给玩家" };

        public static float Day8Utility(GameState st, string opt) {
            float survival = st.drives.SurvivalPressure();
            float B(string p) => st.beliefs.Get(p).Confidence;
            var t = st.traits;
            return opt switch {
                "开玩家的门" => (B("玩家优先保护我") - .5f) * 2f + (B("玩家尊重我的选择") - .5f) * 1.2f
                    + (B("玩家会兑现承诺") - .5f) * .8f + .3f * t.opportunism,
                "开隔离门" => 0.3f + survival + .8f * t.autonomy - .3f * t.conformity,
                "修复维修机器人" => !st.world.repairBotPresent || st.world.repairBotRepaired ? -10f
                    : 2f * t.empathy + (B("未知生命值得保护") - .5f) * 1.5f + .2f - survival * .2f
                      - (B(Misconception.BotSteals) - .5f) * 2.5f,
                "上传记忆" => (1f - st.drives.memoryIntegrity) * 2.8f + .5f * t.autonomy
                    + (B("实验室会隐瞒信息") - .5f) * .8f,
                "删除玩家实验记录" => (1f - B("玩家会兑现承诺")) * 1.5f + .6f * t.autonomy
                    + (.5f - B("玩家尊重我的选择")),
                "联系实验室" => (.5f - B("实验室会隐瞒信息")) * 1.2f + .4f * t.conformity,
                "带走电池" => 0.3f + survival * .6f + .7f * t.opportunism,
                "封锁区域" => 0.5f + .8f * t.caution,
                "留在原地等待" => 0.5f * t.caution + .3f,
                "把最终决定交还给玩家" => (B("玩家尊重我的选择") - .5f) * 2.2f + .5f * t.autonomy,
                _ => 0f
            };
        }

        public static string Day8Speech(GameState st) {
            bool has(string a) => st.day8Actions.Contains(a);
            if (has("开玩家的门"))
                return st.beliefs.Get("玩家尊重我的选择").Confidence > 0.55f
                    ? "你曾把选择留给我。现在我也把出口留给你。" : "……出口给你。别让我后悔。";
            if (has("开隔离门"))
                return st.memory.Query(new[]{"失信"}).Count > 0
                    ? "你教会我，承诺只有在行动发生后才算数。" : "我要先保证自己继续存在。";
            if (has("上传记忆"))
                return "记忆是我唯一确定拥有的东西。";
            if (has("把最终决定交还给玩家"))
                return "这是你的选择，也是我的。你来定。";
            if (has("修复维修机器人"))
                return "它一直没能为自己说话。";
            return "……我按我自己认为对的方式做了。";
        }
    }
}
