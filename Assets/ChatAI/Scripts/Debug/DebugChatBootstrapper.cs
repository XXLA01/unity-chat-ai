using UnityEngine;
using ChatAI.Core;
using ChatAI.Coze;

namespace ChatAI.DebugTools
{
    /// <summary>
    /// 调试场景启动器
    /// 通过 Inspector 指定已有的 CozeConfig 资源，运行时自动创建服务
    /// </summary>
    public class DebugChatBootstrapper : MonoBehaviour
    {
        public static DebugChatBootstrapper Instance { get; private set; }

        [Header("配置")]
        [SerializeField, Tooltip("拖入已配置好的 CozeConfig 资源")]
        private CozeConfig cozeConfig;

        public CozeChatService ChatService { get; private set; }
        public DashScopeASRService ASRService { get; private set; }
        public DashScopeTTSService TTSService { get; private set; }
        public WakeWordDetector WakeWord { get; private set; }
        public CozeConfig Config => cozeConfig;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 如果没有指定配置，创建一个空的（运行时可通过 UI 补充）
            if (cozeConfig == null)
            {
                cozeConfig = ScriptableObject.CreateInstance<CozeConfig>();
                UnityEngine.Debug.LogWarning("[Bootstrapper] 未指定 CozeConfig 资源，请在 Inspector 中拖入已配置好的资源");
            }

            // 创建 Coze Chat API 服务
            var chatGo = new GameObject("[CozeChatService]");
            ChatService = chatGo.AddComponent<CozeChatService>();
            InjectField(ChatService, "config", cozeConfig);

            // 创建 DashScope ASR 语音识别服务
            var asrGo = new GameObject("[DashScopeASRService]");
            ASRService = asrGo.AddComponent<DashScopeASRService>();
            if (!string.IsNullOrEmpty(cozeConfig.dashScopeApiKey))
                ASRService.SetApiKey(cozeConfig.dashScopeApiKey);

            // 创建 DashScope TTS 语音合成服务
            var ttsGo = new GameObject("[DashScopeTTSService]");
            TTSService = ttsGo.AddComponent<DashScopeTTSService>();
            if (!string.IsNullOrEmpty(cozeConfig.dashScopeApiKey))
                TTSService.SetApiKey(cozeConfig.dashScopeApiKey);
            // v2/v3 模型需要带版本后缀的音色名
            string voiceName = cozeConfig.ttsVoice;
            if (!string.IsNullOrEmpty(voiceName) && !voiceName.Contains("_v"))
            {
                voiceName = voiceName + "_v2";
                cozeConfig.ttsVoice = voiceName;
                UnityEngine.Debug.Log($"[Bootstrapper] TTS 音色自动修正: {cozeConfig.ttsVoice}");
            }
            if (!string.IsNullOrEmpty(voiceName))
                TTSService.SetVoice(voiceName);

            // 创建唤醒词检测器（仅 Windows 平台有效）
            var wakeGo = new GameObject("[WakeWordDetector]");
            WakeWord = wakeGo.AddComponent<WakeWordDetector>();

            UnityEngine.Debug.Log($"[Bootstrapper] 初始化完成，配置有效: {cozeConfig.IsValid()}, ASR/TTS Key: {!string.IsNullOrEmpty(cozeConfig.dashScopeApiKey)}");
        }

        private void InjectField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                field.SetValue(target, value);
            else
                UnityEngine.Debug.LogWarning($"[Bootstrapper] 无法注入 {fieldName}");
        }

        /// <summary>
        /// 运行时更新配置（覆盖 CozeConfig 中的值）
        /// </summary>
        public void UpdateConfig(string apiToken, string botId, string baseUrl)
        {
            cozeConfig.apiToken = apiToken;
            cozeConfig.botId = botId;
            if (!string.IsNullOrEmpty(baseUrl))
                cozeConfig.apiBaseUrl = baseUrl;
        }

        /// <summary>
        /// 运行时更新 DashScope ASR 配置
        /// </summary>
        public void UpdateASRConfig(string dashScopeApiKey)
        {
            cozeConfig.dashScopeApiKey = dashScopeApiKey;
            if (ASRService != null && !string.IsNullOrEmpty(dashScopeApiKey))
                ASRService.SetApiKey(dashScopeApiKey);
        }
    }
}
