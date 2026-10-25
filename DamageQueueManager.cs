using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.DamageModels;
using UnityEngine;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// thread safe manager that coalesces high-frequency block damage events
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

        private readonly struct DamageEvent(MainConstruct? construct, DamageType type, float amount, Vector3 position)
        {
            public MainConstruct? Construct { get; } = construct;
            public DamageType Type { get; } = type;
            public float Amount { get; } = amount;
            public Vector3 Position { get; } = position;
        }

        private static readonly ConcurrentQueue<DamageEvent> _incomingDamage = new();
        private static readonly Dictionary<long, DamageBucket> _pendingBuckets = [];
        private static readonly List<DamageBucket> _drainedBuckets = [];

        // Per-ship active damage number lists (Key: MainConstruct UniqueId, or -1 for unanchored)
        private static readonly Dictionary<int, List<FloatingElements>> _shipQueues = [];

        // Hard-capped object pool
        private static readonly List<FloatingElements> _pool = [];
        private static GameObject? _poolRoot;

        /// <summary>
        /// Ingests a damage event from any thread.
        /// The main-thread update pass drains and aggregates these hits per construct/type.
        /// </summary>
        public static void RecordDamage(MainConstruct? construct, DamageType damageType, float damageAmount, Vector3 worldPosition)
        {
            if (!DamageNumberSettings.EnableDamageNumbers)
            {
                return;
            }

            if (damageAmount < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(damageAmount) || float.IsInfinity(damageAmount))
            {
                return;
            }

            _incomingDamage.Enqueue(new DamageEvent(construct, damageType, damageAmount, worldPosition));
        }

        /// <summary>
        /// If a construct is being actively repaired, this uses a custom "healing" damage type to display the amount of health restored.
        /// </summary>
        //public static void RecordHealing(MainConstruct? construct, float healingAmount)
        //{
        //    Vector3 worldPosition = construct != null ? construct.CentreOfMass : Vector3.zero;

        //}

        /// <summary>
        /// Processes coalesced buckets, updates lifetimes, and assigns vertical stack indices.
        /// Runs on Unity's main thread every frame.
        /// </summary>
        public static void Update(float currentTime, float deltaTime)
        {
            if (!DamageNumberSettings.EnableDamageNumbers)
            {
                Clear();
                return;
            }

            // Drain all incoming damage events into coalesced buckets for this frame.
            DrainIncomingDamageToBuckets();

            // Drain aggregated buckets into a local list for processing this frame.
            _drainedBuckets.Clear();
            if (_pendingBuckets.Count > 0)
            {
                foreach (var kvp in _pendingBuckets)
                {
                    _drainedBuckets.Add(kvp.Value);
                }
                _pendingBuckets.Clear();
            }

            // If hosting a multiplayer session, enqueue coalesced damage for tickrate broadcast
            if (BrilliantSkies.Core.Net.IsServer)
            {
                for (int i = 0; i < _drainedBuckets.Count; i++)
                {
                    DamageBucket bucket = _drainedBuckets[i];
                    MultiplayerHandler.QueueDamageForBroadcast(bucket.Construct, bucket.Type, bucket.TotalDamage, bucket.Position);
                }

                MultiplayerHandler.Tick(currentTime);
            }

            // Process each coalesced damage bucket
            for (int i = 0; i < _drainedBuckets.Count; i++)
            {
                ProcessBucket(_drainedBuckets[i], currentTime);
            }

            // Prune expired numbers from ship queues and compute dynamic stack indices
            List<int>? emptyShipKeys = null;
            float stackProximityDistance = DamageNumberSettings.StackProximityDistance;
            float stackProximityDistanceSquared = stackProximityDistance * stackProximityDistance;
            Camera? activeCamera = FloatingElements.ResolveActiveCamera();

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
                    emptyShipKeys ??= [];
                    emptyShipKeys.Add(shipId);
                    continue;
                }

                // Assign StackIndex: when multiple damage types (e.g. APHE: Kinetic + Explosive)
                // occur at the same location, stack them vertically along the camera UP axis
                for (int i = 0; i < queue.Count; i++)
                {
                    FloatingElements current = queue[i];
                    if (current == null || !current.gameObject.activeSelf) continue;

                    if (!DamageNumberSettings.EnableDetailedSettings)
                    {
                        current.StackIndex = i;
                        continue;
                    }

                    int stack = 0;
                    for (int j = 0; j < i; j++)
                    {
                        FloatingElements prior = queue[j];
                        if (prior != null && prior.gameObject.activeSelf)
                        {
                            Vector3 offset = current.LastWorldHitPosition - prior.LastWorldHitPosition;
                            if (offset.sqrMagnitude <= stackProximityDistanceSquared)
                            {
                                stack++;
                            }
                        }
                    }

                    current.StackIndex = stack;
                }

                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    FloatingElements item = queue[i];
                    if (item == null || !item.gameObject.activeSelf)
                    {
                        queue.RemoveAt(i);
                        continue;
                    }

                    item.ManualUpdate(currentTime, deltaTime, activeCamera);
                    if (!item.gameObject.activeSelf)
                    {
                        queue.RemoveAt(i);
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

        // Drains all incoming damage events into coalesced buckets for this frame.
        private static void DrainIncomingDamageToBuckets()
        {
            while (_incomingDamage.TryDequeue(out DamageEvent damageEvent))
            {
                int constructId = damageEvent.Construct != null ? damageEvent.Construct.UniqueId : -1;
                long key = ((long)constructId << 32) | (long)(uint)damageEvent.Type;

                if (_pendingBuckets.TryGetValue(key, out DamageBucket? bucket))
                {
                    bucket.PreviousDamage = bucket.TotalDamage;
                    bucket.TotalDamage += damageEvent.Amount;
                    bucket.HitCount++;

                    if (DamageNumberSettings.EnableDetailedSettings)
                    {
                        float weight = 1f / bucket.HitCount;
                        bucket.Position = Vector3.Lerp(bucket.Position, damageEvent.Position, weight);
                    }
                }
                else
                {
                    _pendingBuckets[key] = new DamageBucket
                    {
                        Construct = damageEvent.Construct,
                        Type = damageEvent.Type,
                        TotalDamage = damageEvent.Amount,
                        PreviousDamage = damageEvent.Amount,
                        Position = damageEvent.Position,
                        HitCount = 1
                    };
                }
            }
        }

        // Processes a single coalesced damage bucket, either merging it into an existing floating number or spawning a new one.
        private static void ProcessBucket(DamageBucket bucket, float currentTime)
        {
            int constructId = bucket.Construct != null ? bucket.Construct.UniqueId : -1;

            if (!_shipQueues.TryGetValue(constructId, out var queue))
            {
                queue = new List<FloatingElements>();
                _shipQueues[constructId] = queue;
            }

            // if false, all damage types will merge into a single number per ship
            if (!DamageNumberSettings.AggregateOnlySameDamageType)
            {
                bucket.Type = DamageType.None;
            }

                bool isExplosive = bucket.Type == DamageType.Explosive;
            bool isFire = bucket.Type == DamageType.Fire;
            bool isLaser = bucket.Type == DamageType.Laser;
            bool isRapid = isExplosive || isFire || isLaser;

            FloatingElements? candidate = null;
            float maxAccumulationDistance = DamageNumberSettings.MaxAccumulationDistance;
            float maxAccumulationDistanceSquared = maxAccumulationDistance * maxAccumulationDistance;

            if (DamageNumberSettings.AccumulateSustainedFire)
            {
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    FloatingElements existing = queue[i];
                    if (existing == null || !existing.gameObject.activeSelf) continue;

                    // Match damage type: Kinetic only merges with Kinetic, Explosive with Explosive
                    if (existing.Type != bucket.Type)
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

                        Vector3 offset = existing.LastWorldHitPosition - bucket.Position;
                        if (offset.sqrMagnitude > maxAccumulationDistanceSquared)
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
                oldest?.gameObject.SetActive(false);
                queue.RemoveAt(0);
            }
        }

        public static void PrewarmPool()
        {
            EnsurePoolRoot();

            while (_pool.Count < DamageNumberSettings.MaxWorldPoolSize)
            {
                GameObject go = new($"FloatingDamageNumber_3D_{_pool.Count}");
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
                GameObject go = new($"FloatingDamageNumber_3D_{_pool.Count}");
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
            while (_incomingDamage.TryDequeue(out _))
            {
            }

            _pendingBuckets.Clear();
            foreach (var q in _shipQueues.Values)
            {
                foreach (var item in q)
                {
                    item?.gameObject.SetActive(false);
                }
            }
            _shipQueues.Clear();
        }
    }
}

