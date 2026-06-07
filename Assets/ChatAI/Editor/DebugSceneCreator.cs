#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ChatAI.EditorTools
{
    /// <summary>
    /// 编辑器菜单工具：一键创建完整的 Coze 对话调试场景（含预制 UI）
    /// </summary>
    public static class DebugSceneCreator
    {
        // ==================== 颜色常量 ====================
        private static readonly Color BgColor         = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color ConfigPanelBg   = new Color(0.18f, 0.18f, 0.22f, 0.95f);
        private static readonly Color InputFieldBg    = new Color(0.22f, 0.22f, 0.26f);
        private static readonly Color ButtonBlue      = new Color(0.15f, 0.50f, 0.85f);
        private static readonly Color TextWhite       = Color.white;
        private static readonly Color TextGray        = new Color(0.6f, 0.6f, 0.6f);

        private static Font _font;

        // ==================== 菜单入口 ====================

        [MenuItem("ChatAI/创建调试场景", false, 1)]
        public static void CreateDebugScene()
        {
            _font = GetFont();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // ---- 系统根节点 ----
            var systemRoot = new GameObject("[ChatAI System]");

            var eventCenter = new GameObject("EventCenter");
            eventCenter.transform.SetParent(systemRoot.transform);
            eventCenter.AddComponent<ChatAI.Core.EventCenter>();

            var gameManager = new GameObject("GameManager");
            gameManager.transform.SetParent(systemRoot.transform);
            gameManager.AddComponent<ChatAI.Core.GameManager>();

            var bootstrapper = new GameObject("DebugBootstrapper");
            bootstrapper.transform.SetParent(systemRoot.transform);
            var bsComp = bootstrapper.AddComponent<ChatAI.DebugTools.DebugChatBootstrapper>();

            // 自动查找项目中的 CozeConfig 资源并赋值
            var configGuids = AssetDatabase.FindAssets("t:ChatAI.Core.CozeConfig");
            if (configGuids.Length > 0)
            {
                string configPath = AssetDatabase.GUIDToAssetPath(configGuids[0]);
                var configAsset = AssetDatabase.LoadAssetAtPath<ChatAI.Core.CozeConfig>(configPath);
                if (configAsset != null)
                {
                    var bsSo = new SerializedObject(bsComp);
                    bsSo.FindProperty("cozeConfig").objectReferenceValue = configAsset;
                    bsSo.ApplyModifiedPropertiesWithoutUndo();
                    UnityEngine.Debug.Log($"[ChatAI] 已自动关联 CozeConfig 资源: {configPath}");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("[ChatAI] 未找到 CozeConfig 资源，请在 Inspector 中手动拖入");
            }

            // ---- Canvas ----
            var canvasGo = CreateCanvas();
            var debugUI = canvasGo.AddComponent<ChatAI.DebugTools.DebugChatUI>();

            // 背景
            var bg = CreateGo(canvasGo.transform, "Background");
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = BgColor;
            bgImg.raycastTarget = true;
            Stretch(bg);

            // 主布局
            var mainLayout = CreateGo(canvasGo.transform, "MainLayout");
            Stretch(mainLayout);
            var vlg = mainLayout.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true; vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 4;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            // ---- 配置面板 ----
            var configRefs = BuildConfigPanel(mainLayout.transform);

            // ---- 聊天区域 ----
            var chatRefs = BuildChatArea(mainLayout.transform);

            // ---- 输入区域 ----
            var inputRefs = BuildInputArea(mainLayout.transform);

            // ---- 状态栏 ----
            var statusText = BuildStatusBar(mainLayout.transform);

            // ---- 序列化赋值 ----
            var so = new SerializedObject(debugUI);
            so.FindProperty("configPanel").objectReferenceValue = configRefs.panel;
            so.FindProperty("tokenInput").objectReferenceValue = configRefs.tokenInput;
            so.FindProperty("botIdInput").objectReferenceValue = configRefs.botIdInput;
            so.FindProperty("baseUrlInput").objectReferenceValue = configRefs.baseUrlInput;
            so.FindProperty("asrKeyInput").objectReferenceValue = configRefs.asrKeyInput;
            so.FindProperty("connectBtn").objectReferenceValue = configRefs.connectBtn;
            so.FindProperty("collapseBtn").objectReferenceValue = configRefs.collapseBtn;
            so.FindProperty("scrollRect").objectReferenceValue = chatRefs.scrollRect;
            so.FindProperty("chatContent").objectReferenceValue = chatRefs.content;
            so.FindProperty("messageInput").objectReferenceValue = inputRefs.messageInput;
            so.FindProperty("sendBtn").objectReferenceValue = inputRefs.sendBtn;
            so.FindProperty("clearBtn").objectReferenceValue = inputRefs.clearBtn;
            so.FindProperty("voiceToggleBtn").objectReferenceValue = inputRefs.voiceToggleBtn;
            so.FindProperty("micBtn").objectReferenceValue = inputRefs.micBtn;
            so.FindProperty("ttsToggleBtn").objectReferenceValue = inputRefs.ttsToggleBtn;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 保存场景
            string scenePath = "Assets/ChatAI/Scenes/DebugChatScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            UnityEngine.Debug.Log($"[ChatAI] 调试场景已创建: {scenePath}");

            EditorUtility.DisplayDialog("ChatAI",
                "调试场景已创建完成！\n\n" +
                "点击 Play 运行，在界面中输入：\n" +
                "• API Token（coze.cn 个人设置获取）\n" +
                "• Bot ID（发布智能体后获取）\n\n" +
                "然后点击「连接」即可开始对话。",
                "好的");
        }

        [MenuItem("ChatAI/打开 Coze 开发者平台", false, 100)]
        public static void OpenCozePlatform() => Application.OpenURL("https://www.coze.cn/home");

        [MenuItem("ChatAI/打开 API Token 页面", false, 101)]
        public static void OpenTokenPage() => Application.OpenURL("https://www.coze.cn/open/oauth/pats");

        // ==================== 配置面板 ====================

        private struct ConfigRefs
        {
            public GameObject panel;
            public InputField tokenInput, botIdInput, baseUrlInput, asrKeyInput;
            public Button connectBtn, collapseBtn;
        }

        private static ConfigRefs BuildConfigPanel(Transform parent)
        {
            var refs = new ConfigRefs();

            var panel = CreateGo(parent, "ConfigPanel");
            panel.AddComponent<Image>().color = ConfigPanelBg;
            var pvlg = panel.AddComponent<VerticalLayoutGroup>();
            pvlg.childControlWidth = true; pvlg.childControlHeight = true;
            pvlg.childForceExpandWidth = true; pvlg.childForceExpandHeight = true;
            pvlg.spacing = 6; pvlg.padding = new RectOffset(10, 10, 8, 8);
            var ple = panel.AddComponent<LayoutElement>();
            ple.preferredHeight = 190;
            refs.panel = panel;

            // 标题行
            var titleRow = CreateHRow(panel.transform, 24);
            CreateLabel(titleRow.transform, "CozeAPI 配置", -1, true);
            refs.collapseBtn = CreateButton(titleRow.transform, "收起 ▼", 80);

            // Token 行
            var tokenRow = CreateHRow(panel.transform, 30);
            CreateLabel(tokenRow.transform, "Token:", 70);
            refs.tokenInput = CreateInputField(tokenRow.transform, "输入 Personal Access Token", true, true);
            CreateLabel(tokenRow.transform, "", 0); // spacer

            // Bot ID + Base URL 行
            var botRow = CreateHRow(panel.transform, 30);
            CreateLabel(botRow.transform, "Bot ID:", 70);
            refs.botIdInput = CreateInputField(botRow.transform, "输入 Bot ID", true);
            CreateLabel(botRow.transform, "Base URL:", 70);
            refs.baseUrlInput = CreateInputField(botRow.transform, "https://api.coze.cn", false, false, 220);

            // DashScope ASR Key 行
            var asrRow = CreateHRow(panel.transform, 30);
            CreateLabel(asrRow.transform, "ASR Key:", 70);
            refs.asrKeyInput = CreateInputField(asrRow.transform, "DashScope API Key（语音识别用）", true, true);
            CreateLabel(asrRow.transform, "", 0); // spacer

            // 按钮行
            var btnRow = CreateHRow(panel.transform, 34);
            var btnHlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            btnHlg.childAlignment = TextAnchor.MiddleCenter;
            btnHlg.childForceExpandWidth = false;
            refs.connectBtn = CreateButton(btnRow.transform, "连接 Coze", 140);

            return refs;
        }

        // ==================== 聊天区域 ====================

        private struct ChatRefs
        {
            public ScrollRect scrollRect;
            public RectTransform content;
        }

        private static ChatRefs BuildChatArea(Transform parent)
        {
            var refs = new ChatRefs();

            var scrollGo = CreateGo(parent, "ChatScroll");
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1;
            scrollGo.GetComponent<LayoutElement>().minHeight = 200;
            scrollGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f);

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            refs.scrollRect = sr;

            // Viewport
            var viewport = CreateGo(scrollGo.transform, "Viewport");
            Stretch(viewport);
            viewport.AddComponent<Image>().color = Color.white;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            var content = CreateGo(viewport.transform, "Content");
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cvlg = content.AddComponent<VerticalLayoutGroup>();
            cvlg.childControlWidth = true; cvlg.childControlHeight = false;
            cvlg.childForceExpandWidth = true; cvlg.childForceExpandHeight = false;
            cvlg.spacing = 6; cvlg.padding = new RectOffset(8, 8, 8, 8);
            refs.content = crt;

            sr.content = crt;
            sr.viewport = viewport.GetComponent<RectTransform>();

            return refs;
        }

        // ==================== 输入区域 ====================

        private struct InputRefs
        {
            public InputField messageInput;
            public Button sendBtn, clearBtn;
            public Button voiceToggleBtn, micBtn, ttsToggleBtn;
        }

        private static InputRefs BuildInputArea(Transform parent)
        {
            var refs = new InputRefs();

            // 文字输入行
            var row = CreateHRow(parent, 42);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;

            refs.messageInput = CreateInputField(row.transform, "输入消息，按 Enter 发送...", true);
            refs.sendBtn = CreateButton(row.transform, "发送", 80);
            refs.clearBtn = CreateButton(row.transform, "清空", 60);

            // 语音控制行
            var voiceRow = CreateHRow(parent, 38);
            var vhlg = voiceRow.GetComponent<HorizontalLayoutGroup>();
            vhlg.spacing = 8;
            vhlg.childAlignment = TextAnchor.MiddleCenter;
            vhlg.childForceExpandWidth = false;

            refs.voiceToggleBtn = CreateButton(voiceRow.transform, "语音模式", 120);
            refs.micBtn = CreateButton(voiceRow.transform, "说话", 100);
            refs.ttsToggleBtn = CreateButton(voiceRow.transform, "语音播报", 120);

            return refs;
        }

        // ==================== 状态栏 ====================

        private static Text BuildStatusBar(Transform parent)
        {
            var row = CreateGo(parent, "StatusBar");
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 22;
            return CreateText(row.transform, "  ● 就绪", 12, TextGray, TextAnchor.MiddleLeft);
        }

        // ==================== UI 工厂方法 ====================

        private static GameObject CreateGo(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreateCanvas()
        {
            var go = new GameObject("DebugCanvas", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static GameObject CreateHRow(Transform parent, float height)
        {
            var row = CreateGo(parent, "Row");
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.spacing = 8;
            return row;
        }

        private static void CreateLabel(Transform parent, string text, float width, bool flexible = false)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = _font;
            t.fontSize = 13;
            t.color = TextGray;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var le = go.AddComponent<LayoutElement>();
            if (flexible) le.flexibleWidth = 1;
            if (width > 0) le.preferredWidth = width;
        }

        private static Text CreateText(Transform parent, string content, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go);
            var t = go.AddComponent<Text>();
            t.text = content;
            t.font = _font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = alignment;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private static InputField CreateInputField(Transform parent, string placeholder,
                                                    bool flexible, bool isPassword = false, float preferredWidth = 0)
        {
            var go = CreateGo(parent, "InputField");
            go.AddComponent<Image>().color = InputFieldBg;
            if (flexible) go.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (preferredWidth > 0)
            {
                var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                le.preferredWidth = preferredWidth;
            }

            // TextArea
            var textArea = CreateGo(go.transform, "TextArea");
            Stretch(textArea);
            textArea.AddComponent<RectMask2D>();

            // InputText
            var inputTextGo = CreateGo(textArea.transform, "InputText");
            Stretch(inputTextGo);
            var itRt = inputTextGo.GetComponent<RectTransform>();
            itRt.offsetMin = new Vector2(8, 2);
            itRt.offsetMax = new Vector2(-8, -2);
            var itText = inputTextGo.AddComponent<Text>();
            itText.font = _font;
            itText.fontSize = 13;
            itText.color = TextWhite;
            itText.alignment = TextAnchor.MiddleLeft;
            itText.horizontalOverflow = HorizontalWrapMode.Wrap;
            itText.supportRichText = false;

            // Placeholder
            var phGo = CreateGo(textArea.transform, "Placeholder");
            Stretch(phGo);
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.offsetMin = new Vector2(8, 2);
            phRt.offsetMax = new Vector2(-8, -2);
            var phText = phGo.AddComponent<Text>();
            phText.text = placeholder;
            phText.font = _font;
            phText.fontSize = 13;
            phText.fontStyle = FontStyle.Italic;
            phText.color = TextGray;
            phText.alignment = TextAnchor.MiddleLeft;
            phText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // InputField 组件
            var inputField = go.AddComponent<InputField>();
            inputField.textComponent = itText;
            inputField.placeholder = phText;
            if (isPassword) inputField.contentType = InputField.ContentType.Password;

            return inputField;
        }

        private static Button CreateButton(Transform parent, string label, float width)
        {
            var go = CreateGo(parent, "Btn_" + label);
            var img = go.AddComponent<Image>();
            img.color = ButtonBlue;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            var txt = CreateText(go.transform, label, 14, TextWhite, TextAnchor.MiddleCenter);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.20f, 0.55f, 0.90f);
            colors.pressedColor = new Color(0.10f, 0.40f, 0.70f);
            btn.colors = colors;

            return btn;
        }

        private static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Font GetFont()
        {
            // 编辑器中必须使用持久化的内置字体资源
            // CreateDynamicFontFromOSFont 创建的是临时对象，保存场景后会丢失
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null)
                f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
#endif
