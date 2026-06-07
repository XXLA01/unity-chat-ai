using System;

namespace ChatAI.Core
{
    /// <summary>
    /// 唤醒词检测器接口
    /// Windows 平台使用 KeywordRecognizer（零延迟），其他平台使用 Vosk 离线识别
    /// </summary>
    public interface IWakeWordDetector
    {
        /// <summary>检测到唤醒词时触发</summary>
        event Action OnWakeWordDetected;

        /// <summary>TTS 播报期间检测到打断词时触发（Windows KeywordRecognizer 和 Vosk 均支持）</summary>
        event Action OnInterruptDetected;

        /// <summary>检测到休眠词时触发，系统进入休眠状态，需重新唤醒</summary>
        event Action OnSleepDetected;

        /// <summary>是否正在监听</summary>
        bool IsListening { get; }

        /// <summary>是否处于暂停状态（TTS 播报中）</summary>
        bool IsPaused { get; }

        /// <summary>开始监听唤醒词</summary>
        void StartListening();

        /// <summary>停止监听</summary>
        void StopListening();

        /// <summary>暂停监听（TTS 播报时调用，防止回声误触发）</summary>
        void PauseListening();

        /// <summary>恢复监听（TTS 播报结束后调用）</summary>
        void ResumeListening();
    }
}
