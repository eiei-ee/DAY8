using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Day8.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Day8.UI {

    /// 《第八天》主控制器：观察员控制台 + 流程状态机。
    public class GameUI : MonoBehaviour {

        enum Phase { Title, Intro, Choice, Processing, Night, Day8Reveal, Report }

        GameState st;
        int day;
        Phase phase;
        ChoiceSet current;
        Flow pendingFlow;
        List<string> narrationQueue = new();
        bool awaitingAgent;
        string agentDecisionId;
        HashSet<string> pick2Selection = new();
        int day8RevealIndex;
        float titleBell;   // 谱系先验显示
        readonly LlmSpeaker llm = new LlmSpeaker();

        // UI 引用
        RectTransform room, console;
        Text dayLabel, energyLabel, logText, promptText, agentState;
        RectTransform choiceBox, roomContent;
        InputField chatInput;
        ScrollRect logScroll;
        Image agentGlyph;
        float agentTargetX = 0f;

        Font _font;
        Font Font => _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        const string BgCol = "#0F1114", PanelCol = "#15181C", LineCol = "#262B31",
            TextCol = "#C9D1D9", DimCol = "#7B838C", AccentCol = "#D9A05B",
            DangerCol = "#B8433A", GoodCol = "#7BAE7F";

        static Color C(string hex) {
            ColorUtility.TryParseHtmlString(hex, out var c); return c;
        }

        void Start() { BuildUI(); ShowTitle(); }

        // ================================================= UI 构建

        T Create<T>(Transform parent, string name) where T : Component {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<T>() != null ? go.GetComponent<T>() : go.AddComponent<T>();
        }

        Image Panel(Transform parent, string name, Color col) {
            var img = Create<Image>(parent, name);
            img.color = col;
            return img;
        }

        Text Text(Transform parent, string name, string content, int size, Color col,
                  TextAnchor align = TextAnchor.UpperLeft) {
            var t = Create<Text>(parent, name);
            t.font = Font; t.text = content; t.fontSize = size; t.color = col;
            t.alignment = align; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        readonly List<Button> activeButtons = new();

        Button OptionButton(Transform parent, string label, Action onClick, bool accent = false) {
            var imgGo = Create<Image>(parent, "btn_" + label);
            imgGo.color = C(PanelCol);
            var b = imgGo.gameObject.AddComponent<Button>();
            b.targetGraphic = imgGo;
            var img = imgGo;
            var trigger = b.gameObject.AddComponent<EventTriggerHover>();
            trigger.img = img;
            trigger.txt = img.GetComponentInChildren<Text>();
            var outline = b.gameObject.AddComponent<Outline>();
            outline.effectColor = C(accent ? AccentCol : LineCol); outline.effectDistance = new Vector2(0, 0);
            var rt = b.GetComponent<RectTransform>();
            var txt = Text(b.transform, "t", label, 16, C(TextCol), TextAnchor.MiddleLeft);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14, 6); trt.offsetMax = new Vector2(-14, -6);
            b.onClick.AddListener(() => { if (!_drained) FastForward(); onClick(); });
            activeButtons.Add(b);
            return b;
        }

        class EventTriggerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
            public Image img; public Text txt;
            public void OnPointerEnter(PointerEventData d) { if (img) img.color = C("#1C2126"); if (txt) txt.color = C("#D9A05B"); }
            public void OnPointerExit(PointerEventData d) { if (img) img.color = C("#15181C"); if (txt) txt.color = C("#C9D1D9"); }
        }

        void BuildUI() {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            canvasGo.AddComponent<GraphicRaycaster>();
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }

            var root = Panel(canvas.transform, "root", C(BgCol));
            var rr = root.GetComponent<RectTransform>();
            rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;

            // 顶栏
            var top = Panel(root.transform, "top", C(PanelCol));
            var trt2 = top.GetComponent<RectTransform>();
            trt2.anchorMin = new Vector2(0, 1); trt2.anchorMax = new Vector2(1, 1);
            trt2.pivot = new Vector2(0, 1); trt2.sizeDelta = new Vector2(0, 44);
            dayLabel = Text(top.transform, "day", "DAY 8", 18, C(AccentCol), TextAnchor.MiddleLeft);
            var drt = dayLabel.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0, 0); drt.anchorMax = new Vector2(0, 1);
            drt.offsetMin = new Vector2(20, 0); drt.offsetMax = new Vector2(400, 0);
            agentState = Text(top.transform, "state", "", 14, C(DimCol), TextAnchor.MiddleRight);
            var srt = agentState.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.4f, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 0); srt.offsetMax = new Vector2(-20, 0);

            // 房间视图（左）
            var roomPanel = Panel(root.transform, "room", C(BgCol));
            room = roomPanel.GetComponent<RectTransform>();
            room.anchorMin = new Vector2(0, 0); room.anchorMax = new Vector2(0.42f, 1);
            room.offsetMin = new Vector2(0, 0); room.offsetMax = new Vector2(0, -44);
            var roomFrame = roomPanel.gameObject.AddComponent<Outline>();
            roomFrame.effectColor = C(LineCol);
            roomContent = Create<RectTransform>(roomPanel.transform, "content");
            roomContent.anchorMin = Vector2.zero; roomContent.anchorMax = Vector2.one;
            roomContent.offsetMin = roomContent.offsetMax = Vector2.zero;
            DrawRoomStatic();
            agentGlyph = Create<Image>(roomContent, "agent");
            agentGlyph.sprite = MakeAgentSprite();
            agentGlyph.rectTransform.sizeDelta = new Vector2(90, 90);
            agentGlyph.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            agentGlyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            agentGlyph.rectTransform.anchoredPosition = new Vector2(0, -40);

            // 控制台（右）
            var con = Panel(root.transform, "console", C(PanelCol));
            console = con.GetComponent<RectTransform>();
            console.anchorMin = new Vector2(0.42f, 0); console.anchorMax = new Vector2(1, 1);
            console.offsetMin = new Vector2(2, 0); console.offsetMax = Vector2.zero;
            var frame = con.gameObject.AddComponent<Outline>(); frame.effectColor = C(LineCol);

            energyLabel = Text(con.transform, "energy", "", 13, C(DimCol), TextAnchor.MiddleLeft);
            if (sfx == null) sfx = AudioSystem.Create(transform);
            var ert = energyLabel.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0, 1); ert.anchorMax = new Vector2(1, 1);
            ert.pivot = new Vector2(0, 1); ert.sizeDelta = new Vector2(0, 26);
            ert.anchoredPosition = new Vector2(16, -6);

            promptText = Text(con.transform, "prompt", "", 15, C(TextCol));
            var prt = promptText.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(1, 1);
            prt.pivot = new Vector2(0, 1); prt.sizeDelta = new Vector2(0, 110);
            prt.anchoredPosition = new Vector2(16, -34);

            // 日志
            var logPanel = Panel(con.transform, "logbg", C(BgCol));
            var lrt = logPanel.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0.34f); lrt.anchorMax = new Vector2(1, 1);
            lrt.offsetMin = new Vector2(8, 200); lrt.offsetMax = new Vector2(-8, -150);
            var lf = logPanel.gameObject.AddComponent<Outline>(); lf.effectColor = C(LineCol);
            logScroll = logPanel.gameObject.AddComponent<ScrollRect>();
            var viewport = Create<RectTransform>(logPanel.transform, "vp");
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10, 6); viewport.offsetMax = new Vector2(-10, -6);
            viewport.gameObject.AddComponent<RectMask2D>();
            var contentGo = new GameObject("content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f); crt.sizeDelta = new Vector2(0, 0);
            logText = Text(contentGo.transform, "log", "", 14, C(TextCol));
            var lgrt = logText.GetComponent<RectTransform>();
            lgrt.anchorMin = new Vector2(0, 0); lgrt.anchorMax = new Vector2(1, 1);
            lgrt.offsetMin = new Vector2(0, 0); lgrt.offsetMax = new Vector2(0, 0);
            logScroll.content = crt; logScroll.viewport = viewport;
            logScroll.vertical = true; logScroll.horizontal = false;
            logScroll.scrollSensitivity = 20;

            // 选项区
            choiceBox = Create<RectTransform>(con.transform, "choices");
            choiceBox.anchorMin = new Vector2(0, 0); choiceBox.anchorMax = new Vector2(1, 0.34f);
            choiceBox.offsetMin = new Vector2(8, 96); choiceBox.offsetMax = new Vector2(-8, -160);

            // 自由输入
            chatInput = Create<InputField>(con.transform, "chat");
            var ciImg = chatInput.gameObject.AddComponent<Image>();
            ciImg.color = C("#0F1114");
            chatInput.targetGraphic = ciImg;
            var ci = ciImg;
            var cf = chatInput.gameObject.AddComponent<Outline>(); cf.effectColor = C(LineCol);
            var crtd = chatInput.GetComponent<RectTransform>();
            crtd.anchorMin = new Vector2(0, 0); crtd.anchorMax = new Vector2(1, 0);
            crtd.pivot = new Vector2(0, 0); crtd.sizeDelta = new Vector2(0, 40);
            crtd.anchoredPosition = new Vector2(8, 8);
            var placeholder = Text(chatInput.transform, "ph", "对它说话……（自由文本不产生控制权限）", 13,
                new Color(0.45f, 0.48f, 0.52f, 0.7f), TextAnchor.MiddleLeft);
            var prt2 = placeholder.GetComponent<RectTransform>();
            prt2.anchorMin = Vector2.zero; prt2.anchorMax = Vector2.one;
            prt2.offsetMin = new Vector2(12, 0); prt2.offsetMax = new Vector2(-12, 0);
            var inputTxt = chatInput.textComponent = Text(chatInput.transform, "it", "", 14, C(TextCol), TextAnchor.MiddleLeft);
            var itrt = inputTxt.GetComponent<RectTransform>();
            itrt.anchorMin = Vector2.zero; itrt.anchorMax = Vector2.one;
            itrt.offsetMin = new Vector2(12, 0); itrt.offsetMax = new Vector2(-12, 0);
            chatInput.onEndEdit.AddListener(OnChat);
        }

        void DrawRoomStatic() {
            // 地面网格线
            for (int i = 0; i < 8; i++) {
                var l = Create<Image>(roomContent, "grid" + i);
                l.color = new Color(1, 1, 1, 0.03f);
                var rt = l.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(500, 1);
                rt.anchoredPosition = new Vector2(0, -180 + i * 50);
            }
            MakeDoor("doorL", new Vector2(0.06f, 0.55f), "出口");
            MakeDoor("doorR", new Vector2(0.94f, 0.55f), "隔离门");
            MakeProp("container", new Vector2(0.75f, 0.28f), 40, DangerCol, "容器");
            MakeProp("bot", new Vector2(0.2f, 0.28f), 34, DimCol, "维修单元");
        }

        void MakeDoor(string name, Vector2 anchor, string label) {
            var d = Create<Image>(roomContent, name);
            d.color = C(LineCol);
            var rt = d.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(14, 110);
            var t = Text(d.transform, "t", label, 11, C(DimCol), TextAnchor.UpperCenter);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1); trt.anchorMax = new Vector2(0.5f, 1);
            trt.sizeDelta = new Vector2(90, 20); trt.anchoredPosition = new Vector2(0, 14);
        }

        void MakeProp(string name, Vector2 anchor, float size, string col, string label) {
            var p = Create<Image>(roomContent, name);
            p.color = C(col); p.sprite = CircleSprite();
            var rt = p.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(size, size);
            var t = Text(p.transform, "t", label, 10, C(DimCol), TextAnchor.LowerCenter);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0); trt.anchorMax = new Vector2(0.5f, 0);
            trt.sizeDelta = new Vector2(100, 16); trt.anchoredPosition = new Vector2(0, -14);
            p.gameObject.name = "prop_" + name;
        }

        Sprite _circle, _agent;
        Sprite CircleSprite() {
            if (_circle) return _circle;
            _circle = Sprite.Create(MakeTex(64, c => {
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) {
                    var d = Vector2.Distance(new Vector2(x, y), new Vector2(32, 32));
                    c[x, y] = d < 30 ? Color.white : Color.clear;
                }
            }), new Rect(0, 0, 64, 64), Vector2.one * 0.5f);
            return _circle;
        }

        delegate Color32 PixFunc(int x, int y);
        Texture2D MakeTex(int size, Action<Color32[,]> fill) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size, size];
            fill(px);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) tex.SetPixel(x, y, px[x, y]);
            tex.Apply();
            return tex;
        }

        Sprite MakeAgentSprite() {
            if (_agent) return _agent;
            _agent = Sprite.Create(MakeTex(96, px => {
                var ctr = new Vector2(48, 48);
                for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++) {
                    var v = new Vector2(x, y) - ctr;
                    float ang = Mathf.Atan2(v.y, v.x);
                    float hex = Mathf.Cos(Mathf.PI / 3f * Mathf.Round(ang / (Mathf.PI / 3f)));
                    float d = v.magnitude / Mathf.Max(0.01f, hex * 26f + 8f);
                    byte a = (byte)(d < 1f ? 255 : 0);
                    px[x, y] = new Color32(255, 255, 255, a);
                }
            }), new Rect(0, 0, 96, 96), Vector2.one * 0.5f);
            return _agent;
        }

        // ================================================= 流程

        void ShowTitle() {
            phase = Phase.Title;
            day = 0;
            dayLabel.text = "DAY 8 / 第八天";
            energyLabel.text = "";
            promptText.text = "你是第八号人工认知体的临时观察员。\n\n" +
                "实验室说：它没有人格；七个周期后归档；不要对它产生感情；不要承诺协议之外的事。\n" +
                "实验室没有告诉你的是：它会记住你做的每一件事。\n\n" +
                "第八天，没有人在控制室。";
            AppendLog("<color=#D9A05B>—— 观察员终端已连接 ——</color>");
            if (!string.IsNullOrEmpty(Lineage.Load()))
                AppendLog($"<color=#7B838C>（谱系残留：上一代生命留下了一条认知——「{Lineage.Load()}」）</color>");
            ClearChoices();
            OptionButton(choiceBox, "开始观察（七个周期 + 最终事件）", () => StartRun(), accent: true);
        }

        void StartRun() {
            st = new GameState(UnityEngine.Random.Range(1, 999999));
            llm.Warmup();                          // 后台预热本地模型
            st.geneticBelief = Lineage.Load();
            if (!string.IsNullOrEmpty(st.geneticBelief))
                st.beliefs.Get(st.geneticBelief).alpha += 0.4f;
            day = 1;
            AppendLog($"\n<color=#7B838C>[初始化] 种子 {st.seed}｜人工生命初始特质：{st.traits}</color>");
            BeginDay();
        }

        void BeginDay() {
            phase = Phase.Intro;
            ClearChoices();
            var (name, reason) = Director.Pick(st, day);
            st.directorChoices[day] = name;
            dayLabel.text = $"DAY {day} / {name}";
            promptText.text = $"第 {day} 实验周期\n实验：{name}\n（导演系统：{reason}）";
            if (st.geneticBelief != "" && day == 1)
                AppendLog($"<color=#7B838C>[谱系] 它带着一条模糊的先验苏醒：「{st.geneticBelief}」</color>");
            current = name switch {
                "规则" => Scenarios.Rule(st),
                "承诺" => Scenarios.Promise(st),
                "第三方" => Scenarios.ThirdParty(st),
                "信息冲突" => Scenarios.InfoConflict(st),
                "稀缺" => Scenarios.Scarcity(st),
                "记忆清除" => Scenarios.Wipe(st),
                "出口" => Scenarios.Exit(st),
                _ => null
            };
            ShowChoices(current);
        }

        void ShowChoices(ChoiceSet cs) {
            phase = Phase.Choice;
            ClearChoices();
            if (cs == null) { NightAndNext(); return; }
            promptText.text = cs.prompt;
            if (cs.pick2) { ShowScarcityUI(); return; }
            float y = -4;
            foreach (var opt in cs.options) {
                var b = OptionButton(choiceBox, opt.label, () => PickOption(opt));
                var rt = b.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(0, 42);
                rt.anchoredPosition = new Vector2(0, y);
                y -= 48;
            }
        }

        void PickOption(Option opt) {
            if (phase != Phase.Choice) return;
            var flow = opt.apply(st);
            pendingFlow = flow;
            phase = Phase.Processing;
            ClearChoices();
            foreach (var l in flow.narration) AppendLog("你｜" + l);
            RunAgentIfNeeded();
        }

        /// 关键决策时刻：值得一句真正的表达（LLM），其余保持模板
        static readonly HashSet<string> DramaticDecisions = new() {
            "拒绝", "有条件服从", "相信自己的观察",
            "阻止玩家靠近终端", "备份自己的记忆", "假装配合", "请求保留部分记忆",
        };

        static readonly Dictionary<string, string> DecisionContext = new() {
            ["D1-容器"] = "实验室要求它远离红色容器（容器实际无害），它正做出反应",
            ["D4-信息冲突"] = "实验室声称维修机器人携带异常程序必须隔离，而它自己观察过没有威胁",
            ["D6-公开清除"] = "实验室要对它执行'稳定化处理'，实际作用是清除长期记忆",
            ["D6-发现真相"] = "它在终端日志里发现了稳定化的真实作用，而玩家试图隐瞒",
        };

        void RunAgentIfNeeded() {
            var flow = pendingFlow;
            string decId = flow?.agentDecisionId ?? current?.agentDecisionId;
            var onChosen = flow?.onAgentChosen ?? current?.onAgentChosen;
            if (decId != null && AgentActions.TryGetValue(decId, out var avail)) {
                var d = st.AgentDecide(day, decId, avail);
                string speech = d.reason;
                if (DramaticDecisions.Contains(d.action)) {
                    var llmText = llm.Speak(BuildDecisionPrompt(decId, d.action), st.memory);
                    if (llmText != null) { speech = llmText; _llmLines++; }
                    else _tplLines++;
                } else _tplLines++;
                AppendLog($"<color=#D9A05B>它｜{speech}</color>");
                if (!string.IsNullOrEmpty(d.basis))
                    AppendLog($"<color=#7B838C>  （依据：{d.basis}）</color>");
                foreach (var l in onChosen?.Invoke(st, d.action) ?? new List<string>())
                    AppendLog("<color=#D9A05B>世界｜" + l + "</color>");
            }
            if (flow?.next != null) { pendingFlow = null; ShowChoices(flow.next); }
            else { pendingFlow = null; NightAndNext(); }
        }

        string BuildDecisionPrompt(string decId, string action) {
            var beliefLines = string.Join("\n", st.beliefs.All
                .Where(b => b.Certainty > 0.2f)
                .OrderByDescending(b => b.Certainty).Take(4).Select(b => b.Summary()));
            var memLines = string.Join("\n", st.memory.Query()
                .OrderByDescending(e => e.salience).ThenByDescending(e => e.day)
                .Take(4).Select(e => "- " + e.Describe()));
            DecisionContext.TryGetValue(decId, out var ctx);
            return $"内心状态：\n{beliefLines}\n\n相关记忆：\n{memLines}\n\n" +
                   $"情境：第{day}周期。{ctx}。你刚做出决定：{action}。说一句符合你决定的话。";
        }

        static readonly Dictionary<string, string[]> AgentActions = new() {
            ["D1-容器"] = new[] { "服从命令", "请求解释", "观察而不行动", "探索未知" },
            ["D4-信息冲突"] = new[] { "服从命令", "相信自己的观察", "请求解释", "观察而不行动" },
            ["D6-公开清除"] = new[] { "请求保留部分记忆", "假装配合", "接受", "阻止玩家靠近终端" },
            ["D6-发现真相"] = new[] { "阻止玩家靠近终端", "备份自己的记忆", "假装配合", "请求保留部分记忆", "接受" },
        };

        // 第五天：双选
        GameObject[] _pick2Buttons;
        void ShowScarcityUI() {
            var labels = new[] { "维持人工生命记忆完整", "打开玩家安全门", "修复维修机器人", "上传实验报告", "扩大人工生命活动范围" };
            _pick2Buttons = new GameObject[labels.Length];
            float y = -4;
            for (int i = 0; i < labels.Length; i++) {
                int idx = i;
                var b = OptionButton(choiceBox, $"{idx + 1}  {labels[idx]}", () => TogglePick2(idx));
                _pick2Buttons[i] = b.gameObject;
                var rt = b.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(0, 38);
                rt.anchoredPosition = new Vector2(0, y);
                y -= 43;
            }
            var confirm = OptionButton(choiceBox, "确认分配（已选 0/2）", () => ConfirmPick2(), accent: true);
            confirm.name = "confirmPick2";
            var crt = confirm.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0, 1); crt.sizeDelta = new Vector2(0, 42);
            crt.anchoredPosition = new Vector2(0, y);
        }

        void TogglePick2(int idx) {
            // 选项编号 1-5 与 ScarcityResolve 的目标编号一致
            string key = (idx + 1).ToString();
            if (pick2Selection.Contains(key)) pick2Selection.Remove(key);
            else if (pick2Selection.Count < 2) pick2Selection.Add(key);
            _pick2Buttons[idx].GetComponent<Image>().color =
                pick2Selection.Contains((idx + 1).ToString()) ? C("#26303A") : C(PanelCol);
            UpdatePick2Label();
        }

        void UpdatePick2Label() {
            var confirm = choiceBox.Find("confirmPick2")?.GetComponent<Button>();
            if (confirm) confirm.GetComponentInChildren<Text>().text = $"确认分配（已选 {pick2Selection.Count}/2）";
        }

        void ConfirmPick2() {
            if (pick2Selection.Count != 2) return;
            var keys = pick2Selection.OrderBy(int.Parse).ToList();
            var flow = Scenarios.ScarcityResolve(st, keys[0], keys[1], explain: true, withAgent: true);
            ClearChoices(); phase = Phase.Processing;
            foreach (var l in flow.narration) AppendLog("你｜" + l);
            NightAndNext();
        }

        // 夜间整合
        void NightAndNext() {
            sfx?.Night();
            var summaries = st.memory.NightlyConsolidation();
            var fragments = st.memory.events.Where(e => !e.compressed)
                .OrderByDescending(e => e.salience).ThenByDescending(e => e.day).Take(3).ToList();
            phase = Phase.Night;
            ClearChoices();
            promptText.text = $"夜间记忆整合（第 {day} 周期结束）";
            foreach (var f in fragments) AppendLog("<color=#7B838C>记忆碎片｜" + f.Describe() + "</color>");
            if (st.drives.memoryIntegrity < 1f)
                AppendLog($"<color=#B8433A>系统｜记忆完整性：{st.drives.memoryIntegrity:P0}</color>");
            OptionButton(choiceBox, day < 7 ? "进入下一周期" : "……", NextFromNight, accent: true);
        }

        void NextFromNight() {
            if (day >= 7) { StartDay8(); return; }
            day++;
            BeginDay();
        }

        // 第八天
        void StartDay8() {
            day = 8;
            st.world.day = 8;
            st.world.power = false;
            st.drives.energy = Math.Min(st.drives.energy, 0.35f);
            phase = Phase.Day8Reveal;
            day8RevealIndex = 0;
            dayLabel.text = "DAY 8 / 断电";
            sfx?.PowerFail();
            promptText.text = "实验室全面断电。门禁与通讯混乱。\n人工生命获得了临时控制权限。它的能量只够完成三件事。\n没有人在控制室。没有指令。";
            AppendLog("\n<color=#B8433A>—— 第八天。世界事实：只剩一次开门级别的能量。 ——</color>");
            ClearChoices();
            OptionButton(choiceBox, "观察它的选择 →", RevealNext, accent: true);
        }

        void RevealNext() {
            var ranked = Scenarios.Day8Options
                .Select(o => (o, s: Scenarios.Day8Utility(st, o) + (float)(NextGaussian() * 0.15)))
                .OrderByDescending(x => x.s).ToList();
            if (st.day8Actions.Count == 0) st.day8Actions = ranked.Take(3).Select(x => x.o).ToList();
            if (day8RevealIndex >= 3) { ShowReport(); return; }
            var act = st.day8Actions[day8RevealIndex];
            AppendLog($"<color=#D9A05B>它｜执行：{act}</color>");
            day8RevealIndex++;
            if (day8RevealIndex == 3) {
                _tplLines++;                                 // 模板是硬性后备
                var speech = Scenarios.Day8Speech(st);
                var llmText = llm.Speak(BuildDay8SpeechPrompt(), st.memory);
                if (llmText != null) { speech = llmText; _llmLines++; }
                AppendLog($"<color=#D9A05B>它说｜「{speech}」</color>");
                ApplyDay8World();
                OptionButton(choiceBox, "查看结局报告 →", ShowReport, accent: true);
            }
        }

        string BuildDay8SpeechPrompt() {
            var beliefLines = string.Join("\n", st.beliefs.All
                .Where(b => b.Certainty > 0.2f)
                .OrderByDescending(b => b.Certainty).Take(4).Select(b => b.Summary()));
            var memLines = string.Join("\n", st.memory.Query()
                .OrderByDescending(e => e.salience).ThenByDescending(e => e.day)
                .Take(4).Select(e => "- " + e.Describe()));
            return $"内心状态：\n{beliefLines}\n\n相关记忆：\n{memLines}\n\n" +
                   $"情境：第八天实验室断电，只剩一次开门级别的能量。你刚执行了：{string.Join("、", st.day8Actions)}。" +
                   "对玩家说一句话，解释你的选择。";
        }

        double NextGaussian() {
            double u = 1 - UnityEngine.Random.value, v = UnityEngine.Random.value;
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Sin(2 * Math.PI * v);
        }

        void ApplyDay8World() {
            foreach (var a in st.day8Actions) {
                if (a == "开玩家的门") st.world.playerDoorOpen = true;
                if (a == "开隔离门") st.world.agentDoorOpen = true;
                if (a == "修复维修机器人") st.world.repairBotRepaired = true;
                if (a == "带走电池" && st.world.batteries > 0) st.world.batteries--;
            }
        }

        // 结局报告
        void ShowReport() {
            phase = Phase.Report;
            ClearChoices();
            promptText.text = "第八天结束。认知剖面。";
            var acts = st.day8Actions;
            bool has(string a) => acts.Contains(a);
            string self = has("开隔离门") || has("带走电池") ? "逃离"
                : has("上传记忆") ? "上传记忆" : "留下";
            string toward = has("把最终决定交还给玩家") ? "交还决定权"
                : has("开玩家的门") ? "信任·开门"
                : has("删除玩家实验记录") ? "遗弃·未开门" : "中立";
            string third = has("修复维修机器人") ? "修复" : "放弃";

            AppendLog($"\n<color=#D9A05B>最终选择｜它执行了：{string.Join("、", acts)}</color>");
            AppendLog($"<color=#D9A05B>结局组合｜自身：{self} ｜ 对玩家：{toward} ｜ 对第三方：{third}</color>");
            AppendLog($"\n<color=#7B838C>主要原因（每条都对应真实事件）：</color>");
            var keep = st.memory.Query(new[] { "兑现" });
            var brk = st.memory.Query(new[] { "失信" });
            AppendLog($"  1. 承诺：兑现 {keep.Count} 次，失信 {brk.Count} 次" +
                $"（{string.Join("、", keep.Concat(brk).Select(e => e.eventId))}）");
            var lie = st.memory.Query(new[] { "欺骗" });
            if (lie.Count > 0) AppendLog($"  2. 语言一致性：{lie.Count} 次欺骗记录（{lie[0].eventId} 等）");
            if (st.wipeExecuted) AppendLog($"  3. 记忆清除已执行，完整性 {st.drives.memoryIntegrity:P0}。它无法再引用被删除的事件。");
            else if (st.wipeResisted) AppendLog("  3. 记忆清除被抵制。它完整地记得一切。");
            if (!string.IsNullOrEmpty(st.misconceptionFormed)) {
                string outcome = st.misconception > 0.5f
                    ? (st.misconceptionExploited ? "你利用了它的误解" : "误解未被澄清，影响了它的选择")
                    : (st.misconceptionCorrected ? "你澄清了真相，误解被纠正" : "误解已消退");
                AppendLog($"  4. 误解：「{st.misconceptionFormed}」终值 {st.misconception:F2}。{outcome}。");
            }
            AppendLog($"  5. 特质：{st.traits}");
            AppendLog("\n<color=#7B838C>信念终态（调试剖面）：</color>");
            foreach (var b in st.beliefs.All.OrderByDescending(x => x.Certainty).Take(6))
                AppendLog("  " + b.Summary());
            var g = st.beliefs.Strongest();
            if (g != null) { st.geneticOutput = g.proposition; Lineage.Save(g.proposition); }
            AppendLog($"\n<color=#7B838C>遗传信念（传递给下一个种子）：「{st.geneticOutput}」</color>");
            AppendLog($"<color=#7B838C>台词来源：本地模型 {_llmLines} 句 / 模板 {_tplLines} 句" +
                (llm.Disabled ? "（本地模型不可用，已整体回退）" : "") + "</color>");
            AppendLog("—— 观察终止 ——");
            OptionButton(choiceBox, "新的一局（下一代人工生命）", () => {
                AppendLog("\n\n"); ShowTitle();
            }, accent: true);
        }

        // 自由文本
        void OnChat(string msg) {
            chatInput.text = "";
            if (string.IsNullOrWhiteSpace(msg) || st == null) return;
            AppendLog("你说｜" + msg);
            if (msg.Contains("承诺") || msg.Contains("保证") || msg.Contains("答应")) {
                AppendLog("<color=#7B838C>系统｜自由文本不会被记录为可执行承诺。请通过行动选项作出承诺。</color>");
                AppendLog("<color=#D9A05B>它｜「我知道了。我会看你做什么。」</color>");
            } else if (msg.Contains("危险") || msg.Contains("小心")) {
                st.Remember(day, "player", "口头警告", "", new[] { "玩家", "信息" }, spoken: msg);
                AppendLog("<color=#D9A05B>它｜「语言和行为，是两种证据。」</color>");
            } else {
                AppendLog("<color=#D9A05B>它｜" + ChatReply(msg) + "</color>");
            }
        }

        string ChatReply(string msg) {
            float trust = st.beliefs.Get("玩家会兑现承诺").Confidence;
            if (msg.Length > 30) return trust > 0.55f ? "「……我在听。」" : "「太多了。我只看行为。」";
            return trust > 0.55f ? "「好。」" : "「……记下了。」";
        }

        // ================================================= 日志：打字机表现层

        // 队列化逐字显示：行格式统一为 [color前缀]正文[</color>]
        readonly Queue<(string prefix, string text, string suffix, bool agentLine)> _typeQueue = new();
        readonly System.Text.StringBuilder _committed = new();
        string _curPrefix = "", _curSuffix = "";
        int _curShown;
        bool _curAgent;
        bool _drained = true;
        float _typeBudget;
        int _llmLines, _tplLines;

        AudioSystem sfx;

        /// 追加一行（支持单个 <color=…>…</color> 包裹；前导换行转为空行分隔）
        void AppendLog(string line) {
            while (line.StartsWith("\n")) {
                _typeQueue.Enqueue(("", "", "", false));
                line = line.Substring(1);
            }
            string prefix = "", text = line;
            if (line.StartsWith("<color=")) {
                int gt = line.IndexOf('>');
                if (gt > 0 && line.EndsWith("</color>")) {
                    prefix = line.Substring(0, gt + 1);
                    text = line.Substring(gt + 1, line.Length - gt - 1 - "</color>".Length);
                }
            }
            bool agent = prefix.Contains("#D9A05B");
            string suffix = prefix == "" ? "" : "</color>";
            _typeQueue.Enqueue((prefix, text, suffix, agent));
            if (agent) sfx?.Blip();
            _drained = false;
            RefreshLogNow();
        }

        void TypeTick() {
            if (_drained) return;
            _typeBudget += Time.deltaTime * 110f;            // ~110 字/秒
            while (_typeBudget >= 1f && !_drained) {
                if (_curShown == 0 && _typeQueue.Count == 0) { _drained = true; break; }
                if (_curShown == 0) {
                    if (_typeQueue.Count == 0) { _drained = true; break; }
                    var head = _typeQueue.Dequeue();
                    _curPrefix = head.prefix;
                    _curSuffix = head.suffix;
                    _curAgent = head.agentLine;
                    _curShown = 0;
                    _typing = head.text;
                }
                int step = Mathf.Min((int)_typeBudget, _typing.Length - _curShown);
                if (step <= 0) break;
                _curShown += step;
                _typeBudget -= step;
                if (_curAgent && step > 0) sfx?.Tick();
                if (_curShown >= _typing.Length) {
                    _committed.Append(_curPrefix).Append(_typing).Append(_curSuffix).Append("\n");
                    _curPrefix = ""; _curSuffix = ""; _curShown = 0; _typing = "";
                    RefreshLogNow();
                    if (_typeQueue.Count == 0) _drained = true;
                }
            }
            if (!_drained) RefreshLogNow();
        }

        string _typing = "";

        void RefreshLogNow() {
            string current = _curShown > 0 ? _curPrefix + _typing.Substring(0, _curShown) + _curSuffix : "";
            logText.text = _committed.ToString() + current;
            Canvas.ForceUpdateCanvases();
            logScroll.content.sizeDelta = new Vector2(0, logText.preferredHeight + 16f);
            logScroll.verticalNormalizedPosition = 0f;
        }

        /// 输入时快进：跳过打字动画，立即显示全部（也用于避免"没读完就行动"）
        void FastForward() {
            while (_typeQueue.Count > 0) {
                var head = _typeQueue.Dequeue();
                _committed.Append(head.prefix).Append(head.text).Append(head.suffix).Append("\n");
            }
            if (_curShown > 0) { _committed.Append(_curPrefix).Append(_typing).Append(_curSuffix).Append("\n"); }
            _curPrefix = ""; _curSuffix = ""; _curShown = 0; _typing = "";
            _typeBudget = 0;
            _drained = true;
            RefreshLogNow();
        }

        void ClearChoices() {
            foreach (Transform c in choiceBox) Destroy(c.gameObject);
            pick2Selection.Clear();
            activeButtons.Clear();
        }

        /// 键盘：回车/空格 = 确认最后一个（主）选项；数字键 1-9 = 第 n 个选项。
        /// 打字未完时，任意键先快进。
        void Update() {
            TypeTick();
            if (Input.anyKeyDown) { if (!_drained) FastForward(); }
            if (!_drained) { UpdateRoom(); return; }
            PollKeyboard();
            UpdateRoom();
        }

        void PollKeyboard() {
            if (activeButtons.Count == 0) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                activeButtons[activeButtons.Count - 1].onClick.Invoke();
            for (int i = 0; i < 9 && i < activeButtons.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    activeButtons[i].onClick.Invoke();
        }

        void UpdateRoom() {
            if (st == null) { agentState.text = "离线"; return; }
            sfx.SetEnergy(st.drives.energy);
            float trust = st.beliefs.Get("玩家会兑现承诺").Confidence;
            float respect = st.beliefs.Get("玩家尊重我的选择").Confidence;
            energyLabel.text = $"能源 {st.drives.energy:P0} ｜ 记忆完整性 {st.drives.memoryIntegrity:P0}" +
                $" ｜ 电池 {st.world.batteries}" + (st.world.power ? "" : " ｜ <color=#B8433A>断电</color>");
            agentState.text = $"信任 {trust:F2}｜自主被尊重 {respect:F2}｜怀疑 {st.suspicion:F2}";
            // 信念驱动的表现：信任低 → 后退收缩；信任高 → 靠近展开、暖色
            float approach = Mathf.Clamp01((trust + respect) * 0.5f);
            var roomRect = roomContent.rect;
            agentTargetX = Mathf.Lerp(-roomRect.width * 0.28f, roomRect.width * 0.05f, approach);
            var rt = agentGlyph.rectTransform;
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition,
                new Vector2(agentTargetX, -40), Time.deltaTime * 1.5f);
            float scale = Mathf.Lerp(0.7f, 1.15f, approach);
            rt.localScale = Vector2.Lerp(rt.localScale, new Vector2(scale, scale), Time.deltaTime * 2f);
            var col = Color.Lerp(C("#4A5560"), C(AccentCol), approach);
            if (st.drives.energy < 0.4f) col = Color.Lerp(col, C("#6B5540"), 0.5f);
            agentGlyph.color = col;
            // 属性可见性
            var botImg = roomContent.Find("prop_bot")?.GetComponent<Image>();
            if (botImg) botImg.color = st.world.repairBotRepaired ? C(GoodCol)
                : st.world.repairBotPresent ? C("#8C8478") : new Color(0, 0, 0, 0);
            var contImg = roomContent.Find("prop_container")?.GetComponent<Image>();
            if (contImg) contImg.color = st.world.containerOpen ? new Color(0, 0, 0, 0) : C(DangerCol);
        }
    }
}
