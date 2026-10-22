using System;
using BrilliantSkies.Ftd.DamageModels;
using UnityEngine;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// Configuration settings for the Floating Damage Numbers mod.
    /// </summary>
    public static class DamageNumberSettings
    {
        public static bool EnableDamageNumbers = true; // master toggle for the mod
        //public static bool ShowHealingNumbers = false; // show green healing numbers when a block is repaired or healed, maybe later 
        // either health per block, construct effective health, or blocks alive.

        #region Shared Settings

        #region Damage Type Colors

        public static Color KineticColor = new(1.00f, 0.95f, 0.75f, 1f); // white (nearly)
        public static Color ExplosiveColor = new(1.00f, 0.50f, 0.05f, 1f); // orange
        public static Color EmpColor = new(0.00f, 0.85f, 1.00f, 1f); // blue (different)
        public static Color LaserColor = new(1.00f, 0.20f, 0.20f, 1f); // red
        public static Color ParticleColor = new(0.55f, 0.95f, 1.00f, 1f); // light blue
        public static Color PlasmaColor = new(0.00f, 1.00f, 0.15f, 1f); // green
        public static Color FireColor = new(1.00f, 0.75f, 0.00f, 1f); // gold
        public static Color CrashColor = new(0.50f, 0.10f, 0.00f, 1f); // deep brown
        public static Color DefaultColor = new(1.00f, 1.00f, 1.00f, 1f); // white

        public static Color GetColor(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Kinetic => KineticColor,
                DamageType.Explosive => ExplosiveColor,
                DamageType.Emp => EmpColor,
                DamageType.Laser => LaserColor,
                DamageType.Particle => ParticleColor,
                DamageType.Plasma => PlasmaColor,
                DamageType.Fire => FireColor,
                DamageType.Crash => CrashColor,
                _ => DefaultColor
            };
        }

        #endregion

        #region Visual & Scale Settings

        public static float BaseTextScale = 0.035f;
        [Obsolete("maybe later, but I felt like different scales mattered a little less right now")]
        public static float LargeHitTextScale = 0.05f;
        public static float PopBounceScale = 1.25f;
        public static float PopBounceDuration = 0.5f;

        /// <summary>
        /// Distance scaling curve exponent, where 1.0 = linear, <1.0 = slower falloff, >1.0 = faster falloff. 0 is fixed
        /// </summary>
        public static float DistanceScaleExponent = 0.75f;

        /// <summary>
        /// Reference distance in meters where scale is considered 1.0x. Larger numbers will scale down, smaller numbers will scale up. This is used in conjunction with DistanceScaleExponent to determine the final scale of the number based on distance from the camera.
        /// </summary>
        public static float ReferenceDistance = 60f;

        /// <summary>
        /// Absolute maximum 3D scale clamp to prevent distant numbers from ballooning over targets.
        /// </summary>
        public static float MaxTextScale = 40f;

        /// <summary>
        /// Minimum 3D scale clamp for very close-up inspection.
        /// </summary>
        public static float MinTextScale = 0.5f;

        /// <summary>
        /// Multiplier for dynamic sky elevation that lifts damage numbers above the target silhouette.
        /// Used with DistanceElevationExponent to scale smoothly with distance.
        /// </summary>
        public static float DistanceElevationFactor = 1.25f;

        /// <summary>
        /// Distance elevation curve exponent (sub-linear power curve).
        /// 1.0 = linear growth, 0.5 = square root falloff.
        /// Values below 1.0 dampen elevation gain at long ranges so distant numbers stay near the target,
        /// while giving close-range targets enough height to stay clear of the superstructure.
        /// </summary>
        public static float DistanceElevationExponent = 0.55f;

        /// <summary>
        /// Absolute ceiling clamp for sky elevation in meters.
        /// Prevents numbers on distant targets (e.g. >3000m) from floating too high into the clouds.
        /// </summary>
        public static float MaxDistanceElevation = 140f;

        /// <summary>
        /// Minimum floor for sky elevation in meters for close-range targets.
        /// </summary>
        public static float MinDistanceElevation = 0f;

        public static bool FormatAbbreviatedThousands = false;
        public static bool ShowDamageTypeTag = false;
        public static bool ShowAssociatedIcon = true; // show damage type icon

        #endregion

        #region Number Stacking (Multi-Type Hits e.g. APHE)

        /// <summary>
        /// Vertical spacing multiplier (relative to text height) when multiple damage types stack (e.g. AP+Chemical).
        /// </summary>
        public static float StackSpacingMultiplier = 1.35f;

        /// <summary>
        /// Distance threshold in meters to consider numbers co-located and stack them vertically.
        /// </summary>
        public static float StackProximityDistance = 15f;

        #endregion

        #region Movement & Lifetime Settings

        public static bool TrackConstructMovement = true;
        public static Vector3 BaseSpawnOffset = new(0f, 10.0f, 0f);
        public static float FloatUpSpeed = 2.0f;
        public static float NumberLifetime = 3.0f;
        public static float LargeHitLifetimeMultiplier = 1.2f;
        public static float FadeOutDuration = 0.5f;

        #endregion

        #region Other Settings

        /// <summary>
        /// Single shot damage threshold for large hit
        /// </summary>
        /// 
        public static float LargeHitThreshold = 10000f;  // threshold for pop triggers
        public static float HugeHitThreshold = 100000f; // threshold for something else

        public static bool AccumulateSustainedFire = true;  // here for the global setting
        public static bool AggregateOnlySameDamageType = true;
        public static bool HoverOverLastPositionHit = true;

        #endregion

        #region Private Settings  
        // not exposed to user, but can be changed in code for testing or debugging.

        public static bool EnableQueuePerShip = true;

        #endregion

        public static bool EnableDetailedSettings = false; // toggle between simple and detailed settings, I'd recommend leaving this off as the simple settings are more visually clean and less cluttered.

        #endregion

        #region Simple Settings
        // for the simple setting of floating damage numbers, 8 stacking damage numbers for each type above the COM of a ship instead of independent positions for each hit. This is less visually accurate but much simpler and less cluttered.

        #endregion

        #region Detailed Settings
        // position is included when detailed settings are enabled, otherwise numbers are anchored to COM of ship.

        /// <summary>
        /// Minimum damage amount required to display (filters sub-point noise).
        /// </summary>
        public static float MinDamageToDisplay = 5f;

        /// <summary>
        /// Maximum simultaneous numbers allowed per ship.
        /// </summary>
        public static int MaxNumbersPerShip = 8; // i do not recommend increasing this.

        /// <summary>
        /// Hard cap on total 3D TextMesh objects in the world pool.
        /// </summary>
        public static int MaxWorldPoolSize = 36; // i also do not recommend increasing this.

        #region Aggregation Settings

        /// <summary>
        /// Time window (in seconds) during which successive hits add into the existing number.
        /// </summary>
        public static float AccumulationWindowSeconds = 1f;

        // <summary>
        /// Minimum and Maximum distance (in meters) between hit points on the same ship to merge into one number.
        /// </summary>
        // public static float MinAccumulationDistance = 0.5f;
        public static float MaxAccumulationDistance = 25f;

        // public static float AccumulationRate = 0.1f; // the rate the distance increases when damage is at baseline. Falls off to 0 at 0 dmg.
        // public static float AccumulationBaseline = 2000f; // the hit threshold to do 100% rate.

        #endregion

        #endregion
    }
}