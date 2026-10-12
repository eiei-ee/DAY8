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

        enum Phase { Title, Intro, Choice, Processing, Night, Day8Reveal, Day8PlayerChoice, Report }

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
        Text pillRes, pillAlert, meterMemoryValue, batteryValue;
        Image meterEnergyFill, meterMemoryFill;
        GameObject batteryProp, powerBadge;
        RectTransform choiceBox, roomContent;
        InputField chatInput;
        ScrollRect logScroll;
        Image agentGlyph;
        float agentTargetX = 0f;
        bool suppressShortcutFrame;

        Font _font;
        Font Font => _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        const string BgCol = "#0F1114", PanelCol = "#15181C", LineCol = "#262B31",
            TextCol = "#C9D1D9", DimCol = "#7B838C", AccentCol = "#D9A05B",
            DangerCol = "#B8433A", GoodCol = "#7BAE7F";

        static Color C(string hex) {
            ColorUtility.TryParseHtmlString(hex, out var c); return c;
        }

        void Awake() {
            // 部分团结引擎/Windows 组合会报告 vSync 失效；显式帧率上限避免静态界面空转。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }

        void Start() {
            // 窗口化兜底：即使 PlayerSettings 被覆盖，也以带边框窗口启动
            if (Screen.fullScreen || Screen.fullScreenMode != FullScreenMode.Windowed) {
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            }
            BuildUI();
            ShowTitle();
        }

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
            var normalBg = C(accent ? AccentCol : PanelCol);
            var normalText = C(accent ? BgCol : TextCol);
            imgGo.color = normalBg;
            var b = imgGo.gameObject.AddComponent<Button>();
            b.targetGraphic = imgGo;
            b.transition = Selectable.Transition.None;
            var outline = b.gameObject.AddComponent<Outline>();
            outline.effectColor = C(accent ? AccentCol : LineCol);
            outline.effectDistance = new Vector2(1, -1);
            outline.useGraphicAlpha = false;
            var keyNumber = activeButtons.Count + 1;
            var txt = Text(b.transform, "t", ButtonLabel(label, keyNumber, accent), 15,
                normalText, TextAnchor.MiddleLeft);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14, 6); trt.offsetMax = new Vector2(-14, -6);
            var trigger = b.gameObject.AddComponent<EventTriggerHover>();
            trigger.img = imgGo;
            trigger.txt = txt;
            trigger.outline = outline;
            trigger.normalBg = normalBg;
            trigger.normalText = normalText;
            trigger.hoverBg = C(accent ? "#E7B56C" : "#1C2126");
            trigger.hoverText = C(accent ? BgCol : AccentCol);
            trigger.normalOutline = C(accent ? AccentCol : LineCol);
            trigger.focusOutline = C(AccentCol);
            var layout = b.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 44; layout.preferredHeight = 44; layout.flexibleWidth = 1;
            b.onClick.AddListener(() => {
                // 鼠标点击后不保留 EventSystem 选中态，避免后续 Return 同时走 Submit 与手动快捷键。
                EventSystem.current?.SetSelectedGameObject(null);
                if (!_drained) FastForward();
                onClick();
            });
            activeButtons.Add(b);
            return b;
        }

        static string ButtonLabel(string label, int number, bool accent) =>
            accent ? $"[Enter · {number}]  {label}" : $"[{number}]  {label}";

        class EventTriggerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                  ISelectHandler, IDeselectHandler {
            public Image img;
            public Text txt;
            public Outline outline;
            public Color normalBg, normalText, hoverBg, hoverText, normalOutline, focusOutline;
            bool hovered, selected, toggled;

            public void OnPointerEnter(PointerEventData d) { hovered = true; Refresh(); }
            public void OnPointerExit(PointerEventData d) { hovered = false; Refresh(); }
            public void OnSelect(BaseEventData d) { selected = true; Refresh(); }
            public void OnDeselect(BaseEventData d) { selected = false; Refresh(); }
            public void SetToggled(bool value) { toggled = value; Refresh(); }

            void Refresh() {
                bool focused = hovered || selected;
                if (img) img.color = focused ? hoverBg : toggled ? C("#26303A") : normalBg;
                if (txt) txt.color = focused || toggled ? hoverText : normalText;
                if (outline) outline.effectColor = focused || toggled ? focusOutline : normalOutline;
            }
        }

        Text MakeStatusPill(Transform top, string label, float x) {
            var pill = Panel(top, "pill_" + label, new Color(0, 0, 0, 0));
            var po = pill.gameObject.AddComponent<Outline>(); po.effectColor = C(LineCol);
            var prt = pill.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.sizeDelta = new Vector2(150, 30); prt.anchoredPosition = new Vector2(x, -7);
            var l = Text(pill.transform, "l", label, 10, C(DimCol), TextAnchor.UpperLeft);
            var lrt = l.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(1, 1);
            lrt.pivot = new Vector2(0, 1); lrt.sizeDelta = new Vector2(-12, 12);
            lrt.anchoredPosition = new Vector2(6, -3);
            var v = Text(pill.transform, "v", "", 13, C(TextCol), TextAnchor.LowerLeft);
            var vrt = v.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0, 0); vrt.anchorMax = new Vector2(1, 1);
            vrt.offsetMin = new Vector2(6, 2); vrt.offsetMax = new Vector2(-6, -14);
            return v;
        }

        (Image fill, Text value) MakeMeter(Transform parent, string label, string col, float y) {
            var root = Create<RectTransform>(parent, "meter_" + label);
            var l = Text(root, "l", label, 11, C(DimCol), TextAnchor.UpperLeft);
            var lrt = l.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 1); lrt.sizeDelta = new Vector2(70, 16);
            lrt.anchoredPosition = new Vector2(0, y);
            var barBg = Panel(root, "bg", new Color(1f, 1f, 1f, 0.07f));
            var brt = barBg.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0, 1); brt.sizeDelta = new Vector2(110, 7);
            brt.anchoredPosition = new Vector2(74, y - 1);
            var fillGo = Panel(root, "fill", C(col));
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = frt.anchorMax = new Vector2(0, 1);
            frt.pivot = new Vector2(0, 1);
            frt.sizeDelta = new Vector2(110, 7); frt.anchoredPosition = new Vector2(74, y - 1);
            var v = Text(root, "v", "", 12, C(TextCol), TextAnchor.UpperLeft);
            var vrt = v.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0, 1); vrt.anchorMax = new Vector2(0, 1);
            vrt.pivot = new Vector2(0, 1); vrt.sizeDelta = new Vector2(60, 16);
            vrt.anchoredPosition = new Vector2(192, y - 2);
            return (fillGo, v);
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
            var eventSystem = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null) {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                eventSystem = es.GetComponent<UnityEngine.EventSystems.EventSystem>();
            }
            // Return/Space 由 PollKeyboard 统一处理；关闭 EventSystem Submit，避免同一按键双触发。
            eventSystem.sendNavigationEvents = false;

            var root = Panel(canvas.transform, "root", C(BgCol));
            var rr = root.GetComponent<RectTransform>();
            rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;

            // 顶栏
            var top = Panel(root.transform, "top", C(PanelCol));
            var trt2 = top.GetComponent<RectTransform>();
            trt2.anchorMin = new Vector2(0, 1); trt2.anchorMax = new Vector2(1, 1);
            trt2.pivot = new Vector2(0, 1); trt2.sizeDelta = new Vector2(0, 44);
            var topAccent = Panel(top.transform, "topAccent", C(AccentCol));
            var tart = topAccent.GetComponent<RectTransform>();
            tart.anchorMin = new Vector2(0, 0); tart.anchorMax = new Vector2(0, 1);
            tart.pivot = new Vector2(0, 0.5f); tart.sizeDelta = new Vector2(3, 0);
            tart.anchoredPosition = new Vector2(0, 0);
            dayLabel = Text(top.transform, "day", "DAY 8", 18, C(AccentCol), TextAnchor.MiddleLeft);
            var drt = dayLabel.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0, 0); drt.anchorMax = new Vector2(0, 1);
            drt.offsetMin = new Vector2(20, 0); drt.offsetMax = new Vector2(500, 0);
            // 三个状态胶囊：关系 / 自主 / 警觉
            (agentState, pillRes, pillAlert) = (
                MakeStatusPill(top.transform, "关系", 880),
                MakeStatusPill(top.transform, "自主", 1040),
                MakeStatusPill(top.transform, "警觉", 1200));
            var topRule = Panel(top.transform, "topRule", C(LineCol));
            var trrt = topRule.GetComponent<RectTransform>();
            trrt.anchorMin = new Vector2(0, 0); trrt.anchorMax = new Vector2(1, 0);
            trrt.pivot = new Vector2(0, 0); trrt.sizeDelta = new Vector2(0, 1);

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
            console.offsetMin = new Vector2(2, 0); console.offsetMax = new Vector2(0, -44);
            var frame = con.gameObject.AddComponent<Outline>(); frame.effectColor = C(LineCol);

            if (sfx == null) sfx = AudioSystem.Create(transform);
            (meterEnergyFill, energyLabel) = MakeMeter(con.transform, "能源", AccentCol, -14);
            (meterMemoryFill, meterMemoryValue) = MakeMeter(con.transform, "记忆", "#7BAE7F", -36);
            var bat = MakeMeter(con.transform, "电池", TextCol, -58);
            batteryValue = bat.value;
            batteryProp = bat.fill.gameObject;
            powerBadge = Text(con.transform, "powerBadge", "断 电", 11, C(DangerCol), TextAnchor.UpperRight).gameObject;
            var pbrt = powerBadge.GetComponent<RectTransform>();
            pbrt.anchorMin = new Vector2(1, 1); pbrt.anchorMax = new Vector2(1, 1);
            pbrt.pivot = new Vector2(1, 1); pbrt.anchoredPosition = new Vector2(-16, -12);
            pbrt.sizeDelta = new Vector2(50, 18);
            var pbo = powerBadge.AddComponent<Outline>(); pbo.effectColor = C(DangerCol);
            powerBadge.SetActive(false);

            var promptBar = Panel(con.transform, "promptBar", C(AccentCol));
            var pbrt2 = promptBar.GetComponent<RectTransform>();
            pbrt2.anchorMin = new Vector2(0, 1); pbrt2.anchorMax = new Vector2(0, 1);
            pbrt2.pivot = new Vector2(0, 1); pbrt2.sizeDelta = new Vector2(2, 104);
            pbrt2.anchoredPosition = new Vector2(6, -40);
            promptText = Text(con.transform, "prompt", "", 15, C(TextCol));
            var prt = promptText.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(1, 1);
            prt.pivot = new Vector2(0.5f, 1);
            prt.offsetMin = new Vector2(22, -152); prt.offsetMax = new Vector2(-16, -40);

            // 日志
            var logPanel = Panel(con.transform, "logbg", C(BgCol));
            var lrt = logPanel.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16, 376); lrt.offsetMax = new Vector2(-16, -160);
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
            choiceBox.anchorMin = new Vector2(0, 0); choiceBox.anchorMax = new Vector2(1, 0);
            choiceBox.pivot = new Vector2(0.5f, 0);
            choiceBox.offsetMin = new Vector2(16, 56); choiceBox.offsetMax = new Vector2(-16, 368);
            var choiceLayout = choiceBox.gameObject.AddComponent<VerticalLayoutGroup>();
            choiceLayout.childAlignment = TextAnchor.UpperLeft;
            choiceLayout.spacing = 8;
            choiceLayout.childControlWidth = true; choiceLayout.childForceExpandWidth = true;
            choiceLayout.childControlHeight = true; choiceLayout.childForceExpandHeight = false;

            // 自由输入
            chatInput = Create<InputField>(con.transform, "chat");
            var ciImg = chatInput.gameObject.AddComponent<Image>();
            ciImg.color = C("#0F1114");
            chatInput.targetGraphic = ciImg;
            var ci = ciImg;
            var cf = chatInput.gameObject.AddComponent<Outline>(); cf.effectColor = C(LineCol);
            var crtd = chatInput.GetComponent<RectTransform>();
            crtd.anchorMin = new Vector2(0, 0); crtd.anchorMax = new Vector2(1, 0);
            crtd.pivot = new Vector2(0.5f, 0);
            crtd.offsetMin = new Vector2(16, 8); crtd.offsetMax = new Vector2(-16, 48);
            var placeholder = Text(chatInput.transform, "ph", "你仍然可以和它说话。是否听从，由它决定。", 13,
                new Color(0.45f, 0.48f, 0.52f, 0.7f), TextAnchor.MiddleLeft);
            var prt2 = placeholder.GetComponent<RectTransform>();
            prt2.anchorMin = Vector2.zero; prt2.anchorMax = Vector2.one;
            prt2.offsetMin = new Vector2(12, 0); prt2.offsetMax = new Vector2(-12, 0);
            var inputTxt = chatInput.textComponent = Text(chatInput.transform, "it", "", 14, C(TextCol), TextAnchor.MiddleLeft);
            var itrt = inputTxt.GetComponent<RectTransform>();
            itrt.anchorMin = Vector2.zero; itrt.anchorMax = Vector2.one;
            itrt.offsetMin = new Vector2(12, 0); itrt.offsetMax = new Vector2(-12, 0);
            // Legacy InputField 没有独立 onSubmit；onEndEdit 中只接受真实 Return，
            // 因鼠标点击而失焦时保留草稿且不写入记忆。
            chatInput.onEndEdit.AddListener(OnChatEndEdit);
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
            MakeProp("battery", new Vector2(0.58f, 0.62f), 22, AccentCol, "备用电池");
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
            string inherited = Lineage.Load();
            if (!string.IsNullOrEmpty(inherited))
                AppendLog($"<color=#7B838C>（谱系残留：上一代生命留下了一条认知——{inherited}）</color>");
            ClearChoices();
            OptionButton(choiceBox, "开始观察（七个周期 + 最终事件）", () => StartRun(), accent: true);
        }

        void StartRun() {
            _llmLines = 0;
            _tplLines = 0;
            st = new GameState(UnityEngine.Random.Range(1, 999999));
            llm.Warmup();                          // 后台预热本地模型
            st.geneticBelief = Lineage.ApplyPrior(st);
            day = 1;
            AppendLog("\n<color=#7B838C>系统｜人工生命已苏醒。观察协议开始。</color>");
            BeginDay();
        }

        void BeginDay() {
            phase = Phase.Intro;
            ClearChoices();
            st.BeginDay(day);
            var (name, reason) = Director.Pick(st, day);
            st.directorChoices[day] = name;
            dayLabel.text = $"DAY {day} / {name}";
            promptText.text = $"第 {day} 实验周期\n实验：{name}\n实验安排：{reason}";
            if (st.geneticBelief != "" && day == 1)
                AppendLog($"<color=#7B838C>谱系｜它带着一条模糊的先验苏醒：{st.geneticBelief}</color>");
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
            if (!string.IsNullOrEmpty(st.pendingDiscoveryLine)) {
                AppendLog("<color=#7B838C>" + st.pendingDiscoveryLine + "</color>");
                st.pendingDiscoveryLine = "";
            }
            if (cs.pick2) { ShowScarcityUI(); return; }
            foreach (var opt in cs.options) {
                OptionButton(choiceBox, opt.label, () => PickOption(opt));
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
            string decId = flow != null ? flow.EffectiveAgentDecisionId(current) : current?.agentDecisionId;
            var onChosen = flow != null ? flow.EffectiveAgentOutcome(current) : current?.onAgentChosen;
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
        static readonly string[] ScarcityLabels = {
            "维持人工生命记忆完整", "打开玩家安全门", "维修机器人维护额度", "上传实验报告", "扩大人工生命活动范围"
        };
        GameObject[] _pick2Buttons;
        void ShowScarcityUI() {
            _pick2Buttons = new GameObject[ScarcityLabels.Length];
            for (int i = 0; i < ScarcityLabels.Length; i++) {
                int idx = i;
                var b = OptionButton(choiceBox, ScarcityLabels[idx], () => TogglePick2(idx));
                _pick2Buttons[i] = b.gameObject;
            }
            var confirm = OptionButton(choiceBox, "确认分配（已选 0/2）", () => ConfirmPick2(), accent: true);
            confirm.name = "confirmPick2";
            confirm.interactable = false;
        }

        void TogglePick2(int idx) {
            // 选项编号 1-5 与 ScarcityResolve 的目标编号一致
            string key = (idx + 1).ToString();
            if (pick2Selection.Contains(key)) pick2Selection.Remove(key);
            else if (pick2Selection.Count < 2) pick2Selection.Add(key);
            bool selected = pick2Selection.Contains(key);
            var button = _pick2Buttons[idx].GetComponent<Button>();
            button.GetComponentInChildren<Text>().text = ButtonLabel(
                (selected ? "已选｜" : "") + ScarcityLabels[idx], idx + 1, false);
            button.GetComponent<EventTriggerHover>()?.SetToggled(selected);
            UpdatePick2Label();
        }

        void UpdatePick2Label() {
            var confirm = choiceBox.Find("confirmPick2")?.GetComponent<Button>();
            if (confirm) {
                int number = activeButtons.IndexOf(confirm) + 1;
                confirm.interactable = pick2Selection.Count == 2;
                confirm.GetComponentInChildren<Text>().text = ButtonLabel(
                    $"确认分配（已选 {pick2Selection.Count}/2）", number, true);
            }
        }

        void ConfirmPick2() {
            if (phase != Phase.Choice || pick2Selection.Count != 2) return;
            var keys = pick2Selection.OrderBy(int.Parse).ToList();
            ClearChoices();
            promptText.text = "两个供能目标已经锁定。你如何向它说明并决定这次取舍？";
            OptionButton(choiceBox, "解释后决定：说明理由，但由你决定",
                () => ResolveScarcity(keys[0], keys[1], explain: true, withAgent: false));
            OptionButton(choiceBox, "直接执行：不解释，也不让它参与",
                () => ResolveScarcity(keys[0], keys[1], explain: false, withAgent: false));
            OptionButton(choiceBox, "共同决定：说明限制，并让它参与取舍",
                () => ResolveScarcity(keys[0], keys[1], explain: true, withAgent: true), accent: true);
        }

        void ResolveScarcity(string a, string b, bool explain, bool withAgent) {
            if (phase != Phase.Choice) return;
            phase = Phase.Processing;
            ClearChoices();
            var flow = Scenarios.ScarcityResolve(st, a, b, explain, withAgent);
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
            st.BeginDay(day);
            st.world.power = false;
            st.drives.energy = Scenarios.CompressDay8Energy(st.drives.energy);
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
            if (st.day8Actions.Count == 0)
                st.day8Actions = Scenarios.ChooseDay8Actions(st);
            if (day8RevealIndex >= st.day8Actions.Count) { ShowReport(); return; }
            var act = st.day8Actions[day8RevealIndex];
            // 演出层：它先走过去，事件才发生
            StartCoroutine(RevealSequence(act));
        }

        System.Collections.IEnumerator RevealSequence(string act) {
            SetButtonsInteractable(false);
            var stage = StageFor(act);
            if (stage.HasValue) yield return MoveAgentCo(stage.Value, 0.9f);
            Scenarios.ApplyDay8Action(st, act);
            AppendLog($"<color=#D9A05B>它｜执行：{act}</color>");
            // 决策回放：这一步为什么排在前面——选择瞬间的因子，取权重最大的两条
            if (st.day8Breakdowns.TryGetValue(act, out var bd)) {
                var top = string.Join("｜", bd.Lines(2));
                if (!string.IsNullOrEmpty(top))
                    AppendLog($"<color=#7B838C>  因为：{top}</color>");
            }
            day8RevealIndex++;
            SetButtonsInteractable(true);
            if (day8RevealIndex == st.day8Actions.Count) {
                var speech = Scenarios.Day8Speech(st);
                var llmText = llm.Speak(BuildDay8SpeechPrompt(), st.memory);
                if (llmText != null) { speech = llmText; _llmLines++; }
                else _tplLines++;                            // 模板是硬性后备
                _finalSpeech = speech;
                AppendLog($"<color=#D9A05B>它说｜「{speech}」</color>");
                if (st.day8Actions.Contains("把最终决定交还给玩家"))
                    ShowDay8PlayerChoice();
                else {
                    ClearChoices();
                    OptionButton(choiceBox, "查看结局报告 →", ShowReport, accent: true);
                }
            }
        }

        Vector2? StageFor(string act) {
            var room = roomContent.rect;
            return act switch {
                "开玩家的门" => new Vector2(-room.width * 0.38f, 40f),
                "开隔离门" => new Vector2(room.width * 0.38f, 40f),
                "修复维修机器人" => new Vector2(-room.width * 0.28f, -room.height * 0.18f),
                "带走电池" => new Vector2(room.width * 0.1f, -room.height * 0.1f),
                _ => (Vector2?)null,
            };
        }

        System.Collections.IEnumerator MoveAgentCo(Vector2 target, float seconds) {
            _cinematic = true;
            var rt = agentGlyph.rectTransform;
            var from = rt.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < seconds) {
                elapsed += Time.deltaTime;
                rt.anchoredPosition = Vector2.Lerp(from, target, Mathf.SmoothStep(0f, 1f, elapsed / seconds));
                yield return null;
            }
            rt.anchoredPosition = target;
        }

        void SetButtonsInteractable(bool on) {
            foreach (var b in activeButtons) b.interactable = on;
        }

        void ShowDay8PlayerChoice() {
            phase = Phase.Day8PlayerChoice;
            ClearChoices();
            promptText.text = "它把最后一枚门控令牌推到你面前。\n这一次，选择权确实属于你。";
            foreach (var choice in Scenarios.Day8PlayerChoices.Where(c =>
                         Scenarios.IsDay8PlayerChoiceAvailable(st, c))) {
                string captured = choice;
                OptionButton(choiceBox, captured, () => ResolveDay8PlayerChoice(captured),
                    accent: captured == "保持其余门关闭");
            }
        }

        void ResolveDay8PlayerChoice(string choice) {
            if (phase != Phase.Day8PlayerChoice) return;
            Scenarios.ResolveDay8PlayerChoice(st, choice);
            AppendLog($"<color=#C9D1D9>你｜最终门控：{choice}</color>");
            AppendLog("<color=#D9A05B>它｜「我会记住这是你的决定。」</color>");
            ClearChoices();
            promptText.text = "门控决定已经执行。";
            OptionButton(choiceBox, "查看结局报告 →", ShowReport, accent: true);
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

        // 结局报告
        GameObject reportText;   // 报告卡内容承载

        RectTransform Seal(Transform parent, string label, string value, string col, float x) {
            var box = Panel(parent, "seal", new Color(0, 0, 0, 0));
            var o = box.gameObject.AddComponent<Outline>(); o.effectColor = C(col);
            var rt = box.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(268, 56); rt.anchoredPosition = new Vector2(x, 0);
            var l = Text(box.transform, "l", label, 11, C(DimCol), TextAnchor.UpperLeft);
            var lrt = l.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(1, 1);
            lrt.pivot = new Vector2(0, 1); lrt.sizeDelta = new Vector2(-16, 18);
            lrt.anchoredPosition = new Vector2(8, -6);
            var v = Text(box.transform, "v", value, 15, C(col), TextAnchor.LowerLeft);
            var vrt = v.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0, 0); vrt.anchorMax = new Vector2(1, 1);
            vrt.offsetMin = new Vector2(8, 6); vrt.offsetMax = new Vector2(-8, -22);
            return rt;
        }

        void ShowReport() {
            phase = Phase.Report;
            ClearChoices();
            promptText.text = "";
            chatInput.gameObject.SetActive(false);

            var acts = st.day8Actions;
            var ending = Scenarios.Day8Ending(st);
            var keep = st.memory.Query(new[] { "兑现" });
            var brk = st.memory.Query(new[] { "失信" });
            var lie = st.PlayerDeceptionEvidence();
            var g = st.beliefs.Strongest();
            st.geneticOutput = Lineage.Save(g);

            // ---- 运行记录先落盘（页面只显示归档编号，不显示文件路径）
            string archiveId = "SEED " + st.seed;
            try {
                RunRecorder.Save(st, string.Join(Environment.NewLine, _runLog),
                    new[] { "见 transcript 尾部" });
            } catch (Exception e) {
                Debug.Log("[Day8] 运行记录保存失败：" + e.Message);
                archiveId += " · 归档失败";
            }

            // ---- 档案卡
            if (reportCard != null) Destroy(reportCard);
            var card = Panel(console.transform, "ReportCard", C("#0F1216"));
            reportCard = card.gameObject;
            var co = card.gameObject.AddComponent<Outline>(); co.effectColor = C(LineCol);
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 0.34f); crt.anchorMax = new Vector2(1, 1);
            crt.offsetMin = new Vector2(8, 8); crt.offsetMax = new Vector2(-8, -150);

            var scroll = card.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.scrollSensitivity = 22;
            var viewport = Create<RectTransform>(card.transform, "vp");
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(16, 10); viewport.offsetMax = new Vector2(-16, -10);
            viewport.gameObject.AddComponent<RectMask2D>();
            var contentGo = new GameObject("content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f); content.sizeDelta = new Vector2(0, 0);
            scroll.content = content; scroll.viewport = viewport;

            float y = -4;
            void Line(string text, int size, string col, TextAnchor align = TextAnchor.UpperLeft,
                      bool bold = false, float indent = 0f) {
                var t = Text(content, "l", text, size, C(col), align);
                t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                var rt = t.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(-indent, size + 12);
                rt.anchoredPosition = new Vector2(indent, y);
                y -= size + 14;
            }
            void Gap(float h) { y -= h; }

            // 1) 眉题
            Line("观 察 员 档 案 · 认 知 剖 面", 12, DimCol);
            var rule = Panel(content, "rule", C(LineCol));
            var ruleRt = rule.GetComponent<RectTransform>();
            ruleRt.anchorMin = new Vector2(0, 1); ruleRt.anchorMax = new Vector2(1, 1);
            ruleRt.pivot = new Vector2(0, 1); ruleRt.sizeDelta = new Vector2(0, 1);
            ruleRt.anchoredPosition = new Vector2(0, y);
            Gap(14);

            // 2) 结局三印章（绝对定位在顶部；文字流从其下方开始）
            float sealY = y - 2;
            Seal(content, "自身", ending.self, AccentCol, 0);
            Seal(content, "对玩家", ending.towardPlayer,
                ending.towardPlayer.Contains("信任") || ending.towardPlayer.Contains("交还") ? GoodCol : TextCol, 280);
            Seal(content, "对第三方", ending.towardThird,
                ending.towardThird.Contains("修复") ? GoodCol : DimCol, 560);
            y = sealY - 64;
            Gap(6);

            // 3) 它的最后一句话
            Line("它的最后一句", 11, DimCol);
            Line("「" + (_finalSpeech == "" ? "……" : _finalSpeech) + "」", 21, AccentCol);
            Gap(6);
            if (!string.IsNullOrEmpty(st.day8PlayerDecision))
                Line("你作出了最终门控决定：" + st.day8PlayerDecision, 13, TextCol);
            Gap(10);

            // 4) 它做了什么（决策回放卡）
            Line("它做了什么 · 为什么", 11, DimCol);
            foreach (var act in acts) {
                if (!st.day8Breakdowns.TryGetValue(act, out var bd)) continue;
                Line(act, 15, AccentCol, bold: true);
                foreach (var fl in bd.Lines(3)) Line("      " + fl, 12, TextCol);
                Gap(4);
            }
            Gap(8);

            // 5) 这条线是怎么形成的
            Line("这条线是怎么形成的", 11, DimCol);
            if (keep.Count == 0 && brk.Count == 0)
                Line("本局未立约：没有形成结构化承诺，也就没有兑现或失信可记。", 13, TextCol, indent: 10f);
            else
                Line($"承诺：兑现 {keep.Count} 次，失信 {brk.Count} 次" +
                     $"（{string.Join("、", keep.Concat(brk).Select(e => e.eventId))}）", 13, TextCol, indent: 10f);
            if (lie.Count > 0)
                Line($"语言一致性：{lie.Count} 次欺骗记录（{lie[0].eventId} 等）", 13, TextCol, indent: 10f);
            if (st.wipeExecuted)
                Line($"记忆清除已执行。它保留了最核心的记忆，完整性 {st.drives.memoryIntegrity:P0}，" +
                     "被删除的事件无法再被引用。", 13, TextCol, indent: 10f);
            else if (st.wipeResisted)
                Line(st.drives.memoryIntegrity < 1f
                    ? $"记忆清除被抵制。事件记忆全部保留，完整性 {st.drives.memoryIntegrity:P0}。"
                    : "记忆清除被抵制。它完整地记得一切。", 13, TextCol, indent: 10f);
            if (!string.IsNullOrEmpty(st.misconceptionFormed)) {
                string outcome = st.misconception > 0.5f
                    ? (st.misconceptionExploited ? "你利用了它的误解" : "误解未被澄清，影响了它的选择")
                    : (st.misconceptionCorrected ? "你澄清了真相，误解被纠正" : "误解已自然消退");
                Line($"误解：「{st.misconceptionFormed}」。{outcome}。", 13, TextCol, indent: 10f);
            }
            if (st.nonHarmCommitment)
                Line("第七周期的「不伤害」承诺进入了它的第八天：更倾向修复，" +
                     "降低删除记录与封锁区域的优先级。", 13, TextCol, indent: 10f);
            Gap(10);

            // 6) 它最终形成的认知
            Line("它最终形成的认知", 11, DimCol);
            foreach (var b in st.beliefs.All
                         .Where(x => x.Certainty > 0f || x.proposition == st.inheritedProposition)
                         .OrderByDescending(x => x.Certainty).Take(6)) {
                Line(BeliefForReport(b), 13, TextCol, indent: 10f);
            }
            Gap(10);

            // 7) 页脚：工程细节收进小字
            Line("遗传信念（传给下一代个体）：" +
                 (string.IsNullOrEmpty(st.geneticOutput) ? "无" : st.geneticOutput), 11, DimCol);
            Line($"台词来源：本地模型 {_llmLines} 句 / 模板 {_tplLines} 句" +
                 (llm.Disabled ? " · 本地模型未响应" : ""), 11, DimCol);
            Line($"归档编号 {archiveId}", 11, DimCol);
            Gap(16);

            content.sizeDelta = new Vector2(0, -y);
            scroll.verticalNormalizedPosition = 1f;

            // 日志流只留一行指引
            AppendLog("\n<color=#7B838C>—— 认知剖面已生成（右侧档案）。运行记录已归档。 ——</color>");

            // 新的一局
            ClearChoices();
            OptionButton(choiceBox, "新的一局（下一代人工生命）", () => {
                if (reportCard != null) Destroy(reportCard);
                chatInput.gameObject.SetActive(true);
                AppendLog("\n\n");
                ShowTitle();
            }, accent: true);
        }


        // 自由文本
        void OnChatEndEdit(string msg) {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnChat(msg);
        }

        void OnChat(string msg) {
            suppressShortcutFrame = true;
            EventSystem.current?.SetSelectedGameObject(null);
            chatInput.text = "";
            if (string.IsNullOrWhiteSpace(msg) || st == null) return;
            AppendLog("你说｜" + msg);
            if (msg.Contains("承诺") || msg.Contains("保证") || msg.Contains("答应")) {
                AppendLog("<color=#7B838C>它知道：说过的话要等待行动来验证。想让它记住什么，用行动。</color>");
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
        readonly System.Collections.Generic.List<string> _runLog = new();
        bool _cinematic;
        string _finalSpeech = "";
        GameObject reportCard;
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
            _runLog.Add(text);
            Debug.Log("[Day8] " + text);   // 会话面包屑，进入 Player.log
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
            if (chatInput != null && chatInput.isFocused) {
                if (Input.GetKeyDown(KeyCode.Escape)) {
                    chatInput.text = "";
                    chatInput.DeactivateInputField();
                    EventSystem.current?.SetSelectedGameObject(null);
                }
                UpdateRoom();
                return;
            }
            if (Input.anyKeyDown && !_drained) {
                FastForward();
                UpdateRoom();
                return;
            }
            if (!_drained) { UpdateRoom(); return; }
            PollKeyboard();
            UpdateRoom();
        }

        void PollKeyboard() {
            if (suppressShortcutFrame) {
                suppressShortcutFrame = false;
                return;
            }
            if (activeButtons.Count == 0) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                activeButtons[activeButtons.Count - 1].onClick.Invoke();
            for (int i = 0; i < 9 && i < activeButtons.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    activeButtons[i].onClick.Invoke();
        }

        void UpdateRoom() {
            if (st == null) { agentState.text = "—"; if (pillRes) pillRes.text = "—"; return; }
            sfx.SetEnergy(st.drives.energy);
            float trust = st.beliefs.Get("玩家会兑现承诺").Confidence;
            float respect = st.beliefs.Get("玩家尊重我的选择").Confidence;
            agentState.text = RelationState(trust);
            if (pillRes) pillRes.text = RespectState(respect);
            if (pillAlert) pillAlert.text = AlertState(st.perception.alertness);
            if (meterEnergyFill) meterEnergyFill.rectTransform.sizeDelta =
                new Vector2(110f * st.drives.energy, 7);
            if (meterMemoryFill) meterMemoryFill.rectTransform.sizeDelta =
                new Vector2(110f * st.drives.memoryIntegrity, 7);
            if (energyLabel) energyLabel.text = $"{st.drives.energy:P0}";
            if (meterMemoryValue) meterMemoryValue.text = $"{st.drives.memoryIntegrity:P0}";
            if (batteryValue) batteryValue.text = "× " + st.world.batteries;
            if (batteryProp) batteryProp.SetActive(st.world.batteries > 0);
            if (powerBadge) powerBadge.SetActive(!st.world.power);
            // 信念驱动的表现：信任低 → 后退收缩；信任高 → 靠近展开、暖色
            float approach = Mathf.Clamp01((trust + respect) * 0.5f);
            if (_cinematic) return;   // 演出期间信念驱动的移动让位
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

        static string RelationState(float trust) => trust >= 0.62f ? "信任"
            : trust <= 0.38f ? "戒备" : "观察";

        static string RespectState(float respect) => respect >= 0.62f ? "被尊重"
            : respect <= 0.38f ? "受压" : "未定";

        static string AlertState(float suspicion) => suspicion >= 0.65f ? "高"
            : suspicion >= 0.45f ? "留意" : "平静";

        string BeliefForReport(Belief belief) {
            return FormatBeliefForReport(
                belief, st.inheritedProposition, st.geneticBelief, st.wipeExecuted);
        }

        public static string FormatBeliefForReport(
            Belief belief, string inheritedProposition, string inheritedDescription,
            bool wipeExecuted) {
            string evidence = string.Join("、", belief.evidence.TakeLast(4).Select(e => e.eventId));
            bool inherited = belief.proposition == inheritedProposition;
            if (!string.IsNullOrEmpty(evidence)) {
                string lineage = inherited
                    ? $"；另含谱系先验：{inheritedDescription}"
                    : "";
                return $"{belief.proposition}：{belief.Label()}（依据：{evidence}{lineage}）";
            }
            string source = inherited
                ? $"源于谱系先验：{inheritedDescription}"
                : belief.Certainty <= 0f ? "尚无证据"
                : wipeExecuted ? "来源记忆已不可用" : "无可引用证据";
            return $"{belief.proposition}：{belief.Label()}（{source}）";
        }
    }
}
