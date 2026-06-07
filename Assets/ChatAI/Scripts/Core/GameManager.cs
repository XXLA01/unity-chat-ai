using UnityEngine;
using System;

namespace ChatAI.Core
{
    /// <summary>
    /// 数字人全局状态枚举
    /// </summary>
    public enum DigitalHumanState
    {
        Idle,           // 待机状态 - 等待唤醒
        Listening,      // 聆听状态 - 正在录制用户语音
        Thinking,       // 思考状态 - 等待 AI 回复
        Speaking,       // 回复状态 - 正在播放 AI 语音
        Error           // 错误状态
    }

    /// <summary>
    /// 全局游戏管理器 - 管理数字人整体生命周期和状态
    /// 使用 DontDestroyOnLoad 保证跨场景存在
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("配置")]
        [SerializeField] private CozeConfig cozeConfig;

        // 当前状态
        public DigitalHumanState CurrentState { get; private set; } = DigitalHumanState.Idle;

        // 状态变更事件
        public event Action<DigitalHumanState, DigitalHumanState> OnStateChanged;

        // 对话 ID（维护多轮对话上下文）
        public string CurrentConversationId { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            TransitionTo(DigitalHumanState.Idle);
            Debug.Log("[ChatAI] GameManager 初始化完成，数字人进入待机状态");
        }

        /// <summary>
        /// 状态切换核心方法
        /// </summary>
        public void TransitionTo(DigitalHumanState newState)
        {
            if (CurrentState == newState) return;

            var oldState = CurrentState;
            CurrentState = newState;

            Debug.Log($"[ChatAI] 状态切换: {oldState} -> {newState}");
            OnStateChanged?.Invoke(oldState, newState);

            // 发布全局事件
            EventCenter.Instance?.Publish(new StateChangedEvent(oldState, newState));
        }

        /// <summary>
        /// 唤醒成功，进入对话模式
        /// </summary>
        public void OnWakeWordDetected()
        {
            Debug.Log("[ChatAI] 唤醒词检测成功，进入对话模式");
            TransitionTo(DigitalHumanState.Listening);
        }

        /// <summary>
        /// 用户语音录制完成，开始 AI 处理
        /// </summary>
        public void OnUserSpeechComplete(string transcribedText)
        {
            Debug.Log($"[ChatAI] 用户说: {transcribedText}");
            TransitionTo(DigitalHumanState.Thinking);

            // 将识别文本传递给 Coze 对话服务
            EventCenter.Instance?.Publish(new UserSpeechEvent(transcribedText));
        }

        /// <summary>
        /// AI 回复开始播放语音
        /// </summary>
        public void OnAIResponseStart()
        {
            TransitionTo(DigitalHumanState.Speaking);
        }

        /// <summary>
        /// AI 回复播放完成，回到聆听状态
        /// </summary>
        public void OnAIResponseComplete()
        {
            TransitionTo(DigitalHumanState.Listening);
        }

        /// <summary>
        /// 对话超时，回到待机状态
        /// </summary>
        public void OnConversationTimeout()
        {
            Debug.Log("[ChatAI] 对话超时，回到待机状态");
            CurrentConversationId = null;
            TransitionTo(DigitalHumanState.Idle);
        }

        /// <summary>
        /// 用户打断 AI 说话
        /// </summary>
        public void OnUserInterrupt()
        {
            Debug.Log("[ChatAI] 用户打断 AI 回复");
            EventCenter.Instance?.Publish(new InterruptEvent());
            TransitionTo(DigitalHumanState.Listening);
        }

        public void SetConversationId(string conversationId)
        {
            CurrentConversationId = conversationId;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }

    // ==================== 事件定义 ====================

    public struct StateChangedEvent
    {
        public DigitalHumanState OldState;
        public DigitalHumanState NewState;
        public StateChangedEvent(DigitalHumanState oldState, DigitalHumanState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public struct UserSpeechEvent
    {
        public string Text;
        public UserSpeechEvent(string text) { Text = text; }
    }

    public struct InterruptEvent { }
}
