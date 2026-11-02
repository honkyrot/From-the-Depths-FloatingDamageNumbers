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
        // Per-ship active damage number lists (Key: MainConstruct UniqueId, or -1 for unanchored)
        private static readonly Dictionary<int, List<FloatingElements>> _shipQueues = [];

        // Per-ship active construct health element and cached block health state
        private static readonly Dictionary<int, FloatingElements> _shipHealthElements = [];
        private static readonly Dictionary<int, ShipHealthCache> _shipHealthCache = [];

        private class ShipHealthCache
        {
            public float CurrentHealth;
            public float MaxHealth;
            public float LastFullScanTime = -999f;
            public float LastUiRefreshTime = -999f;
            public ConstructHealthMode LastMode;
        }

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
        public static void RecordHealing(MainConstruct? construct, float healingAmount, Vector3 hitPosition)
        {
            if (!DamageNumberSettings.ShowHealingNumbers) return;
            RecordDamage(construct, DamageNumberSettings.HealingDamageType, healingAmount, hitPosition);
        }

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

            UpdateConstructHealthElements(currentTime, deltaTime, activeCamera);
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

            // if false, all damage types will merge into a single number per ship, excludes healing
            if (!DamageNumberSettings.AggregateOnlySameDamageType && bucket.Type != DamageNumberSettings.HealingDamageType)
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
                if (DamageNumberSettings.ShowConstructHealth && bucket.Construct != null)
                {
                    UpdateOrSpawnConstructHealth(bucket.Construct, bucket.TotalDamage, bucket.TotalDamage >= DamageNumberSettings.LargeHitThreshold, currentTime);
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

            if (DamageNumberSettings.ShowConstructHealth && bucket.Construct != null)
            {
                UpdateOrSpawnConstructHealth(bucket.Construct, bucket.TotalDamage, isLargeHit, currentTime);
            }

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

            if (oldest != null && oldest.IsHealthElement)
            {
                _shipHealthElements.Remove(oldest.ConstructId);
            }

            return oldest!;
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

            foreach (var healthElem in _shipHealthElements.Values)
            {
                healthElem?.gameObject.SetActive(false);
            }
            _shipHealthElements.Clear();
            _shipHealthCache.Clear();
        }

        /// <summary>
        /// Updates the existing health display for a construct or creates one from the object pool.
        /// </summary>
        private static void UpdateOrSpawnConstructHealth(MainConstruct construct, float damageDelta, bool isLargeHit, float currentTime)
        {
            int constructId = construct.UniqueId;
            ResolveConstructHealth(construct, damageDelta, currentTime, out float currentHealth, out float maxHealth);

            if (maxHealth <= 0f)
            {
                return;
            }

            float baseLife = DamageNumberSettings.NumberLifetime;
            if (isLargeHit && !DamageNumberSettings.UseDPSMode)
            {
                baseLife *= DamageNumberSettings.LargeHitLifetimeMultiplier;
            }

            // checks for existing elements
            if (_shipHealthElements.TryGetValue(constructId, out FloatingElements? existing) && existing != null && existing.gameObject.activeSelf && existing.IsHealthElement)
            {
                existing.UpdateHealth(currentHealth, maxHealth, baseLife, currentTime);
                return;
            }

            // Spawn a new health element from the pool
            FloatingElements healthInstance = GetPooledInstance();
            healthInstance.InitializeHealth(construct, currentHealth, maxHealth, baseLife, currentTime);
            _shipHealthElements[constructId] = healthInstance;
        }

        /// <summary>
        /// Updates active construct health elements and removes expired or invalid elements.
        /// </summary>
        private static void UpdateConstructHealthElements(float currentTime, float deltaTime, Camera? activeCamera)
        {
            if (!DamageNumberSettings.ShowConstructHealth)
            {
                if (_shipHealthElements.Count > 0)
                {
                    foreach (var elem in _shipHealthElements.Values)
                    {
                        elem?.gameObject.SetActive(false);
                    }
                    _shipHealthElements.Clear();
                    _shipHealthCache.Clear();
                }
                return;
            }

            if (_shipHealthElements.Count == 0)
            {
                return;
            }

            List<int>? expiredHealthKeys = null;

            foreach (var kvp in _shipHealthElements)
            {
                int shipId = kvp.Key;
                FloatingElements elem = kvp.Value;

                if (elem == null || elem.IsExpired(currentTime) || !elem.gameObject.activeSelf || elem.Construct == null || elem.Construct.Destroyed)
                {
                    elem?.gameObject.SetActive(false);
                    expiredHealthKeys ??= [];
                    expiredHealthKeys.Add(shipId);
                    continue;
                }

                // Periodically resync displayed health (e.g. for block deaths, repairs, or mode changes)
                if (!_shipHealthCache.TryGetValue(shipId, out ShipHealthCache? cache) ||
                    (currentTime - cache.LastUiRefreshTime) >= 0.25f ||
                    cache.LastMode != DamageNumberSettings.HealthCalculationMode)
                {
                    ResolveConstructHealth(elem.Construct, 0f, currentTime, out float curHp, out float maxHp);
                    elem.RefreshDisplayedHealth(curHp, maxHp);
                }

                elem.ManualUpdate(currentTime, deltaTime, activeCamera);
                if (!elem.gameObject.activeSelf)
                {
                    expiredHealthKeys ??= [];
                    expiredHealthKeys.Add(shipId);
                }
            }

            if (expiredHealthKeys != null)
            {
                for (int i = 0; i < expiredHealthKeys.Count; i++)
                {
                    int key = expiredHealthKeys[i];
                    _shipHealthElements.Remove(key);
                    _shipHealthCache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Resolves the current and maximum health values for a construct.
        /// </summary>
        private static void ResolveConstructHealth(MainConstruct construct, float damageDelta, float currentTime, out float currentHealth, out float maxHealth)
        {
            int constructId = construct.UniqueId;
            if (!_shipHealthCache.TryGetValue(constructId, out ShipHealthCache? cache))
            {
                cache = new ShipHealthCache();
                _shipHealthCache[constructId] = cache;
            }

            ConstructHealthMode mode = DamageNumberSettings.HealthCalculationMode;
            bool modeChanged = cache.LastMode != mode;
            cache.LastMode = mode;
            cache.LastUiRefreshTime = currentTime;

            // If any but TotalBlockHealth mode, use the block count method
            if (mode == ConstructHealthMode.BlockCount || mode == ConstructHealthMode.Percentage)
            {
                try
                {
                    var basics = construct.AllBasicsRestricted;
                    if (basics != null)
                    {
                        currentHealth = basics.GetNumberAliveBlocksIncludingSubConstructables();
                        maxHealth = basics.GetNumberBlocksIncludingSubConstructables();
                        cache.CurrentHealth = currentHealth;
                        cache.MaxHealth = maxHealth;
                        return;
                    }
                }
                catch
                {
                }

                currentHealth = cache.CurrentHealth;
                maxHealth = cache.MaxHealth;
                return;
            }

            // TotalBlockHealth mode: full block scan throttled to once every 0.5s per ship, with instant delta subtraction between scans
            if (modeChanged || cache.MaxHealth <= 0f || (currentTime - cache.LastFullScanTime) >= 0.5f)
            {
                SumConstructBlockHealth(construct, out float scannedCur, out float scannedMax);
                cache.CurrentHealth = scannedCur;
                cache.MaxHealth = scannedMax;
                cache.LastFullScanTime = currentTime;
            }
            else if (damageDelta > 0f)
            {
                cache.CurrentHealth = Mathf.Max(0f, cache.CurrentHealth - damageDelta);
            }

            currentHealth = cache.CurrentHealth;
            maxHealth = cache.MaxHealth;
        }

        /// <summary>
        /// Calculates the total current and maximum health of a construct and its active subconstructs.
        /// </summary>
        private static void SumConstructBlockHealth(MainConstruct construct, out float currentHealth, out float maxHealth)
        {
            currentHealth = 0f;
            maxHealth = 0f;

            try
            {
                var basics = construct.AllBasicsRestricted;
                if (basics == null) return;

                AccumulateBlocksHealth(basics.AliveAndDead?.Blocks, ref currentHealth, ref maxHealth);

                var subs = basics.AllSubconstructsBelowUs;
                if (subs != null)
                {
                    int subCount = subs.Count;
                    for (int i = 0; i < subCount; i++)
                    {
                        var sub = subs[i];
                        if (sub != null && !sub.Destroyed && sub.AllBasicsRestricted != null)
                        {
                            AccumulateBlocksHealth(sub.AllBasicsRestricted.AliveAndDead?.Blocks, ref currentHealth, ref maxHealth);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        ///Accumulates the current and maximum health of the specified blocks.
        /// </summary>
        private static void AccumulateBlocksHealth(List<Block>? blocks, ref float currentHealth, ref float maxHealth)
        {
            if (blocks == null) return;
            int count = blocks.Count;
            for (int i = 0; i < count; i++)
            {
                Block b = blocks[i];
                if (b == null) continue;
                maxHealth += b.MaximumHealth;
                if (b.IsAlive)
                {
                    currentHealth += b.CurrentHealth;
                }
            }
        }
    }
}

