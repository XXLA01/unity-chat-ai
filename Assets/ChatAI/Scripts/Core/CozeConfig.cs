using UnityEngine;

namespace ChatAI.Core
{
    /// <summary>
    /// Coze API 配置文件 (ScriptableObject)
    /// 在 Unity 编辑器中通过 Assets > Create > ChatAI > Coze Config 创建
    /// </summary>
    [CreateAssetMenu(fileName = "CozeConfig", menuName = "ChatAI/Coze Config")]
    public class CozeConfig : ScriptableObject
    {
        [Header("Coze 平台配置")]
        [Tooltip("Personal Access Token，在 coze.cn 个人设置中获取")]
        public string apiToken;

        [Tooltip("智能体 Bot ID，发布 Bot 后获取")]
        public string botId;

        [Tooltip("Coze API 基础地址")]
        public string apiBaseUrl = "https://api.coze.cn";

        [Tooltip("用户 ID（可自定义或使用设备标识）")]
        public string userId = "unity_user";

        [Header("语音服务配置")]
        [Tooltip("音频采样率（Hz）")]
        public int audioSampleRate = 16000;

        [Header("DashScope ASR 配置（阿里云语音识别）")]
        [Tooltip("DashScope API Key，在阿里云百炼控制台获取")]
        public string dashScopeApiKey;

        [Tooltip("ASR 模型名称")]
        public string asrModel = "qwen3-asr-flash";

        [Header("DashScope TTS 配置（语音合成）")]
        [Tooltip("TTS 模型名称")]
        public string ttsModel = "cosyvoice-v2";

        [Tooltip("TTS 音色名称（v2模型需带_v2后缀）")]
        public string ttsVoice = "longxiaochun_v2";

        [Header("对话参数")]
        [Tooltip("对话超时时间（秒），超时后自动回到待机状态")]
        public float conversationTimeout = 30f;

        [Tooltip("是否启用流式输出")]
        public bool enableStreaming = true;

        [Tooltip("请求超时时间（秒）")]
        public float requestTimeout = 30f;

        [Tooltip("最大重试次数")]
        public int maxRetryCount = 3;

        /// <summary>
        /// 获取完整的 API URL
        /// </summary>
        public string GetChatUrl()
        {
            return $"{apiBaseUrl}/v3/chat";
        }

        /// <summary>
        /// 获取创建会话的 URL
        /// </summary>
        public string GetCreateConversationUrl()
        {
            return $"{apiBaseUrl}/v1/conversation/create";
        }

        /// <summary>
        /// 获取文件上传 URL
        /// </summary>
        public string GetFileUploadUrl()
        {
            return $"{apiBaseUrl}/v1/files/upload";
        }

        /// <summary>
        /// 验证配置是否完整
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(apiToken)
                && !string.IsNullOrEmpty(botId)
                && !string.IsNullOrEmpty(apiBaseUrl);
        }
    }
}
