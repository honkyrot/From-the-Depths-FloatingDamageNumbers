using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.DamageModels;
using UnityEngine;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// High-performance thread-safe manager that coalesces high-frequency block damage events
    /// (e.g. 5,000-block turret explosions) into single unified damage numbers per construct/type,
    /// </summary>
    public static class DamageQueueManager
    {
        private class DamageBucket
        {
            public MainConstruct? Construct;
            public DamageType Type;
            public float TotalDamage;
            public float PreviousDamage;
            public Vector3 Position;
            public int HitCount;
        }

        private static readonly object _syncLock = new object();
        private static readonly Dictionary<long, DamageBucket> _pendingBuckets = new Dictionary<long, DamageBucket>();
        private static readonly List<DamageBucket> _drainedBuckets = new List<DamageBucket>();

        // Per-ship active damage number lists (Key: MainConstruct UniqueId, or -1 for unanchored)
        private static readonly Dictionary<int, List<FloatingElements>> _shipQueues = new Dictionary<int, List<FloatingElements>>();

        // Hard-capped object pool
        private static readonly List<FloatingElements> _pool = new List<FloatingElements>();
        private static GameObject? _poolRoot;

        /// <summary>
        /// Ingests a damage event. Thread-safe: collapses multiple hits on the same construct/type
        /// in the current frame into a single bucket.
        /// </summary>
        public static void RecordDamage(MainConstruct? construct, DamageType damageType, float damageAmount, Vector3 worldPosition)
        {
            if (damageAmount < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(damageAmount) || float.IsInfinity(damageAmount))
            {
                return;
            }

            int constructId = construct != null ? construct.UniqueId : -1;
            long key = ((long)constructId << 32) | (long)(uint)damageType;

            lock (_syncLock)
            {
                if (_pendingBuckets.TryGetValue(key, out var bucket))
                {
                    bucket.PreviousDamage = bucket.TotalDamage;
                    bucket.TotalDamage += damageAmount;
                    bucket.HitCount++;
                    if (DamageNumberSettings.EnableDetailedSettings)
                    {
                        // Weighted average position for the cluster
                        float weight = 1f / bucket.HitCount;
                        bucket.Position = Vector3.Lerp(bucket.Position, worldPosition, weight);
                    }
                    // else, use the first hit position for the bucket (it really doesn't matter, simple uses COM)
                }
                else
                {
                    _pendingBuckets[key] = new DamageBucket
                    {
                        Construct = construct,
                        Type = damageType,
                        TotalDamage = damageAmount,
                        PreviousDamage = damageAmount,
                        Position = worldPosition,
                        HitCount = 1
                    };
                }
            }
        }

        /// <summary>
        /// If a construct is being actively repaird, this uses a custom "healing" damage type to display the amount of health restored.
        /// </summary>
        public static void RecordHealing(MainConstruct? construct, float healingAmount)
        {
            Vector3 worldPosition = construct != null ? construct.CentreOfMass : Vector3.zero;

        }

        /// <summary>
        /// Processes coalesced buckets, updates lifetimes, and assigns vertical stack indices.
        /// Runs on Unity's main thread every frame.
        /// </summary>
        public static void Update(float currentTime)
        {
            // Draining the pending buckets into a local list to minimize lock contention
            _drainedBuckets.Clear();
            lock (_syncLock)
            {
                if (_pendingBuckets.Count > 0)
                {
                    foreach (var kvp in _pendingBuckets)
                    {
                        _drainedBuckets.Add(kvp.Value);
                    }
                    _pendingBuckets.Clear();
                }
            }

            // Process each coalesced damage bucket
            for (int i = 0; i < _drainedBuckets.Count; i++)
            {
                ProcessBucket(_drainedBuckets[i], currentTime);
            }

            // Prune expired numbers from ship queues and compute dynamic stack indices
            List<int>? emptyShipKeys = null;

            foreach (var kvp in _shipQueues)
            {
                int shipId = kvp.Key;
                List<FloatingElements> queue = kvp.Value;

                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    FloatingElements item = queue[i];
                    if (item == null || item.IsExpired(currentTime) || !item.gameObject.activeSelf)
                    {
                        if (item != null)
                        {
                            item.gameObject.SetActive(false);
                        }
                        queue.RemoveAt(i);
                    }
                }

                if (queue.Count == 0)
                {
                    emptyShipKeys ??= new List<int>();
                    emptyShipKeys.Add(shipId);
                }
                else
                {
                    // Assign StackIndex: when multiple damage types (e.g. APHE: Kinetic + Explosive)
                    // occur at the same location, stack them vertically along the camera UP axis
                    for (int i = 0; i < queue.Count; i++)
                    {
                        FloatingElements current = queue[i];
                        if (current == null || !current.gameObject.activeSelf) continue;

                        int stack = 0;
                        for (int j = 0; j < i; j++)
                        {
                            FloatingElements prior = queue[j];
                            if (prior != null && prior.gameObject.activeSelf)
                            {
                                float dist = Vector3.Distance(current.LastWorldHitPosition, prior.LastWorldHitPosition);
                                if (dist <= DamageNumberSettings.StackProximityDistance)
                                {
                                    stack++;
                                }
                            }
                        }

                        current.StackIndex = stack;
                    }
                }
            }

            if (emptyShipKeys != null)
            {
                foreach (int key in emptyShipKeys)
                {
                    _shipQueues.Remove(key);
                }
            }
        }

        private static void ProcessBucket(DamageBucket bucket, float currentTime)
        {
            int constructId = bucket.Construct != null ? bucket.Construct.UniqueId : -1;

            if (!_shipQueues.TryGetValue(constructId, out var queue))
            {
                queue = new List<FloatingElements>();
                _shipQueues[constructId] = queue;
            }

            bool isExplosive = bucket.Type == DamageType.Explosive;
            bool isFire = bucket.Type == DamageType.Fire;
            bool isLaser = bucket.Type == DamageType.Laser;
            bool isRapid = isExplosive || isFire || isLaser;

            FloatingElements? candidate = null;

            if (DamageNumberSettings.AccumulateSustainedFire)
            {
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    FloatingElements existing = queue[i];
                    if (existing == null || !existing.gameObject.activeSelf) continue;

                    // Match damage type: Kinetic only merges with Kinetic, Explosive with Explosive
                    if (DamageNumberSettings.AggregateOnlySameDamageType && existing.Type != bucket.Type)
                    {
                        continue;
                    }

                    // In Detailed mode, enforce strict timing and spatial hit clustering.
                    // In Simple mode, merge into the active label of this damage type on the construct.
                    if (DamageNumberSettings.EnableDetailedSettings)
                    {
                        if ((currentTime - existing.LastHitTime) > DamageNumberSettings.AccumulationWindowSeconds)
                        {
                            continue;
                        }

                        float dist = Vector3.Distance(existing.LastWorldHitPosition, bucket.Position);
                        if (dist > DamageNumberSettings.MaxAccumulationDistance)
                        {
                            continue;
                        }
                    }

                    candidate = existing;
                    break;
                }
            }

            if (candidate != null)
            {
                candidate.AddDamage(bucket.TotalDamage, bucket.Position, currentTime, isRapid);
                if (bucket.TotalDamage >= DamageNumberSettings.LargeHitThreshold)
                {
                    candidate.MarkLargeHit();
                }
                return;
            }

            // Spawn or recycle a 3D TextMesh from the pool
            bool isLargeHit = bucket.TotalDamage >= DamageNumberSettings.LargeHitThreshold;

            FloatingElements textInstance = GetPooledInstance();
            textInstance.Initialize(
                bucket.Construct,
                bucket.Type,
                bucket.TotalDamage,
                bucket.Position,
                isLargeHit,
                currentTime);

            queue.Add(textInstance);

            if (DamageNumberSettings.EnableQueuePerShip && queue.Count > DamageNumberSettings.MaxNumbersPerShip)
            {
                FloatingElements oldest = queue[0];
                if (oldest != null)
                {
                    oldest.gameObject.SetActive(false);
                }
                queue.RemoveAt(0);
            }
        }

        public static void PrewarmPool()
        {
            EnsurePoolRoot();

            while (_pool.Count < DamageNumberSettings.MaxWorldPoolSize)
            {
                GameObject go = new GameObject($"FloatingDamageNumber_3D_{_pool.Count}");
                if (_poolRoot != null)
                {
                    go.transform.SetParent(_poolRoot.transform);
                }
                FloatingElements newItem = go.AddComponent<FloatingElements>();
                go.SetActive(false);
                _pool.Add(newItem);
            }
        }

        private static FloatingElements GetPooledInstance()
        {
            EnsurePoolRoot();

            // Looks for an inactive instance in the pool first
            for (int i = 0; i < _pool.Count; i++)
            {
                FloatingElements item = _pool[i];
                if (item != null && !item.gameObject.activeSelf)
                {
                    return item;
                }
            }

            // If under capacity, create a new instance and add it to the pool
            if (_pool.Count < DamageNumberSettings.MaxWorldPoolSize)
            {
                GameObject go = new GameObject($"FloatingDamageNumber_3D_{_pool.Count}");
                if (_poolRoot != null)
                {
                    go.transform.SetParent(_poolRoot.transform);
                }
                FloatingElements newItem = go.AddComponent<FloatingElements>();
                _pool.Add(newItem);
                return newItem;
            }

            // If at capacity, find the oldest active instance and recycle it
            FloatingElements oldest = _pool[0];
            float oldestTime = float.MaxValue;

            for (int i = 0; i < _pool.Count; i++)
            {
                FloatingElements candidate = _pool[i];
                if (candidate != null && candidate.CreationTime < oldestTime)
                {
                    oldestTime = candidate.CreationTime;
                    oldest = candidate;
                }
            }

            // Remove from whichever queue had it
            foreach (var q in _shipQueues.Values)
            {
                q.Remove(oldest);
            }

            return oldest;
        }

        private static void EnsurePoolRoot()
        {
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("[FloatingDamageNumbers_Pool]");
                UnityEngine.Object.DontDestroyOnLoad(_poolRoot);
            }
        }

        public static void Clear()
        {
            lock (_syncLock)
            {
                _pendingBuckets.Clear();
            }
            foreach (var q in _shipQueues.Values)
            {
                foreach (var item in q)
                {
                    if (item != null) item.gameObject.SetActive(false);
                }
            }
            _shipQueues.Clear();
        }
    }
}

