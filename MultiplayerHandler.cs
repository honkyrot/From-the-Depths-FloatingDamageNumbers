using System;
using System.Collections.Generic;
using HarmonyLib;
using BrilliantSkies.Core;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.Ftd.DamageModels;
using NetInfrastructure;
using UnityEngine;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// Handles multiplayer synchronization of floating damage numbers.
    /// Host accumulates damage and broadcasts at the configured NetworkTickrate (default 30 Hz).
    /// If the host does not have the mod installed, clients automatically fall back to local block destruction hooks.
    /// </summary>
    public static class MultiplayerHandler
    {
        private class BroadcastBucket
        {
            public MainConstruct? Construct;
            public DamageType Type;
            public float TotalDamage;
            public Vector3 Position;
            public int HitCount;
        }

        private static readonly Dictionary<long, BroadcastBucket> _pendingBroadcastBuckets = [];
        private static float _lastBroadcastTime = 0f;

        /// <summary>
        /// True if the current multiplayer host has the mod installed and is transmitting damage packets.
        /// </summary>
        public static bool HostHasMod { get; internal set; }

        public static float LastHostPacketTime { get; internal set; }

        static MultiplayerHandler()
        {
            GameEvents.SceneChange.RegWithEvent(ResetState);
        }

        public static void ResetState()
        {
            HostHasMod = false;
            LastHostPacketTime = 0f;
            _pendingBroadcastBuckets.Clear();
            _lastBroadcastTime = 0f;
        }

        /// <summary>
        /// Attaches a DamageSyncReceiver to the construct's network identity so it can receive RPCs.
        /// </summary>
        public static void AttachReceiver(INetworkIdentity identity)
        {
            if (identity == null) return;

            DamageSyncReceiver receiver = new(identity);
            identity.MonitorThis(receiver);

            if (Net.IsServer)
            {
                identity.RPCMimic("Client_ModHandshake", RPCMimicMode.Others);
            }
        }

        /// <summary>
        /// Enqueues a coalesced damage event into the host-side broadcast buffer.
        /// Flushed to clients at the configured NetworkTickrate.
        /// </summary>
        public static void QueueDamageForBroadcast(MainConstruct? construct, DamageType type, float damageAmount, Vector3 position)
        {
            if (!Net.IsServer || construct == null) return;
            if (damageAmount < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(damageAmount) || float.IsInfinity(damageAmount)) return;

            int constructId = construct.UniqueId;
            long key = ((long)constructId << 32) | (long)(uint)type;

            if (_pendingBroadcastBuckets.TryGetValue(key, out BroadcastBucket? bucket))
            {
                bucket.TotalDamage += damageAmount;
                bucket.HitCount++;
                if (DamageNumberSettings.EnableDetailedSettings)
                {
                    float weight = 1f / bucket.HitCount;
                    bucket.Position = Vector3.Lerp(bucket.Position, position, weight);
                }
            }
            else
            {
                _pendingBroadcastBuckets[key] = new BroadcastBucket
                {
                    Construct = construct,
                    Type = type,
                    TotalDamage = damageAmount,
                    Position = position,
                    HitCount = 1
                };
            }
        }

        /// <summary>
        /// Flushes accumulated broadcast buckets to clients at the configured tickrate (e.g. 30 Hz).
        /// </summary>
        public static void Tick(float currentTime)
        {
            if (!Net.IsServer) return;

            float targetHz = Mathf.Clamp(DamageNumberSettings.NetworkTickrate, 10f, 60f);
            float interval = 1f / targetHz;

            if (currentTime - _lastBroadcastTime < interval)
            {
                return;
            }

            _lastBroadcastTime = currentTime;

            if (_pendingBroadcastBuckets.Count == 0) return;

            foreach (var kvp in _pendingBroadcastBuckets)
            {
                BroadcastBucket bucket = kvp.Value;
                if (bucket.Construct != null)
                {
                    INetworkIdentity? identity = bucket.Construct.NetworkIdentity;
                    if (identity != null && identity.IsValid)
                    {
                        identity.RPCMimic("Client_ReceiveFloatingDamage", RPCMimicMode.Others, (int)bucket.Type, bucket.TotalDamage, bucket.Position);
                    }
                }
            }

            _pendingBroadcastBuckets.Clear();
        }

        /// <summary>
        /// Invoked when a damage packet is received from the host over the network.
        /// </summary>
        public static void OnDamageReceivedFromHost(INetworkIdentity identity, int damageType, float damageAmount, Vector3 position)
        {
            HostHasMod = true;
            LastHostPacketTime = Time.time;

            MainConstruct? construct = null;
            if (identity.GetLinkedClass<MainConstruct>(out var mc))
            {
                construct = mc;
            }
            else if (identity.GetLinkedClass<AllConstruct>(out var ac))
            {
                construct = ac as MainConstruct;
            }

            DamageQueueManager.RecordDamage(construct, (DamageType)damageType, damageAmount, position);
        }
    }

    /// <summary>
    /// Monitored network receiver registered on construct NetworkIdentities.
    /// Methods marked with [NetworkMethod] are invoked when matching RPCs arrive from the host.
    /// </summary>
    public class DamageSyncReceiver
    {
        private readonly INetworkIdentity _identity;

        public DamageSyncReceiver(INetworkIdentity identity)
        {
            _identity = identity;
        }

        [NetworkMethod]
        public void Client_ModHandshake()
        {
            MultiplayerHandler.HostHasMod = true;
        }

        [NetworkMethod]
        public void Client_ReceiveFloatingDamage(int damageType, float damageAmount, Vector3 position)
        {
            MultiplayerHandler.OnDamageReceivedFromHost(_identity, damageType, damageAmount, position);
        }
    }

    /// <summary>
    /// Hooks ConstructableMP.SetNvid to attach our DamageSyncReceiver to each construct's network identity.
    /// </summary>
    [HarmonyPatch(typeof(ConstructableMP), "SetNvid")]
    internal static class ConstructableMpSetNvidPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ConstructableMP __instance)
        {
            try
            {
                if (__instance?.MyNetworkIdentity != null)
                {
                    MultiplayerHandler.AttachReceiver(__instance.MyNetworkIdentity);
                }
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in SetNvid Postfix: {ex}", LogOptions.OnlyInDeveloperLog);
            }
        }
    }

    /// <summary>
    /// Fallback patch for multiplayer clients when the host does NOT have the mod installed.
    /// Intercepts block deaths so clients can still see floating numbers.
    /// Suppressed when HostHasMod is true to avoid duplicate numbers.
    /// </summary>
    [HarmonyPatch(typeof(ConstructBasics), "BlockKilledButMaintainedInDesign", [typeof(Block)])]
    internal static class MultiplayerClientBlockKilledFallbackPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Block B)
        {
            try
            {
                if (!Net.IsClient || MultiplayerHandler.HostHasMod || !DamageNumberSettings.EnableDamageNumbers || B == null) return;
                if (!B.IsAlive) return;

                float damageDealt = B.CurrentHealth > 0f ? B.CurrentHealth : B.MaximumHealth;
                if (damageDealt < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(damageDealt) || float.IsInfinity(damageDealt))
                {
                    return;
                }

                DamageType damageType = B.FireDamageFraction > 0.1f ? DamageType.Fire : DamageType.Explosive;
                MainConstruct? construct = B.MainConstruct as MainConstruct;
                Vector3 hitPosition = DamageNumberSettings.EnableDetailedSettings
                    ? B.GameWorldPosition
                    : (construct != null ? construct.CentreOfMass : Vector3.zero);

                DamageQueueManager.RecordDamage(construct, damageType, damageDealt, hitPosition);
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in MP Client BlockKilled Fallback: {ex}", LogOptions._AlertDevInGame);
            }
        }
    }

    /// <summary>
    /// Fallback patch for catastrophic block removals on clients when the host does NOT have the mod.
    /// Suppressed when HostHasMod is true.
    /// </summary>
    [HarmonyPatch(typeof(ConstructBasics), "PermanentlyRemoveBlock", [typeof(Block), typeof(bool)])]
    internal static class MultiplayerClientBlockRemovedFallbackPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Block B, bool destroyBlock)
        {
            try
            {
                if (!Net.IsClient || MultiplayerHandler.HostHasMod || !DamageNumberSettings.EnableDamageNumbers || B == null || !destroyBlock) return;
                if (!B.IsAlive) return;

                float damageDealt = B.CurrentHealth > 0f ? B.CurrentHealth : B.MaximumHealth;
                if (damageDealt < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(damageDealt) || float.IsInfinity(damageDealt))
                {
                    return;
                }

                DamageType damageType = DamageType.Explosive;
                MainConstruct? construct = B.MainConstruct as MainConstruct;
                Vector3 hitPosition = DamageNumberSettings.EnableDetailedSettings
                    ? B.GameWorldPosition
                    : (construct != null ? construct.CentreOfMass : Vector3.zero);

                DamageQueueManager.RecordDamage(construct, damageType, damageDealt, hitPosition);
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in MP Client BlockRemoved Fallback: {ex}", LogOptions._AlertDevInGame);
            }
        }
    }
}

