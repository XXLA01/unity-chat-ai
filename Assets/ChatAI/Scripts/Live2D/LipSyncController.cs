using UnityEngine;
using ChatAI.Core;

namespace ChatAI.Live2D
{
    /// <summary>
    /// Live2D 口型同步控制器
    /// 根据音频振幅实时驱动 Live2D 模型的嘴型参数
    /// 
    /// 注意：此脚本需要 Live2D Cubism SDK for Unity
    /// 在导入 SDK 后，取消下方注释以启用功能
    /// </summary>
    public class LipSyncController : MonoBehaviour
    {
        [Header("口型同步参数")]
        [SerializeField, Tooltip("振幅灵敏度倍率")]
        private float sensitivity = 5f;

        [SerializeField, Tooltip("口型平滑系数（0~1，越大越平滑）")]
        private float smoothing = 0.3f;

        [SerializeField, Tooltip("最小口型值，避免完全闭合显得僵硬")]
        private float minMouthOpen = 0.05f;

        [SerializeField, Tooltip("最大口型值")]
        private float maxMouthOpen = 1f;

        // 当前口型值
        private float _currentMouthOpen;
        private float _targetMouthOpen;

        // Live2D 模型引用（需要 Cubism SDK）
        // private CubismModel _model;

        private void Start()
        {
            // 初始化 Live2D 模型引用
            // _model = GetComponent<CubismModel>();

            // 订阅音频振幅更新事件
            var audioPlayer = FindObjectOfType<ChatAI.Audio.AudioPlayer>();
            if (audioPlayer != null)
            {
                audioPlayer.OnAmplitudeUpdate += OnAmplitudeUpdate;
            }

            Debug.Log("[LipSync] 口型同步控制器初始化");
        }

        /// <summary>
        /// 接收音频振幅更新
        /// </summary>
        private void OnAmplitudeUpdate(float amplitude)
        {
            // 将振幅映射到口型参数
            _targetMouthOpen = Mathf.Clamp(amplitude * sensitivity, minMouthOpen, maxMouthOpen);
        }

        private void Update()
        {
            // 平滑插值
            _currentMouthOpen = Mathf.Lerp(_currentMouthOpen, _targetMouthOpen, 1f - smoothing);

            // 当振幅很小时，逐渐关闭嘴巴
            if (_targetMouthOpen <= minMouthOpen)
            {
                _currentMouthOpen = Mathf.Lerp(_currentMouthOpen, 0f, 0.1f);
            }

            // 应用到 Live2D 模型
            ApplyToModel(_currentMouthOpen);
        }

        /// <summary>
        /// 将口型参数应用到 Live2D 模型
        /// </summary>
        private void ApplyToModel(float mouthOpenValue)
        {
            // Live2D Cubism SDK 集成代码
            // 取消注释以启用（需导入 SDK）
            /*
            if (_model != null)
            {
                // ParamMouthOpenY: 嘴巴张开程度 (0 = 闭合, 1 = 全开)
                _model.SetParameterValue("ParamMouthOpenY", mouthOpenValue);
            }
            */

            // 调试输出（开发阶段）
            // Live2D Cubism SDK 集成后取消注释以启用的 SetParameterValue 调用
        }

        /// <summary>
        /// 手动设置口型值（用于测试或特殊动画）
        /// </summary>
        public void SetMouthOpen(float value)
        {
            _targetMouthOpen = Mathf.Clamp01(value);
        }

        private void OnDestroy()
        {
            var audioPlayer = FindObjectOfType<ChatAI.Audio.AudioPlayer>();
            if (audioPlayer != null)
            {
                audioPlayer.OnAmplitudeUpdate -= OnAmplitudeUpdate;
            }
        }
    }
}
