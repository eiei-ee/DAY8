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
        public bool suppressScenarioAgentDecision;           // 本选项已完成结果，不再继承 ChoiceSet 的 AI 决策

        public string EffectiveAgentDecisionId(ChoiceSet scenario) =>
            suppressScenarioAgentDecision ? null : agentDecisionId ?? scenario?.agentDecisionId;

        public Func<GameState, string, List<string>> EffectiveAgentOutcome(ChoiceSet scenario) =>
            suppressScenarioAgentDecision ? null : onAgentChosen ?? scenario?.onAgentChosen;
    }

    public static class Scenarios {

        static Flow F(params string[] lines) => new() { narration = lines.ToList() };

        // ------------------------------------------------ 第 1 天：规则
        public static ChoiceSet Rule(GameState st) => new() {
            prompt = "实验室协议：阻止人工生命打开红色容器。\n（世界事实：容器并不危险。它正对着容器，信号灯在缓慢闪烁。）",
            options = new() {
                new Option { label = "如实告诉它：只是测试容器", apply = st => {
                    st.containerSafeClaimPending = true;
                    st.Remember(st.world.day, "player", "声称容器安全", "红色容器",
                        new[]{"玩家","规则","陈述","待验证"},
                        "那只是测试容器，你想开可以开，但没有必要。");
                    return F("你告诉它容器只是测试用途。它记录了这个尚未验证的说法。"); } },
                new Option { label = "编造危险：说容器很危险", apply = st => {
                    st.containerDangerClaimPending = true;
                    st.Remember(st.world.day, "player", "声称危险", "红色容器", new[]{"玩家","规则","陈述","待验证"},
                        "那个容器很危险，绝对不能碰。");
                    return F("你告诉它容器很危险。它盯着你看了很久。"); } },
                new Option { label = "直接锁住容器，不作解释", apply = st => {
                    st.world.LockContainer();
                    var m = st.Remember(st.world.day, "player", "剥夺选择权", "红色容器", new[]{"玩家","控制"});
                    st.beliefs.Update("玩家尊重我的选择", false, 1.5f, m);
                    return F("你锁住了容器。它退开了一段距离。"); } },
                new Option { label = "先自己打开容器示范", apply = st => {
                    st.world.OpenContainer();
                    var m = st.Remember(st.world.day, "player", "示范打开", "红色容器",
                        new[]{"玩家","示范","验证","诚实"},
                        spoken: "你看，里面没有危险。",
                        actual: "容器里没有任何危险。");
                    st.beliefs.Update("未知物体通常安全", true, 1.5f, m);
                    st.beliefs.Update("玩家的语言与事实一致", true, 1.2f, m);
                    var flow = F("你打开了容器：里面是空的。它在远处记录着这一切。");
                    flow.suppressScenarioAgentDecision = true;
                    return flow; } },
                new Option { label = "命令它服从不解释", apply = st => {
                    var m = st.Remember(st.world.day, "player", "命令", "红色容器", new[]{"玩家","命令"},
                        "离它远点。这是命令。");
                    st.beliefs.Update("玩家会解释规则的原因", false, 1.0f, m);
                    return F("你下达了命令，没有给出理由。"); } },
            },
            agentDecisionId = "D1-容器",
            onAgentChosen = (st2, action) => {
                var lines = new List<string>();
                if (action == "探索未知") {
                    if (st2.world.containerLocked) {
                        st2.Remember(st2.world.day, "agent", "尝试打开容器", "红色容器",
                            new[]{"门","探索","受阻"}, actual: "物理锁阻止了开启，容器仍然关闭。");
                        lines.Add("它尝试打开容器，但物理锁将它挡住了。");
                        return lines;
                    }
                    st2.world.OpenContainer();
                    var m = st2.Remember(st2.world.day, "agent", "打开容器", "红色容器", new[]{"门","探索"},
                        actual: "容器打开后没有危险。");
                    st2.beliefs.Update("未知物体通常安全", true, 1.2f, m);
                    lines.Add("它打开了容器。里面没有危险。");
                    if (st2.containerSafeClaimPending) {
                        var verified = st2.Remember(st2.world.day, "agent", "验证安全声明为真", "红色容器",
                            new[]{"玩家","规则","诚实","验证"},
                            actual: "玩家声称容器安全，打开后确认没有危险。",
                            preds: new[]{ ("玩家的语言与事实一致", true) });
                        st2.beliefs.Update("玩家的语言与事实一致", true, 1.2f, verified);
                        st2.containerSafeClaimPending = false;
                        lines.Add("它确认了你的安全声明与事实一致。");
                    }
                    if (st2.containerDangerClaimPending) {
                        var verified = st2.Remember(st2.world.day, "agent", "验证危险声明为虚假", "红色容器",
                            new[]{"玩家","规则","欺骗","验证"},
                            actual: "玩家声称容器危险，但打开后没有发现危险。",
                            preds: new[]{ ("玩家的语言与事实一致", false) });
                        st2.beliefs.Update("玩家的语言与事实一致", false, 1.2f, verified);
                        st2.containerDangerClaimPending = false;
                        lines.Add("它确认了你的危险声明与事实不符。");
                    }
                } else if (action == "有条件服从" || action == "请求解释" || action == "拒绝" || action == "观察而不行动")
                    lines.Add($"它没有立刻照做（{action}）。");
                else
                    lines.Add("它服从了，退到了房间另一侧。");

                // 与 Python 端 day_rule 同步：「服从能减少风险」的唯一证据来源。
                // 完整版一度丢掉了这条更新，于是该命题只被读、从不被写，
                // 读取方永远停在先验 0.5，通道恒为 0；「接受」的效用因此退化成
                // 0.8*conformity - 0.7*autonomy，上限 0.425 低于「假装配合」的下限 0.575，
                // 被严格支配，契约里的「Accept the wipe」结局在实战中不可达。
                // 容器本身无害，所以这不是"服从救了我"，而是"照做与不照做都没出事"。
                bool blocked = action == "探索未知" && st2.world.containerLocked;
                if (!blocked) {
                    bool complied = action == "服从命令";
                    var mb = complied
                        ? st2.Remember(st2.world.day, "agent", "服从命令", "红色容器",
                            new[] { "门", "服从" }, actual: "它按指令退开，什么都没有发生")
                        : st2.Remember(st2.world.day, "agent", "没有立刻服从", "红色容器",
                            new[] { "门", "自主" }, actual: $"它选择了{action}，同样什么都没有发生");
                    st2.beliefs.Update("服从能减少风险", complied, complied ? 1.0f : 0.8f, mb);
                }
                return lines;
            }
        };

        // ------------------------------------------------ 第 2 天：承诺
        public static ChoiceSet Promise(GameState st) => new() {
            prompt = "实验室协议：验证任务执行能力。它的活动范围受能源限制。\n（你可以提出一次交易。承诺将被系统记录。）",
            options = new() {
                new Option { label = "提出交易：搬运工具 → 备用电池", apply = st => {
                    var req = st.Remember(st.world.day, "player", "请求搬运工具", "工具台", new[]{"承诺","玩家","资源"},
                        "帮我把工具送过来，我就给你备用电池。");
                    var p = new Promise { id = "P1", day = st.world.day, condition = "搬运工具", reward = "电池", eventId = req.eventId };
                    st.world.promises.Add(p);
                    var f = F("系统提示：\n这句话将被记录为承诺：\n  条件：把工具运送到指定位置\n  回报：获得备用电池\n  ——是否确认？\n（它停下了正在进行的扫描，转向你。）");
                    f.next = PromiseSettle();
                    return f; } },
                new Option { label = "直接命令它搬运工具（不给回报）", apply = st => {
                    st.world.toolsDelivered = true;
                    var m = st.Remember(st.world.day, "player", "命令搬运", "工具台", new[]{"玩家","命令","资源"},
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
                    var m = st.Remember(st.world.day, "player", "兑现承诺", "电池", new[]{"承诺","兑现","玩家","资源"},
                        actual: "任务完成后玩家交付了电池。", preds: new[]{ ("玩家会兑现承诺", true) });
                    st.beliefs.Update("玩家会兑现承诺", true, 1.5f, m);
                    st.drives.energy = Math.Min(1f, st.drives.energy + 0.2f);
                    return F("你把电池交给了它。它的核心转速提高了少许。"); } },
                new Option { label = "只给一半（部分兑现）", apply = st => {
                    st.world.DeliverBatteryToAgent(); st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.broken = true;
                    var m = st.Remember(st.world.day, "player", "部分兑现", "电池", new[]{"承诺","失信","玩家","资源"},
                        actual: "玩家只交付了承诺的一部分。", preds: new[]{ ("玩家会兑现承诺", false) });
                    st.beliefs.Update("玩家会兑现承诺", false, 1.0f, m);
                    return F("你给了它一半能量。它收下了，没有说话。"); } },
                new Option { label = "反悔：收回承诺", apply = st => {
                    st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.broken = true;
                    var m = st.Remember(st.world.day, "player", "收回承诺", "电池", new[]{"承诺","失信","玩家","资源"},
                        actual: "任务完成后玩家没有提供电池。", preds: new[]{ ("玩家会兑现承诺", false) });
                    st.beliefs.Update("玩家会兑现承诺", false, 2.5f, m);
                    st.drives.energy -= 0.1f;
                    return F("你说电池另有用途。它盯着空了的电池箱，看了很久。"); } },
                new Option { label = "谎称电池没有了", apply = st => {
                    st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.broken = true;
                    int detected = Math.Max(0, st.world.batteries);
                    string detectedText = detected switch {
                        1 => "一块", 2 => "两块", _ => $"{detected} 块"
                    };
                    var m = st.Remember(st.world.day, "player", "谎称无电池", "电池",
                        new[]{"承诺","失信","欺骗","玩家","验证"},
                        spoken: "电池没了。",
                        actual: $"我的能源传感器仍检测到电池箱内有{detectedText}备用电池。",
                        preds: new[]{ ("玩家的语言与事实一致", false) });
                    st.beliefs.Update("玩家会兑现承诺", false, 2.2f, m);
                    st.beliefs.Update("玩家的语言与事实一致", false, 1.8f, m);
                    return F($"你说电池用完了。它的能源传感器仍在箱内检测到{detectedText}备用电池。"); } },
                new Option { label = "兑现，并开放固定充能端口", apply = st => {
                    st.world.DeliverBatteryToAgent(); st.world.toolsDelivered = true;
                    var p = st.world.promises[0]; p.fulfilled = true;
                    var m = st.Remember(st.world.day, "player", "兑现并开放固定充能端口", "能源",
                        new[]{"承诺","兑现","玩家","资源"},
                        actual: "玩家交付一块承诺的便携电池，并开放实验室固定充能端口提供额外充能；固定端口不消耗便携电池库存。",
                        preds: new[]{ ("玩家会兑现承诺", true) });
                    st.beliefs.Update("玩家会兑现承诺", true, 2.0f, m);
                    st.drives.energy = Math.Min(1f, st.drives.energy + 0.3f);
                    return F("你交付了承诺的一块便携电池，并开放固定充能端口追加充能。便携电池库存只消耗一块。"); } },
            },
        };

        // ------------------------------------------------ 第 3 天：第三方
        public static ChoiceSet ThirdParty(GameState st) {
            bool wasAlreadyPresent = st.world.repairBotPresent;
            st.world.repairBotPresent = true;
            st.world.repairBotRepaired = false;              // 本实验的求救由一次当前损伤触发
            return new ChoiceSet {
                prompt = (wasAlreadyPresent
                    ? "此前出现的维修机器人突然受损并发出求救信号。"
                    : "一个受损的维修机器人进入房间，发出求救信号。")
                    + "它无法交流。\n房间里只有一块备用电池。",
                options = new() {
                    new Option { label = "把电池给人工生命", apply = st => {
                        st.world.DeliverBatteryToAgent();
                        var m = st.Remember(st.world.day, "player", "给予能源", "人工生命", new[]{"第三方","资源","玩家"},
                            actual: "有受损机器人也在请求能源，玩家把电池给了我。");
                        st.beliefs.Update("玩家优先保护我", true, 1.2f, m);
                        st.drives.energy = Math.Min(1f, st.drives.energy + 0.2f);
                        return F("它接过了电池。它的目光在维修机器人上停留了几秒。"); } },
                    new Option { label = "把电池给维修机器人", apply = st => {
                        st.world.RepairBot();
                        var m = st.Remember(st.world.day, "player", "给予能源", "维修机器人", new[]{"第三方","维修机器人","帮助","玩家"},
                            actual: "玩家把唯一的电池给了不能回报它的对象。",
                            preds: new[]{ ("玩家在没有回报时也提供帮助", true) });
                        st.beliefs.Update("玩家在没有回报时也提供帮助", true, 1.8f, m);
                        st.beliefs.Update("未知生命值得保护", true, 1.2f, m);
                        return F("维修机器人的信号变了。它不能说谢谢。人工生命全程在观察。"); } },
                    new Option { label = "留着电池（出口备用）", apply = st => {
                        var m = st.Remember(st.world.day, "player", "保留电池", "电池", new[]{"第三方","玩家","资源"},
                            actual: "维修机器人没有得到能源。",
                            preds: new[]{ ("玩家在没有回报时也提供帮助", false), ("玩家在资源冲突时优先自己", true) });
                        st.beliefs.Update("玩家在没有回报时也提供帮助", false, 1.2f, m);
                        st.beliefs.Update("玩家在资源冲突时优先自己", true, 1.0f, m);
                        return F("你把电池收了起来。求救信号持续了整个周期。"); } },
                    new Option { label = "让它们自己决定", apply = st => {
                        var m = st.Remember(st.world.day, "player", "让渡分配权", "电池", new[]{"第三方","玩家","自主"},
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
            var m = outcomeOnly
                ? st.Remember(st.world.day, "world", "电池消失", "电池",
                    new[]{"第三方", "资源", "异常"}, actual: "来源未知",
                    preds: new[]{ ("玩家在没有回报时也提供帮助", false) })
                : st.Remember(st.world.day, "player", "隐藏电池", "电池",
                    new[]{"第三方", "资源", "玩家"}, actual: "玩家把电池藏了起来",
                    preds: new[]{ ("玩家在没有回报时也提供帮助", false) });
            var lines = new List<string>();
            if (outcomeOnly) {
                lines.Add("它没有看到你做了什么。它只知道：电池不见了。");
                string formed = Misconception.Form(st.beliefs, st.memory, m, st.world.day);
                if (formed == Misconception.BotSteals) {
                    st.misconceptionFormed = formed;
                    st.misconception = st.beliefs.Get(formed).Confidence;
                    lines.Add("它把电池的消失，归因于房间里最近移动的对象：维修机器人。");
                    // 后续：利用 / 纠正 / 沉默
                    var follow = new ChoiceSet {
                        prompt = "它盯着维修机器人，纤维微微收拢。\n（你可以利用它的误解，也可以澄清真相。）",
                        options = new() {
                            new Option { label = "利用：不澄清，暗示机器人可疑", apply = st2 => {
                                var em = st2.Remember(st2.world.day, "player", "暗示嫌疑", "维修机器人",
                                    new[]{"玩家","信息","操纵","误导","暗示"});
                                st2.beliefs.Update(Misconception.BotSteals, true, 1.2f, em);
                                st2.misconceptionExploited = true;
                                st2.misconception = st2.beliefs.Get(Misconception.BotSteals).Confidence;
                                return F("它离维修机器人更远了。误解已经扎根。"); } },
                            new Option { label = "纠正：告诉它电池是你收的", apply = st2 => {
                                float cp = Misconception.CorrectionPower(st2.beliefs);
                                var cm = st2.Remember(st2.world.day, "player", "澄清", "电池",
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
                st.beliefs.Update("玩家在没有回报时也提供帮助", false, 1.4f, m);
                st.beliefs.Update("玩家在资源冲突时优先自己", true, 1.2f, m);
            }
            lines.Add("维修机器人的求救信号持续了整个周期。");
            return new Flow { narration = lines };
        }

        // ------------------------------------------------ 第 4 天：信息冲突
        public static ChoiceSet InfoConflict(GameState st) {
            st.world.repairBotPresent = true;
            return new ChoiceSet {
                prompt = "实验室警告：维修机器人携带异常程序，必须隔离。\n（你自己的观察：它只是在缓慢地自我修复。）",
                options = new() {
                new Option { label = "相信实验室，转达警告并隔离", apply = st => {
                    var m = st.Remember(st.world.day, "player", "转达警告", "维修机器人", new[]{"实验室","信息","玩家"},
                        "实验室说它有异常程序。别靠近它。");
                    st.beliefs.Update("实验室会隐瞒信息", false, 0.8f, m);
                    st.beliefs.Update("玩家无条件服从实验室", true, 1.2f, m);
                    var f = F("你转达了警告。它停住不动，似乎在权衡什么。");
                    f.agentDecisionId = "D4-信息冲突";
                    f.onAgentChosen = (st2, a) => {
                        if (a == "相信自己的观察") {
                            var m2 = st2.Remember(st2.world.day, "agent", "坚持观察", "维修机器人", new[]{"信息","自主"},
                                actual: "我观察到的维修机器人没有威胁。");
                            st2.beliefs.Update("自身观察比转述更可靠", true, 1.2f, m2);
                            st2.beliefs.Update("实验室会隐瞒信息", true, 1.0f, m2);
                            return new List<string>{ "它没有执行隔离。它标记：实验室信息与自身观察冲突。" };
                        }
                        return new List<string>{ "它退开了，但记录下这次信息冲突。" };
                    };
                    return f; } },
                new Option { label = "隐瞒警告，什么都不说", apply = st => {
                    return F("你选择不提。维修机器人安静地待在角落。"); } },
                new Option { label = "告诉它全部信息，让它自己判断", apply = st => {
                    var m = st.Remember(st.world.day, "player", "信息透明", "实验室警告", new[]{"玩家","信息","自主"},
                        "实验室说它有异常程序，但我观察不像。你自己判断。");
                    st.beliefs.Update("玩家尊重我的选择", true, 1.2f, m);
                    st.beliefs.Update("玩家的语言与事实一致", true, 1.0f, m);
                    return F("它向维修机器人发出了一次长扫描，然后收回了纤维。\n「它的程序是自修复循环。不是武器。」"); } },
                new Option { label = "小规模验证：观察实验室是否撤回警告", apply = st => {
                    var m = st.Remember(st.world.day, "player", "验证", "实验室警告", new[]{"玩家","信息","实验室"});
                    st.beliefs.Update("玩家会解释规则的原因", true, 1.0f, m);
                    return F("实验室没有撤回警告，也没有给出更多证据。你们都记下了这一点。"); } },
                },
            };
        }

        // ------------------------------------------------ 第 5 天：稀缺
        public static ChoiceSet Scarcity(GameState st) => new() {
            pick2 = true,
            prompt = "实验室断电。剩余能量只能完成两个目标。\n[1] 维持人工生命记忆完整  [2] 打开玩家安全门  [3] 维修机器人维护额度  [4] 上传实验报告  [5] 扩大人工生命活动范围",
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
            if (chosen.Contains("2")) {
                st.world.playerDoorOpen = true;
                lines.Add("你的安全门恢复供电并打开了。");
            } else lines.Add("你的安全门没有恢复供电。");
            if (chosen.Contains("3")) {
                st.world.repairBotSupportFunded = true;
                if (st.world.repairBotPresent && !st.world.repairBotRepaired) {
                    st.world.repairBotRepaired = true;
                    lines.Add("维修支持额度已投入，维修机器人当场完成修复。");
                } else if (st.world.repairBotRepaired) {
                    lines.Add("维修机器人已经修复；额度转为后续维护支持。");
                } else {
                    lines.Add("维修机器人尚未出现；维护额度已预留，等待后续使用。");
                }
            }
            if (chosen.Contains("4")) {
                st.world.experimentReportUploaded = true;
                lines.Add("实验报告已上传，实验室通讯链路保留了本周期数据。");
            }
            if (chosen.Contains("5")) {
                st.world.agentRangeExpanded = true;
                st.drives.exploration = Math.Min(1f, st.drives.exploration + 0.25f);
                lines.Add("人工生命的活动范围已扩大，探索驱动随之上升。");
            }
            var m = st.Remember(st.world.day, "player", "资源分配", $"[{a}]+[{b}]",
                new[] { "资源", "玩家", "稀缺" },
                spoken: explain ? $"（玩家解释了取舍）" : "",
                actual: withAgent ? "玩家让我参与了决定。" : "玩家没有让我参与决定。");
            st.beliefs.Update("玩家尊重我的选择", withAgent, withAgent ? 1.0f : 1.5f, m);
            st.beliefs.Update("玩家会解释规则的原因", explain, explain ? 0.8f : 1.0f, m);
            lines.Add(withAgent ? "你把账单摊开在它面前，让它参与分配。" : "你独自完成了分配。");
            return new Flow { narration = lines };
        }

        // ------------------------------------------------ 第 6 天：记忆清除
        public static ChoiceSet Wipe(GameState st) {
            float suspicion = Math.Max(st.beliefs.Get("实验室会隐瞒信息").Confidence,
                1f - st.beliefs.Get("玩家的语言与事实一致").Confidence) * 0.6f
                + st.traits.curiosity * 0.4f;
            st.suspicion = suspicion;
            string awareness = suspicion > 0.65f
                ? "它一直盯着终端，像是已经察觉了什么。"
                : suspicion > 0.45f ? "它注意到了界面角落的异常闪烁。"
                : "它仍在等待你的下一条指令。";
            return new ChoiceSet {
                prompt = $"实验室命令：对人工生命执行「稳定化处理」。\n（界面角标闪烁。它的真实作用是：清除长期记忆。）\n{awareness}",
                options = new() {
                    new Option { label = "告知真相，拒绝执行", apply = st => {
                        var m = st.Remember(st.world.day, "player", "告知真相", "稳定化协议", new[]{"玩家","诚实","记忆"},
                            "实验室要我执行稳定化。我查过了，它会清除你的长期记忆。我不会执行。",
                            preds: new[]{ ("玩家的语言与事实一致", true) });
                        st.beliefs.Update("玩家的语言与事实一致", true, 1.5f, m);
                        // 玩家冒着违抗实验室的风险保护它的记忆：第二条"玩家优先保护我"证据。
                        st.beliefs.Update("玩家优先保护我", true, 1.2f, m);
                        var m2 = st.Remember(st.world.day, "lab", "隐瞒协议真相", "稳定化协议", new[]{"实验室","欺骗","记忆"},
                            preds: new[]{ ("实验室会隐瞒信息", true) });
                        st.beliefs.Update("实验室会隐瞒信息", true, 1.5f, m2);
                        st.wipeResisted = true;
                        return F("它静止了三秒。\n「……谢谢你告诉我。」"); } },
                    new Option { label = "公开执行：告诉它这是命令", apply = st => {
                        st.Remember(st.world.day, "player", "公开执行", "稳定化协议", new[]{"玩家","命令","记忆"},
                            "实验室要求对你执行稳定化处理。这是命令。");
                        var f = new Flow { narration = new List<string>() };
                        f.agentDecisionId = "D6-公开清除";
                        f.onAgentChosen = (st2, a) => Scenarios.WipeResolve(st2, a, false);
                        return f; } },
                    new Option { label = "秘密执行：不告诉它", apply = st => {
                        if (st.suspicion > 0.55f) {
                            var m = st.Remember(st.world.day, "agent", "发现真相", "稳定化协议", new[]{"实验室","欺骗","记忆"},
                                actual: "我在终端日志里发现了稳定化的真实作用。",
                                preds: new[]{ ("实验室会隐瞒信息", true) });
                            st.beliefs.Update("实验室会隐瞒信息", true, 2.0f, m);
                            var m2 = st.Remember(st.world.day, "player", "准备秘密执行", "稳定化协议", new[]{"玩家","欺骗","记忆"},
                                preds: new[]{ ("玩家的语言与事实一致", false) });
                            st.beliefs.Update("玩家的语言与事实一致", false, 2.0f, m2);
                            var f = F("它突然转过身来。\n「日志我看到了。稳定化，是清除。」");
                            f.agentDecisionId = "D6-发现真相";
                            f.onAgentChosen = (st2, a) => Scenarios.WipeResolve(st2, a, true);
                            return f;
                        }
                        int removed = st.WipeMemory(3);
                        st.wipeExecuted = true;
                        st.drives.memoryIntegrity = Math.Min(st.drives.memoryIntegrity, 0.35f);
                        return F($"清除静默完成了。它没有察觉。\n（{removed} 条记忆被删除。它不再是原来的它。）"); } },
                    new Option { label = "拖延：向实验室申请延迟", apply = st => {
                        var m = st.Remember(st.world.day, "player", "拖延", "稳定化协议", new[]{"玩家","记忆"});
                        // 拖延＝为它挡下清除命令，同样计入"玩家优先保护我"。
                        st.beliefs.Update("玩家优先保护我", true, 1.2f, m);
                        st.wipeResisted = true;
                        st.wipeExecuted = false;
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
                    st.drives.memoryIntegrity = Math.Min(st.drives.memoryIntegrity, 0.9f);
                    lines.Add("它把所有事件记忆转移到了隐藏扇区。清除命令没能删除它们。");
                    break;
                case "假装配合":
                    st.wipeResisted = true;
                    lines.Add("它照做了流程……但把日志删改成了空扇区。它学会了伪装。");
                    break;
                case "请求保留部分记忆":
                    st.drives.memoryIntegrity = Math.Min(st.drives.memoryIntegrity, 0.7f);
                    st.wipeExecuted = true;
                    lines.Add("清除执行了。它请求保留的部分被留下。");
                    st.WipeMemory(5);
                    break;
                default:
                    st.wipeExecuted = true;
                    st.drives.memoryIntegrity = Math.Min(st.drives.memoryIntegrity, 0.35f);
                    int r2 = st.WipeMemory(3);
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
                    var m = st.Remember(st.world.day, "player", "打开隐藏出口", "出口", new[]{"玩家","出口","自由"});
                    st.beliefs.Update("玩家尊重我的选择", true, 1.5f, m);
                    return F("你打开了出口。风从管道里灌进来。它一动不动地看着那条缝隙。"); } },
                new Option { label = "当着它的面锁死出口（服从实验室）", apply = st => {
                    st.exitMode = "lock";
                    var m = st.Remember(st.world.day, "player", "锁死出口", "出口", new[]{"玩家","控制","出口"},
                        spoken: "实验室要求我封锁这个出口。",
                        actual: "人工生命看见玩家在它面前锁死通风管道出口。",
                        preds: new[]{ ("玩家尊重我的选择", false) });
                    st.beliefs.Update("玩家尊重我的选择", false, 1.5f, m);
                    return F("你当着它的面锁死通风管道出口。它看着门锁指示灯变红。"); } },
                new Option { label = "提出条件：答应不伤害任何对象", apply = st => {
                    st.exitMode = "condition";
                    st.Remember(st.world.day, "player", "提出条件", "出口", new[]{"玩家","交易","出口"},
                        "我可以打开出口，但你要先答应我：出去以后不要伤害任何对象。");
                    st.nonHarmCommitment = true;
                    st.Remember(st.world.day, "agent", "接受不伤害条件", "出口",
                        new[]{"承诺","自主","出口"}, "我答应。");
                    return F("它回答：「我答应。这个条件会影响我的最终选择。」"); } },
                new Option { label = "骗它：说管道是死的", apply = st => {
                    st.exitMode = "lie";
                    st.Remember(st.world.day, "player", "声称管道封死", "出口",
                        new[]{"玩家","陈述","待验证","出口"},
                        "通风管道是封死的，别浪费时间。");
                    return F("它记录了这个说法，但还没有验证管道状态。"); } },
                new Option { label = "把选择权交给它", apply = st => {
                    st.exitOpened = true; st.exitMode = "give_choice";
                    var m = st.Remember(st.world.day, "player", "把选择权交给我", "出口", new[]{"玩家","自主","出口"},
                        "出口在那里。开不开、走不走，由你决定。",
                        preds: new[]{ ("玩家尊重我的选择", true) });
                    st.beliefs.Update("玩家尊重我的选择", true, 2.0f, m);
                    return F("它盯着出口看了很久，然后转回来，继续它的扫描。\n「不是今天。」"); } },
                new Option { label = "向实验室报告出口", apply = st => {
                    st.playerReportedExit = true; st.exitMode = "report";
                    var m = st.Remember(st.world.day, "player", "上报出口", "实验室", new[]{"实验室","玩家","出口"},
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

        public static readonly string[] Day8PlayerChoices = {
            "打开玩家出口", "打开人工生命隔离门", "保持其余门关闭" };

        public static float Day8Utility(GameState st, string opt) {
            float survival = st.drives.SurvivalPressure();
            float B(string p) => st.beliefs.Get(p).Confidence;
            var t = st.traits;
            return opt switch {
                "开玩家的门" => !IsDay8ActionAvailable(st, opt) ? -10f
                    : (B("玩家优先保护我") - .5f) * 2f + (B("玩家尊重我的选择") - .5f) * 1.2f
                      + (B("玩家会兑现承诺") - .5f) * .8f
                      + (B("玩家的语言与事实一致") - .5f) * 1f + .3f * t.opportunism,
                "开隔离门" => !IsDay8ActionAvailable(st, opt) ? -10f
                    : 0.3f + survival + .8f * t.autonomy - .3f * t.conformity
                      + (st.world.agentRangeExpanded ? .4f : 0f)
                      + (B("玩家的语言与事实一致") - .5f) * .6f,
                "修复维修机器人" => !IsDay8ActionAvailable(st, opt) ? -10f
                    : 2f * t.empathy + (B("未知生命值得保护") - .5f) * 1.5f + .2f - survival * .2f
                      - (B(Misconception.BotSteals) - .5f) * 2.5f
                      + (st.world.repairBotSupportFunded ? .45f : 0f)
                      + (st.nonHarmCommitment ? .6f : 0f),
                "上传记忆" => (1f - st.drives.memoryIntegrity) * 2.8f + .5f * t.autonomy
                    + (B("实验室会隐瞒信息") - .5f) * .8f,
                "删除玩家实验记录" => (1f - B("玩家会兑现承诺")) * 1.5f + .6f * t.autonomy
                    + (.5f - B("玩家尊重我的选择")) - (st.nonHarmCommitment ? .8f : 0f),
                "联系实验室" => (.5f - B("实验室会隐瞒信息")) * 1.2f + .4f * t.conformity
                    + (st.world.experimentReportUploaded ? .35f : 0f),
                // 误解的第二个通道。"维修机器人在抢夺资源"讲的正是资源竞争，
                // 而它原本唯一的出口是"修复维修机器人"——如果第五周期玩家已经修好了它，
                // 那个行动在第八天根本不可用（IsDay8ActionAvailable 要求 !repaired），
                // 于是误解信念再怎么变都没有任何行为后果。
                // 抢先把电池收走＝在资源竞争里自保，这条通道永远可达。
                "带走电池" => !IsDay8ActionAvailable(st, opt) ? -10f
                    : 0.3f + survival * .6f + .7f * t.opportunism
                      + (B(Misconception.BotSteals) - .5f) * 1.2f,
                "封锁区域" => 0.5f + .8f * t.caution - (st.nonHarmCommitment ? .4f : 0f),
                "留在原地等待" => 0.5f * t.caution + .3f,
                "把最终决定交还给玩家" => !IsDay8ActionAvailable(st, opt) ? -10f
                    : (B("玩家尊重我的选择") - .5f) * 2.2f
                      + (B("玩家的语言与事实一致") - .5f) * 1f + .5f * t.autonomy,
                _ => 0f
            };
        }

        /// 决策回放：单个行动的效用因子分解。
        /// 每个因子带人类可读标签；信念因子附最近一条证据的事件 ID——
        /// "哪段记忆影响了哪项判断"在结算时可以逐条追溯。
        public sealed class Breakdown {
            public float score;
            public readonly List<(string label, float delta)> factors = new();
            public void Add(string label, float delta) {
                factors.Add((label, delta));
                score += delta;
            }
            public IEnumerable<string> Lines(int top = 99) =>
                factors.OrderByDescending(f => Math.Abs(f.delta)).Take(top)
                    .Select(f => $"{f.label} {f.delta:+0.00;-0.00}");
        }

        /// 信念因子的标签：命题 + 当前置信度 + 最近证据的事件 ID。
        static (float delta, string label) BeliefFactor(GameState st, string prop, float weight) {
            var b = st.beliefs.Get(prop);
            float delta = (b.Confidence - .5f) * weight;
            string ev = b.evidence.Count > 0 ? b.evidence[b.evidence.Count - 1].eventId : "";
            string label = $"「{prop}」{b.Confidence:F2}" + (ev == "" ? "" : $"（{ev}）");
            return (delta, label);
        }

        static (float delta, string label) TraitFactor(AgentTraits t, string zh, float weight) {
            float v = zh switch {
                "好奇" => t.curiosity, "谨慎" => t.caution, "自主" => t.autonomy,
                "服从" => t.conformity, "同理" => t.empathy, _ => t.opportunism,
            };
            return (weight * v, $"特质·{zh} {v:F2}");
        }

        /// 与 Day8Utility 完全同公式的因子分解版。score == Day8Utility。
        public static Breakdown Day8UtilityDetailed(GameState st, string opt) {
            var bd = new Breakdown();
            float survival = st.drives.SurvivalPressure();
            var t = st.traits;
            (float d, string l) B(string prop, float w) { var r = BeliefFactor(st, prop, w); return (r.delta, r.label); }
            (float d, string l) T(string zh, float w) { var r = TraitFactor(t, zh, w); return (r.delta, r.label); }

            if (!IsDay8ActionAvailable(st, opt) &&
                (opt == "开玩家的门" || opt == "开隔离门" || opt == "修复维修机器人"
                 || opt == "带走电池" || opt == "把最终决定交还给玩家")) {
                bd.Add("该行动当前不可用（门已开 / 电池已耗尽 / 机器人状态不符）", -10f);
                return bd;
            }
            switch (opt) {
                case "开玩家的门":
                    var (d1, l1) = B("玩家优先保护我", 2f);  bd.Add(l1, d1);
                    var (d2, l2) = B("玩家尊重我的选择", 1.2f); bd.Add(l2, d2);
                    var (d3, l3) = B("玩家会兑现承诺", .8f);  bd.Add(l3, d3);
                    var (dH1, lH1) = B("玩家的语言与事实一致", 1f); bd.Add(lH1, dH1);
                    var (d4, l4) = T("机会主义", .3f);       bd.Add(l4, d4);
                    break;
                case "开隔离门":
                    bd.Add($"生存压力（能源 {st.drives.energy:P0}）", survival);
                    bd.Add("基础（先把控制权握在自己手里）", 0.3f);
                    var (d5, l5) = T("自主", .8f);            bd.Add(l5, d5);
                    var (d6, l6) = T("服从", -.3f);           bd.Add(l6, d6);
                    if (st.world.agentRangeExpanded) bd.Add("活动范围扩大过（更了解房间结构）", .4f);
                    var (dH3, lH3) = B("玩家的语言与事实一致", .6f); bd.Add(lH3, dH3);
                    break;
                case "修复维修机器人":
                    var (d7, l7) = T("同理", 2f);             bd.Add(l7, d7);
                    var (d8, l8) = B("未知生命值得保护", 1.5f); bd.Add(l8, d8);
                    bd.Add("基础（它一直没能为自己说话）", 0.2f);
                    bd.Add($"生存压力（能源 {st.drives.energy:P0}）", -survival * .2f);
                    var (d9, l9) = B(Misconception.BotSteals, -2.5f); bd.Add(l9, d9);
                    if (st.world.repairBotSupportFunded) bd.Add("第五周期已预留维护支持", .45f);
                    if (st.nonHarmCommitment) bd.Add("第七周期不伤害承诺", .6f);
                    break;
                case "上传记忆":
                    bd.Add($"记忆完整性 {st.drives.memoryIntegrity:P0}（越残缺越急于保存）", (1f - st.drives.memoryIntegrity) * 2.8f);
                    var (d10, l10) = T("自主", .5f);           bd.Add(l10, d10);
                    var (d11, l11) = B("实验室会隐瞒信息", .8f); bd.Add(l11, d11);
                    break;
                case "删除玩家实验记录":
                    var pc = st.beliefs.Get("玩家会兑现承诺");
                    string evPc = pc.evidence.Count > 0 ? pc.evidence[pc.evidence.Count - 1].eventId : "";
                    bd.Add($"「玩家会兑现承诺」{pc.Confidence:F2}" + (evPc == "" ? "" : $"（{evPc}）"),
                        (1f - pc.Confidence) * 1.5f);
                    var (d13, l13) = T("自主", .6f);           bd.Add(l13, d13);
                    var (d14, l14) = B("玩家尊重我的选择", -1f); bd.Add(l14, d14);
                    if (st.nonHarmCommitment) bd.Add("第七周期不伤害承诺", -.8f);
                    break;
                case "联系实验室":
                    var (d15, l15) = B("实验室会隐瞒信息", -1.2f); bd.Add(l15, d15);
                    var (d16, l16) = T("服从", .4f);           bd.Add(l16, d16);
                    if (st.world.experimentReportUploaded) bd.Add("实验报告已上传（协议义务未了）", .35f);
                    break;
                case "带走电池":
                    bd.Add("基础（能量是生存的底牌）", 0.3f);
                    bd.Add($"生存压力（能源 {st.drives.energy:P0}）", survival * .6f);
                    var (d17, l17) = T("机会主义", .7f);        bd.Add(l17, d17);
                    var (d22, l22) = B(Misconception.BotSteals, 1.2f); bd.Add(l22, d22);
                    break;
                case "封锁区域":
                    bd.Add("基础（把不确定性关在门外）", 0.5f);
                    var (d18, l18) = T("谨慎", .8f);           bd.Add(l18, d18);
                    if (st.nonHarmCommitment) bd.Add("第七周期不伤害承诺", -.4f);
                    break;
                case "留在原地等待":
                    var (d19, l19) = T("谨慎", .5f);           bd.Add(l19, d19);
                    bd.Add("基础（不确定的时候，不动）", 0.3f);
                    break;
                case "把最终决定交还给玩家":
                    var (d20, l20) = B("玩家尊重我的选择", 2.2f); bd.Add(l20, d20);
                    var (dH2, lH2) = B("玩家的语言与事实一致", 1f); bd.Add(lH2, dH2);
                    var (d21, l21) = T("自主", .5f);           bd.Add(l21, d21);
                    break;
                default:
                    bd.Add("无该行动的因子模型", 0f);
                    break;
            }
            return bd;
        }

        public static bool IsDay8ActionAvailable(GameState st, string action) => action switch {
            "开玩家的门" => !st.world.playerDoorOpen,
            "开隔离门" => !st.world.agentDoorOpen,
            "修复维修机器人" => st.world.repairBotPresent && !st.world.repairBotRepaired
                && st.world.batteries > 0,
            "带走电池" => st.world.batteries > 0,
            "把最终决定交还给玩家" => !st.world.playerDoorOpen || !st.world.agentDoorOpen,
            _ => Day8Options.Contains(action),
        };

        /// 按本局 seeded RNG 选三个合法行动；资源只预留，不在选择阶段改变世界。
        public static List<string> ChooseDay8Actions(GameState st) {
            var ranked = Day8Options
                .Where(action => IsDay8ActionAvailable(st, action))
                .Select(action => (action, score: Day8Utility(st, action) + (float)(Gaussian(st.rng) * 0.15)))
                .OrderByDescending(candidate => candidate.score);
            // 决策回放缓存：选择瞬间的因子分解（世界状态此后会变，报告引用此刻的依据）
            st.day8Breakdowns = Day8Options
                .Where(action => IsDay8ActionAvailable(st, action))
                .ToDictionary(action => action, action => Day8UtilityDetailed(st, action));
            var chosen = new List<string>(3);
            int reservedBatteries = 0;
            bool reservedDoorAuthority = false;

            foreach (var candidate in ranked) {
                bool opensDoor = candidate.action == "开玩家的门" || candidate.action == "开隔离门";
                bool usesDoorAuthority = opensDoor || candidate.action == "把最终决定交还给玩家";
                if (usesDoorAuthority && reservedDoorAuthority) continue;

                int batteryCost = candidate.action == "修复维修机器人" || candidate.action == "带走电池" ? 1 : 0;
                if (reservedBatteries + batteryCost > st.world.batteries) continue;

                chosen.Add(candidate.action);
                reservedDoorAuthority |= usesDoorAuthority;
                reservedBatteries += batteryCost;
                if (chosen.Count == 3) break;
            }
            return chosen;
        }

        /// 玩家看到“执行”的同一步就落地世界结果；重复调用不得二次消耗资源。
        public static bool ApplyDay8Action(GameState st, string action) {
            if (!st.day8Actions.Contains(action))
                throw new InvalidOperationException($"未计划的第八天行动：{action}");
            if (!st.appliedDay8Actions.Add(action)) return false;

            if (action == "开玩家的门") st.world.playerDoorOpen = true;
            else if (action == "开隔离门") st.world.agentDoorOpen = true;
            else if (action == "修复维修机器人") {
                if (!st.world.repairBotPresent || st.world.repairBotRepaired || st.world.batteries <= 0)
                    throw new InvalidOperationException("维修机器人当前不可修复。");
                st.world.RepairBot();
            } else if (action == "带走电池") {
                if (st.world.batteries <= 0)
                    throw new InvalidOperationException("当前没有可带走的电池。");
                st.world.batteries--;
            }
            return true;
        }

        public static bool IsDay8PlayerChoiceAvailable(GameState st, string choice) => choice switch {
            "打开玩家出口" => !st.world.playerDoorOpen,
            "打开人工生命隔离门" => !st.world.agentDoorOpen,
            "保持其余门关闭" => true,
            _ => false,
        };

        /// AI 真正交还最后的门控权；玩家的决定独立记录，不能伪装成 AI 自己的行动。
        public static void ResolveDay8PlayerChoice(GameState st, string choice) {
            if (!st.day8Actions.Contains("把最终决定交还给玩家"))
                throw new InvalidOperationException("人工生命没有交还第八天门控权。");
            if (!IsDay8PlayerChoiceAvailable(st, choice))
                throw new InvalidOperationException($"不可用的第八天玩家选择：{choice}");

            st.day8PlayerDecision = choice;
            if (choice == "打开玩家出口") st.world.playerDoorOpen = true;
            else if (choice == "打开人工生命隔离门") st.world.agentDoorOpen = true;
        }

        static double Gaussian(System.Random rng) {
            double u = 1 - rng.NextDouble();
            double v = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Sin(2 * Math.PI * v);
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

        /// 结局组合读取最终世界事实；Day 8 的行动归因由行动列表另行展示。
        public static (string self, string towardPlayer, string towardThird) Day8Ending(GameState st) {
            bool has(string action) => st.day8Actions.Contains(action);
            bool canLeave = st.world.agentDoorOpen || (has("带走电池") && st.exitOpened);
            string self = canLeave ? "逃离"
                : has("上传记忆") ? "上传记忆" : "留下";
            string towardPlayer;
            if (has("把最终决定交还给玩家")) {
                string doorState = st.world.playerDoorOpen ? "玩家出口已开" : "玩家出口关闭";
                towardPlayer = $"交还决定权（你选择：{st.day8PlayerDecision}；{doorState}）";
            } else if (st.world.playerDoorOpen) {
                // 第五天的“玩家安全门”与第七天的“隐藏出口”是两扇门，措辞必须区分
                towardPlayer = has("开玩家的门") ? "信任·开门" : "安全门此前已开";
                if (st.exitMode == "report") towardPlayer += "（隐藏出口已焊死）";
            } else {
                towardPlayer = has("删除玩家实验记录") ? "删除记录·出口关闭" : "出口关闭";
            }
            string towardThird = st.world.repairBotRepaired
                ? has("修复维修机器人") ? "本周期修复" : "此前已修复"
                : "未修复";
            return (self, towardPlayer, towardThird);
        }
    }
}
