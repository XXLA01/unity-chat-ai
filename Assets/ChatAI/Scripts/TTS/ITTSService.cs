using System;

namespace ChatAI.TTS
{
    /// <summary>
    /// TTS 语音合成服务接口
    /// 不同的 TTS 引擎（火山引擎、CosyVoice）实现此接口
    /// </summary>
    public interface ITTSService
    {
        /// <summary>
        /// 合成完成事件，返回音频数据
        /// </summary>
        event Action<byte[], int> OnSynthesisComplete; // pcmData, sampleRate

        /// <summary>
        /// 合成错误事件
        /// </summary>
        event Action<string> OnError;

        /// <summary>
        /// 是否正在合成
        /// </summary>
        bool IsSynthesizing { get; }

        /// <summary>
        /// 合成语音
        /// </summary>
        /// <param name="text">要合成的文本</param>
        /// <param name="voiceId">音色 ID</param>
        /// <param name="speed">语速（0.5~2.0，默认 1.0）</param>
        void Synthesize(string text, string voiceId = null, float speed = 1.0f);

        /// <summary>
        /// 取消当前合成
        /// </summary>
        void Cancel();
    }
}
