using System;
using BrilliantSkies.Core.Logger;
using UnityEngine;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// Unity MonoBehaviour host that updates the DamageQueueManager each frame.
    /// Damage numbers are rendered directly in 3D world space using native TextMesh components.
    /// </summary>
    public class FloatingDamageNumberRenderer : MonoBehaviour
    {
        private static FloatingDamageNumberRenderer? _instance;

        public static FloatingDamageNumberRenderer? Instance => _instance;

        public static void EnsureInstance()
        {
            if (_instance == null)
            {
                try
                {
                    GameObject go = new("[FloatingDamageNumbers_Host]");
                    _instance = go.AddComponent<FloatingDamageNumberRenderer>();
                    DontDestroyOnLoad(go);
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

        private void Update()
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

