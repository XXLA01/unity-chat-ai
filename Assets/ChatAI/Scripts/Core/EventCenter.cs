using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatAI.Core
{
    /// <summary>
    /// 事件中心 - 全局发布/订阅事件系统
    /// 解耦各模块之间的通信
    /// </summary>
    public class EventCenter : MonoBehaviour
    {
        public static EventCenter Instance { get; private set; }

        // 存储事件处理器
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

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

        /// <summary>
        /// 订阅事件
        /// </summary>
        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type))
                _handlers[type] = new List<Delegate>();

            _handlers[type].Add(handler);
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (_handlers.ContainsKey(type))
                _handlers[type].Remove(handler);
        }

        /// <summary>
        /// 发布事件
        /// </summary>
        public void Publish<T>(T eventData) where T : struct
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type)) return;

            // 创建副本避免遍历中修改
            var handlers = new List<Delegate>(_handlers[type]);
            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<T>)handler)?.Invoke(eventData);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EventCenter] 事件处理异常: {type.Name} - {e.Message}");
                }
            }
        }

        /// <summary>
        /// 清除所有事件订阅
        /// </summary>
        public void ClearAll()
        {
            _handlers.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                _handlers.Clear();
                Instance = null;
            }
        }
    }
}
