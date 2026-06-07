using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using ChatAI.Core;

namespace ChatAI.Coze
{
    /// <summary>
    /// Coze Chat API 服务 - 管理与 Coze 平台的对话通信
    /// 支持 SSE 流式输出和多轮对话上下文
    /// </summary>
    public class CozeChatService : MonoBehaviour
    {
        [Header("配置引用")]
        [SerializeField] private CozeConfig config;

        // 事件
        public event Action<string> OnTextChunkReceived;   // 流式文本片段
        public event Action<string> OnFullResponseReady;    // 完整回复文本
        public event Action<string> OnConversationCreated;  // 会话创建
        public event Action<string> OnError;                // 错误信息

        // 音频上传回调
        public event Action<string> OnAudioFileUploaded;    // 音频文件上传完成(fileId)
        public event Action OnAudioProcessing;              // 音频正在处理中

        // 状态
        public bool IsRequesting { get; private set; }
        public string ConversationId { get; private set; }

        // 响应累积器（跨 SSE 事件积累完整回复）
        private StringBuilder _responseAccumulator = new StringBuilder();

        private void Start()
        {
            EventCenter.Instance?.Subscribe<UserSpeechEvent>(OnUserSpeech);
        }

        /// <summary>
        /// 创建新的对话会话
        /// </summary>
        public void CreateConversation()
        {
            StartCoroutine(CreateConversationCoroutine());
        }

        private IEnumerator CreateConversationCoroutine()
        {
            string url = config.GetCreateConversationUrl();
            string jsonBody = "{}";

            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {config.apiToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)config.requestTimeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<ConversationCreateResponse>(request.downloadHandler.text);
                    if (response != null && !string.IsNullOrEmpty(response.data?.id))
                    {
                        ConversationId = response.data.id;
                        GameManager.Instance?.SetConversationId(ConversationId);
                        OnConversationCreated?.Invoke(ConversationId);
                        Debug.Log($"[CozeChat] 会话创建成功: {ConversationId}");
                    }
                }
                else
                {
                    Debug.LogError($"[CozeChat] 创建会话失败: {request.error}");
                    OnError?.Invoke($"创建会话失败: {request.error}");
                }
            }
        }

        /// <summary>
        /// 发送对话请求（SSE 流式）
        /// </summary>
        public void SendMessage(string userMessage)
        {
            if (IsRequesting)
            {
                Debug.LogWarning("[CozeChat] 上一个请求尚未完成");
                return;
            }

            StartCoroutine(SendMessageCoroutine(userMessage));
        }

        private IEnumerator SendMessageCoroutine(string userMessage)
        {
            IsRequesting = true;
            _responseAccumulator.Clear();

            // 构建请求体
            var requestBody = new ChatRequest
            {
                bot_id = config.botId,
                user_id = config.userId,
                stream = true,
                auto_save_history = true,
                additional_messages = new List<ChatMessage>
                {
                    new ChatMessage
                    {
                        role = "user",
                        content = userMessage,
                        content_type = "text"
                    }
                }
            };

            // 如果有会话 ID，加入请求
            if (!string.IsNullOrEmpty(ConversationId))
                requestBody.conversation_id = ConversationId;

            string jsonBody = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(config.GetChatUrl(), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new SSEDownloadHandler(OnSSEEvent);
                request.SetRequestHeader("Authorization", $"Bearer {config.apiToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)config.requestTimeout;

                var operation = request.SendWebRequest();

                // SSE 流式处理：在请求过程中持续处理事件
                while (!operation.isDone)
                {
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"对话请求失败: {request.error}";
                    Debug.LogError($"[CozeChat] {error}");
                    OnError?.Invoke(error);
                }
                else
                {
                    string response = _responseAccumulator.ToString();
                    OnFullResponseReady?.Invoke(response);
                    Debug.Log($"[CozeChat] 完整回复: {response}");
                }
            }

            IsRequesting = false;
        }

        /// <summary>
        /// 发送音频消息：上传音频文件后通过 Chat API 发送
        /// wavData: WAV 格式音频数据
        /// </summary>
        public void SendAudioMessage(byte[] wavData)
        {
            if (IsRequesting)
            {
                Debug.LogWarning("[CozeChat] 上一个请求尚未完成");
                return;
            }
            if (wavData == null || wavData.Length == 0)
            {
                OnError?.Invoke("音频数据为空");
                return;
            }
            StartCoroutine(SendAudioMessageCoroutine(wavData));
        }

        private IEnumerator SendAudioMessageCoroutine(byte[] wavData)
        {
            IsRequesting = true;
            OnAudioProcessing?.Invoke();

            // 第一步：上传音频文件
            string fileId = null;
            yield return StartCoroutine(UploadFileCoroutine(wavData, "audio.wav", (fid) => { fileId = fid; }));

            if (string.IsNullOrEmpty(fileId))
            {
                Debug.LogError("[CozeChat] 音频文件上传失败");
                OnError?.Invoke("音频文件上传失败");
                IsRequesting = false;
                yield break;
            }

            OnAudioFileUploaded?.Invoke(fileId);
            Debug.Log($"[CozeChat] 音频文件上传成功: {fileId}");

            // 第二步：发送包含音频的对话消息
            yield return StartCoroutine(SendAudioChatCoroutine(fileId));

            IsRequesting = false;
        }

        /// <summary>
        /// 上传文件到 Coze
        /// </summary>
        private IEnumerator UploadFileCoroutine(byte[] fileData, string fileName, Action<string> onResult)
        {
            string url = config.GetFileUploadUrl();

            // 构建 multipart/form-data
            var form = new WWWForm();
            form.AddBinaryData("file", fileData, fileName, "audio/wav");

            using (var request = UnityWebRequest.Post(url, form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.apiToken}");
                request.timeout = (int)config.requestTimeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;

                    // 手动解析 file_id
                    string fileId = ExtractJsonValue(responseText, "id");
                    if (string.IsNullOrEmpty(fileId))
                    {
                        // 尝试嵌套解析 data.id
                        string dataBlock = ExtractJsonBlock(responseText, "data");
                        if (!string.IsNullOrEmpty(dataBlock))
                            fileId = ExtractJsonValue(dataBlock, "id");
                    }

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        onResult?.Invoke(fileId);
                    }
                    else
                    {
                        Debug.LogError($"[CozeChat] 无法从响应中解析 file_id: {responseText}");
                        OnError?.Invoke("文件上传成功但无法解析 file_id");
                    }
                }
                else
                {
                    string error = $"文件上传失败: {request.error}\n{request.downloadHandler?.text}";
                    Debug.LogError($"[CozeChat] {error}");
                    OnError?.Invoke(error);
                }
            }
        }

        /// <summary>
        /// 发送包含音频 file_id 的对话消息
        /// </summary>
        private IEnumerator SendAudioChatCoroutine(string audioFileId)
        {
            _responseAccumulator.Clear();

            // 构建包含音频的 object_string
            // content 需要是 JSON 数组字符串：[{"type":"audio","file_id":"xxx"}]
            string audioObjectString = $"[{{\"type\":\"audio\",\"file_id\":\"{audioFileId}\"}}]";

            var requestBody = new ChatRequest
            {
                bot_id = config.botId,
                user_id = config.userId,
                stream = true,
                auto_save_history = true,
                additional_messages = new List<ChatMessage>
                {
                    new ChatMessage
                    {
                        role = "user",
                        content = audioObjectString,
                        content_type = "object_string"
                    }
                }
            };

            if (!string.IsNullOrEmpty(ConversationId))
                requestBody.conversation_id = ConversationId;

            string jsonBody = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(config.GetChatUrl(), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new SSEDownloadHandler(OnSSEEvent);
                request.SetRequestHeader("Authorization", $"Bearer {config.apiToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)config.requestTimeout;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                    yield return null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"音频对话请求失败: HTTP {request.responseCode} {request.error}\n{request.downloadHandler?.text}";
                    Debug.LogError($"[CozeChat] {error}");
                    OnError?.Invoke(error);
                }
                else
                {
                    Debug.Log($"[CozeChat] 音频对话 HTTP {request.responseCode}");
                    string response = _responseAccumulator.ToString();
                    OnFullResponseReady?.Invoke(response);
                    Debug.Log($"[CozeChat] 音频回复完成: {response}");
                }
            }
        }

        /// <summary>
        /// 从 JSON 字符串中提取指定 key 的 string 值（轻量解析）
        /// </summary>
        private string ExtractJsonValue(string json, string key)
        {
            string pattern = $"\"{key}\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            idx = json.IndexOf(':', idx + pattern.Length);
            if (idx < 0) return null;
            idx++;
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            if (idx >= json.Length) return null;

            if (json[idx] == '"')
            {
                idx++;
                var sb = new StringBuilder();
                while (idx < json.Length && json[idx] != '"')
                {
                    if (json[idx] == '\\' && idx + 1 < json.Length)
                    {
                        sb.Append(json[idx + 1]);
                        idx += 2;
                    }
                    else
                    {
                        sb.Append(json[idx]);
                        idx++;
                    }
                }
                return sb.ToString();
            }
            return null;
        }

        /// <summary>
        /// 提取 JSON 中指定 key 对应的 {...} 块
        /// </summary>
        private string ExtractJsonBlock(string json, string key)
        {
            string pattern = $"\"{key}\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            idx = json.IndexOf('{', idx + pattern.Length);
            if (idx < 0) return null;

            int depth = 0;
            int start = idx;
            for (int i = idx; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) return json.Substring(start, i - start + 1); }
            }
            return null;
        }

        /// <summary>
        /// 处理 SSE 事件
        /// </summary>
        private void OnSSEEvent(string eventType, string data)
        {
            if (string.IsNullOrEmpty(data) || data == "[DONE]") return;

            try
            {
                switch (eventType)
                {
                    case "conversation.message.delta":
                        // 流式文本增量
                        var delta = JsonUtility.FromJson<MessageDelta>(data);
                        if (delta != null && !string.IsNullOrEmpty(delta.content))
                        {
                            _responseAccumulator.Append(delta.content);
                            OnTextChunkReceived?.Invoke(delta.content);
                        }
                        break;

                    case "conversation.message.completed":
                        // 消息完成
                        var completed = JsonUtility.FromJson<MessageCompleted>(data);
                        if (completed != null)
                        {
                            // 更新会话 ID（如果返回了）
                            if (!string.IsNullOrEmpty(completed.conversation_id))
                                ConversationId = completed.conversation_id;
                        }
                        break;

                    case "conversation.chat.completed":
                        Debug.Log("[CozeChat] 对话轮次完成");
                        break;

                    case "error":
                        Debug.LogError($"[CozeChat] SSE 错误: {data}");
                        // 尝试从 JSON 中提取错误信息
                        string errorCode = ExtractJsonValue(data, "code");
                        string errorMsg = ExtractJsonValue(data, "msg");
                        if (!string.IsNullOrEmpty(errorMsg))
                            OnError?.Invoke($"[{errorCode}] {errorMsg}");
                        else
                            OnError?.Invoke(data);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CozeChat] SSE 解析异常: {e.Message}");
            }
        }

        /// <summary>
        /// 响应用户语音事件
        /// </summary>
        private void OnUserSpeech(UserSpeechEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.Text))
                SendMessage(evt.Text);
        }

        private void OnDestroy()
        {
            EventCenter.Instance?.Unsubscribe<UserSpeechEvent>(OnUserSpeech);
        }
    }

    // ==================== SSE 下载处理器 ====================

    /// <summary>
    /// 自定义 DownloadHandler 用于解析 SSE 流式数据
    /// </summary>
    public class SSEDownloadHandler : DownloadHandlerScript
    {
        private Action<string, string> _onSSEEvent;
        private string _currentEventType = "message";
        private StringBuilder _dataBuffer = new StringBuilder();
        private bool _firstChunk = true;

        public SSEDownloadHandler(Action<string, string> onSSEEvent)
        {
            _onSSEEvent = onSSEEvent;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0) return true;

            string chunk = Encoding.UTF8.GetString(data, 0, dataLength);

            // 检测非 SSE 格式的裸 JSON 响应（如错误码返回）
            if (_firstChunk)
            {
                _firstChunk = false;
                string trimmedChunk = chunk.TrimStart();
                if (trimmedChunk.StartsWith("{") && !trimmedChunk.StartsWith("{\""))
                {
                    // 非 SSE 格式，直接作为 error 事件发出
                    UnityEngine.Debug.LogWarning($"[SSE] 收到非 SSE 格式的 JSON 响应: {chunk}");
                    _onSSEEvent?.Invoke("error", chunk);
                    return true;
                }
                // 也检查是否是直接 JSON 对象（无 event:/data: 前缀）
                if (trimmedChunk.StartsWith("{") && !chunk.Contains("event:") && !chunk.Contains("data:"))
                {
                    UnityEngine.Debug.LogWarning($"[SSE] 收到裸 JSON 响应: {chunk}");
                    _onSSEEvent?.Invoke("error", chunk);
                    return true;
                }
            }

            string[] lines = chunk.Split('\n');

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    // 空行表示一个 SSE 事件结束
                    if (_dataBuffer.Length > 0)
                    {
                        _onSSEEvent?.Invoke(_currentEventType, _dataBuffer.ToString());
                        _dataBuffer.Clear();
                        _currentEventType = "message";
                    }
                    continue;
                }

                if (trimmed.StartsWith("event:"))
                {
                    _currentEventType = trimmed.Substring(6).Trim();
                }
                else if (trimmed.StartsWith("data:"))
                {
                    string dataContent = trimmed.Substring(5).Trim();
                    if (_dataBuffer.Length > 0)
                        _dataBuffer.Append("\n");
                    _dataBuffer.Append(dataContent);
                }
            }

            return true;
        }
    }

    // ==================== API 数据模型 ====================

    [Serializable]
    public class ChatRequest
    {
        public string bot_id;
        public string user_id;
        public string conversation_id;
        public bool stream;
        public bool auto_save_history;
        public List<ChatMessage> additional_messages;
    }

    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;
        public string content_type;
    }

    [Serializable]
    public class ConversationCreateResponse
    {
        public int code;
        public string msg;
        public ConversationData data;
    }

    [Serializable]
    public class ConversationData
    {
        public string id;
    }

    [Serializable]
    public class MessageDelta
    {
        public string content;
        public string role;
        public string type;
    }

    [Serializable]
    public class MessageCompleted
    {
        public string id;
        public string conversation_id;
        public string role;
        public string content;
    }
}
