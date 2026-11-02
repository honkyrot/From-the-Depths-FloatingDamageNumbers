using System;
using HarmonyLib;
using BrilliantSkies.Core.Logger;
using UnityEngine;

namespace FloatingDamageNumbers
{
    [HarmonyPatch(typeof(Block), nameof(Block.RepairToBlock))]
    internal static class CalculateHealingEvents
    {
        [HarmonyPostfix]
        private static void Postfix(Block __instance, bool __result)
        {
            try
            {
                // Only trigger if enabled and the repair actually succeeded
                if (!DamageNumberSettings.EnableDamageNumbers || !DamageNumberSettings.ShowHealingNumbers) return;
                if (!__result || __instance == null) return;

                // If running as MP client and host has mod, host broadcasts authoritative events
                if (BrilliantSkies.Core.Net.IsClient && MultiplayerHandler.HostHasMod) return;

                float healAmount = __instance.MaximumHealth;
                if (healAmount < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(healAmount) || float.IsInfinity(healAmount))
                {
                    return;
                }

                MainConstruct? construct = __instance.MainConstruct as MainConstruct;
                Vector3 hitPosition = DamageNumberSettings.EnableDetailedSettings
                    ? __instance.GameWorldPosition
                    : (construct != null ? construct.CentreOfMass : Vector3.zero);

                DamageQueueManager.RecordDamage(construct, DamageNumberSettings.HealingDamageType, healAmount, hitPosition);
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in RepairToBlock Postfix: {ex}", LogOptions._AlertDevInGame);
            }
        }
    }
}