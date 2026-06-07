using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using ChatAI.Core;

namespace ChatAI.Coze
{
    /// <summary>
    /// 阿里云 DashScope Qwen-ASR 语音识别服务
    /// 将音频转换为文字，然后发送给 Coze Chat API
    /// API: POST https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions
    /// </summary>
    public class DashScopeASRService : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private string apiKey;
        [SerializeField] private string model = "qwen3-asr-flash";
        [SerializeField] private string apiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
        [SerializeField, Tooltip("请求超时（秒）")]
        private float timeout = 30f;

        // 事件
        public event Action<string> OnTranscriptionResult;  // 识别成功，返回文字
        public event Action<string> OnError;                // 识别失败

        // 状态
        public bool IsProcessing { get; private set; }

        /// <summary>
        /// 识别音频数据（WAV 格式）
        /// </summary>
        /// <param name="wavData">WAV 格式音频字节数组</param>
        public void Transcribe(byte[] wavData)
        {
            if (IsProcessing)
            {
                Debug.LogWarning("[DashScopeASR] 上一次识别尚未完成");
                return;
            }
            if (wavData == null || wavData.Length == 0)
            {
                OnError?.Invoke("音频数据为空");
                return;
            }
            StartCoroutine(TranscribeCoroutine(wavData));
        }

        private IEnumerator TranscribeCoroutine(byte[] wavData)
        {
            IsProcessing = true;

            // Base64 编码音频
            string base64Audio = Convert.ToBase64String(wavData);
            string dataUri = $"data:audio/wav;base64,{base64Audio}";

            // 构建 OpenAI 兼容格式的请求体
            string jsonBody = BuildRequestBody(dataUri);

            Debug.Log($"[DashScopeASR] 发送识别请求 (音频大小: {wavData.Length} bytes)");

            using (var request = new UnityWebRequest(apiUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"ASR 请求失败: HTTP {request.responseCode} {request.error}";
                    string body = request.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(body))
                        error += $"\n{body}";
                    Debug.LogError($"[DashScopeASR] {error}");
                    OnError?.Invoke(error);
                }
                else
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[DashScopeASR] 识别响应: {responseText}");

                    string transcript = ParseTranscription(responseText);
                    if (!string.IsNullOrEmpty(transcript))
                    {
                        Debug.Log($"[DashScopeASR] 识别结果: {transcript}");
                        OnTranscriptionResult?.Invoke(transcript);
                    }
                    else
                    {
                        // 空结果 = 没有检测到语音，不算错误，静默通知 UI 层
                        Debug.Log("[DashScopeASR] 识别结果为空（未检测到语音）");
                        OnTranscriptionResult?.Invoke("");
                    }
                }
            }

            IsProcessing = false;
        }

        /// <summary>
        /// 构建请求体 JSON（手动拼接，避免 JsonUtility 对嵌套数组的限制）
        /// </summary>
        private string BuildRequestBody(string audioDataUri)
        {
            // 转义 JSON 特殊字符
            string escapedUri = audioDataUri.Replace("\\", "\\\\").Replace("\"", "\\\"");

            return $@"{{
  ""model"": ""{model}"",
  ""messages"": [
    {{
      ""role"": ""user"",
      ""content"": [
        {{
          ""type"": ""input_audio"",
          ""input_audio"": {{
            ""data"": ""{escapedUri}""
          }}
        }}
      ]
    }}
  ]
}}";
        }

        /// <summary>
        /// 从 OpenAI 兼容格式的响应中提取转录文本
        /// 响应格式: {"choices":[{"message":{"content":"转录文本"}}]}
        /// </summary>
        private string ParseTranscription(string json)
        {
            // 尝试提取 choices[0].message.content
            // 使用轻量解析避免依赖外部库

            // 找到 "choices" 数组
            int choicesIdx = json.IndexOf("\"choices\"", StringComparison.Ordinal);
            if (choicesIdx < 0) return null;

            // 找到第一个 "content" 字段（在 choices 内部）
            int contentIdx = json.IndexOf("\"content\"", choicesIdx, StringComparison.Ordinal);
            if (contentIdx < 0) return null;

            // 跳过 "content" : 到达值
            int colonIdx = json.IndexOf(':', contentIdx + 9);
            if (colonIdx < 0) return null;
            colonIdx++;
            while (colonIdx < json.Length && char.IsWhiteSpace(json[colonIdx])) colonIdx++;
            if (colonIdx >= json.Length) return null;

            if (json[colonIdx] == '"')
            {
                // 字符串值
                colonIdx++;
                var sb = new StringBuilder();
                while (colonIdx < json.Length && json[colonIdx] != '"')
                {
                    if (json[colonIdx] == '\\' && colonIdx + 1 < json.Length)
                    {
                        char next = json[colonIdx + 1];
                        switch (next)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            default: sb.Append(next); break;
                        }
                        colonIdx += 2;
                    }
                    else
                    {
                        sb.Append(json[colonIdx]);
                        colonIdx++;
                    }
                }
                return sb.ToString().Trim();
            }

            return null;
        }

        /// <summary>
        /// 运行时设置 API Key
        /// </summary>
        public void SetApiKey(string key)
        {
            apiKey = key;
        }
    }
}
