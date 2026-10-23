using System;
using BrilliantSkies.Core.Logger;
using UnityEngine;

namespace FloatingDamageNumbers
{
    public class FloatingDamageNumberRenderer : MonoBehaviour
    {
        private static FloatingDamageNumberRenderer? _instance;
        public static FloatingDamageNumberRenderer? Instance => _instance;

        public static Canvas? OverlayCanvas { get; private set; }
        public static RectTransform? CanvasRect { get; private set; }

        public static void EnsureInstance()
        {
            if (_instance == null)
            {
                try
                {
                    GameObject go = new("[FloatingDamageNumbers_Host]");
                    _instance = go.AddComponent<FloatingDamageNumberRenderer>();
                    DontDestroyOnLoad(go);

                    OverlayCanvas = go.AddComponent<Canvas>();
                    OverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    OverlayCanvas.sortingOrder = 20002;

                    CanvasRect = go.GetComponent<RectTransform>();

                    DamageQueueManager.PrewarmPool();
                    AdvLogger.LogEvent("[FloatingDamageNumbers] Host GameObject and pool pre-warmed.");
                }
                catch (Exception ex)
                {
                    AdvLogger.LogError($"[FloatingDamageNumbers] Error in EnsureInstance: {ex}", LogOptions._AlertDevAndCustomerInGame);
                }
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            try
            {
                DamageNumberSettingsConfig.Get()?.ApplyToSettings();
                DamageIconCache.Initialize();
            }
            catch { }

            DamageQueueManager.PrewarmPool();
        }

        private void LateUpdate()
        {
            DamageQueueManager.Update(Time.time, Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            DamageQueueManager.Clear();
        }
    }
}