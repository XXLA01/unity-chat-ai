using System;

namespace ChatAI.ASR
{
    /// <summary>
    /// ASR 语音识别服务接口
    /// 不同的 ASR 引擎（火山引擎、讯飞、Azure）实现此接口
    /// </summary>
    public interface IASRService
    {
        /// <summary>
        /// 识别完成事件
        /// </summary>
        event Action<ASRResult> OnResult;

        /// <summary>
        /// 中间结果事件（实时识别中的临时文本）
        /// </summary>
        event Action<ASRResult> OnPartialResult;

        /// <summary>
        /// 错误事件
        /// </summary>
        event Action<string> OnError;

        /// <summary>
        /// 是否正在识别
        /// </summary>
        bool IsRecognizing { get; }

        /// <summary>
        /// 发送音频数据进行识别
        /// </summary>
        /// <param name="pcmData">PCM 16-bit 音频数据</param>
        /// <param name="sampleRate">采样率（如 16000）</param>
        void Recognize(byte[] pcmData, int sampleRate);

        /// <summary>
        /// 开始实时流式识别
        /// </summary>
        void StartStreamRecognition(int sampleRate);

        /// <summary>
        /// 发送流式音频数据块
        /// </summary>
        void SendStreamData(byte[] pcmChunk);

        /// <summary>
        /// 结束流式识别
        /// </summary>
        void StopStreamRecognition();

        /// <summary>
        /// 取消当前识别
        /// </summary>
        void Cancel();
    }
}
