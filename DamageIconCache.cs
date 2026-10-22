using System;
using System.Collections.Generic;
using BrilliantSkies.FromTheDepths.Game;
using BrilliantSkies.Ftd.DamageModels;
using BrilliantSkies.ScriptableObjects;
using UnityEngine;
using BrilliantSkies.Core.Logger;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// To grab and convert the Texture2D images of damage types into 3d sprites.
    /// </summary>
    public static class DamageIconCache
    {
        private static readonly Dictionary<DamageType, Sprite> _cache = new();

        public static Sprite? GetIcon(DamageType damageType)
        {
            if (_cache.Count == 0)
            {
                Initialize();
            }

            _cache.TryGetValue(damageType, out Sprite? sprite);
            return sprite;
        }

        public static void Initialize()
        {
            if (_cache.Count > 0)
            {
                return;
            }

            try
            {
                SO_HudDamage? so = LazyLoader.HudDamage?.Get();
                if (so == null) return;

                CacheTexture(DamageType.Kinetic, so.AP);
                CacheTexture(DamageType.Explosive, so.EXP);
                CacheTexture(DamageType.Emp, so.EMP);
                CacheTexture(DamageType.Laser, so.LASER);
                CacheTexture(DamageType.Particle, so.PARTICLE);
                CacheTexture(DamageType.Plasma, so.PLASMA);
                CacheTexture(DamageType.Fire, so.FIRE);
                CacheTexture(DamageType.Crash, so.CRASH);
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in DamageIconCache.Initialize: {ex}", LogOptions.None);
            }
        }

        private static void CacheTexture(DamageType type, Texture2D? tex)
        {
            if (tex == null) return;
            _cache[type] = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );
        }
    }
}