using System;
using HarmonyLib;
using BrilliantSkies.Ftd.DamageModels;
using UnityEngine;
using BrilliantSkies.Core.Logger;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// Harmony patch intercepting Block.ApplyDamage to record damage dealt, damage type,
    /// and spatial hit coordinates on the construct.
    /// </summary>
    [HarmonyPatch(typeof(Block), "ApplyDamage", new Type[]
    {
        typeof(IDamageDescription),
        typeof(float)
    })]
    internal static class CalculateDamageEvents
    {
        [HarmonyPrefix]
        private static void Prefix(Block __instance, out float __state)
        {
            __state = __instance != null ? __instance.CurrentHealth : 0f;
        }

        [HarmonyPostfix]
        private static void Postfix(Block __instance, IDamageDescription? DD, float __state)
        {
            try
            {
                if (!DamageNumberSettings.EnableDamageNumbers) return;
                if (__instance == null || DD == null) return;

                float healthBefore = __state;
                float healthAfter = __instance.CurrentHealth;
                float damageDealt = healthBefore - healthAfter;

                if (damageDealt < 0f)
                {
                    if (!DamageNumberSettings.ShowHealingNumbers) return;
                    // This is a healing event
                    AdvLogger.LogError("someone healed, {damageDealt: " + damageDealt + ", healthBefore: " + healthBefore + ", healthAfter: " + healthAfter, BrilliantSkies.Core.Logger.LogOptions._AlertDevAndCustomerInGame);
                    return;
                }

                if (damageDealt < DamageNumberSettings.MinDamageToDisplay || float.IsNaN(damageDealt) || float.IsInfinity(damageDealt))
                {
                    return;
                }

                DamageType damageType = DD.DamageType;
                Vector3 hitPosition = Vector3.zero;

                if (DamageNumberSettings.EnableDetailedSettings)
                {
                    // For explosions, prioritize the epicenter position if available
                    hitPosition = __instance.GameWorldPosition;
                    if (DD is ExplosionDamageDescription expDD && expDD.Position != Vector3.zero)
                    {
                        hitPosition = expDD.Position;
                    }
                }
                else
                {
                    hitPosition = __instance.MainConstruct.CentreOfMass;
                }

                MainConstruct? construct = __instance.MainConstruct as MainConstruct;

                DamageQueueManager.RecordDamage(construct, damageType, damageDealt, hitPosition);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FloatingDamageNumbers] Error in ApplyDamage Postfix: {ex}");
            }
        }
    }
}

