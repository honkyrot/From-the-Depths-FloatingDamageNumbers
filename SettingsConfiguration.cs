using System;
using System.Linq.Expressions;
using System.Reflection;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Examples;
using BrilliantSkies.Ui.Consoles.Getters;
using BrilliantSkies.Ui.Consoles.Interpretters;
using BrilliantSkies.Ui.Consoles.Interpretters.Simple;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Choices;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Numbers;
using BrilliantSkies.Ui.Consoles.Segments;
using BrilliantSkies.Ui.Consoles.Styles;
using BrilliantSkies.Ui.Layouts.DropDowns;
using BrilliantSkies.Ui.Tips;
using HarmonyLib;
using UnityEngine;

namespace FloatingDamageNumbers
{
    public enum SettingsMode
    {
        Simple = 0,
        Detailed = 1
    }

    /// <summary>
    /// Persistent profile module for Floating Damage Numbers configuration.
    /// </summary>
    public class DamageNumberSettingsConfig : ProfileModule<DamageNumberSettingsConfig.InternalData>
    {
        public class InternalData
        {
            // Master Controls
            public bool EnableDamageNumbers { get; set; } = true;
            //public bool ShowHealingNumbers { get; set; } = false;
            public bool EnableDetailedSettings { get; set; } = false;

            // Shared - Formatting & Icons
            public bool ShowAssociatedIcon { get; set; } = true;
            public bool ShowDamageTypeTag { get; set; } = false;
            public bool FormatAbbreviatedThousands { get; set; } = false;
            public bool AggregateOnlySameDamageType { get; set; } = true;

            // Shared - Text Scale & Distance
            public float BaseTextScale { get; set; } = 0.035f;
            public float PopBounceScale { get; set; } = 1.25f;
            public float PopBounceDuration { get; set; } = 0.5f;
            public float ReferenceDistance { get; set; } = 60f;
            public float DistanceScaleExponent { get; set; } = 0.75f;
            public float MinTextScale { get; set; } = 0.5f;
            public float MaxTextScale { get; set; } = 40f;

            // Shared - Sky Elevation & Stacking
            public float DistanceElevationFactor { get; set; } = 1.25f;
            public float DistanceElevationExponent { get; set; } = 0.65f;
            public float MaxDistanceElevation { get; set; } = 140f;
            public float StackSpacingMultiplier { get; set; } = 1.35f;
            public float StackProximityDistance { get; set; } = 15f;

            // Shared - Motion & Lifetime
            public bool TrackConstructMovement { get; set; } = true;
            public float NumberLifetime { get; set; } = 3.0f;
            public float LargeHitLifetimeMultiplier { get; set; } = 1.2f;
            public float FadeOutDuration { get; set; } = 0.5f;
            public float LargeHitThreshold { get; set; } = 10000f;

            // Simple Mode
            public float BaseSpawnOffsetY { get; set; } = 10.0f;

            // Detailed Mode
            public float MinDamageToDisplay { get; set; } = 5f;
            public int MaxNumbersPerShip { get; set; } = 8;
            public int MaxWorldPoolSize { get; set; } = 36;
            public bool AccumulateSustainedFire { get; set; } = true;
            public float AccumulationWindowSeconds { get; set; } = 1.0f;
            public float MaxAccumulationDistance { get; set; } = 25.0f;
            public bool HoverOverLastPositionHit { get; set; } = true;
        }

        public override ModuleType ModuleType => ModuleType.Options;
        protected override string FilenameAndExtension => "profile.FloatingDamageNumbersConfig";

        public static DamageNumberSettingsConfig? Get()
        {
            try
            {
                return ProfileManager.Instance?.GetModule<DamageNumberSettingsConfig>();
            }
            catch
            {
                return null;
            }
        }

        public override void PostLoad(LoadStatus status)
        {
            base.PostLoad(status);
            ApplyToSettings();
        }

        public void ApplyToSettings()
        {
            DamageNumberSettings.EnableDamageNumbers = EnableDamageNumbers;
            //DamageNumberSettings.ShowHealingNumbers = ShowHealingNumbers;
            DamageNumberSettings.EnableDetailedSettings = EnableDetailedSettings;

            DamageNumberSettings.ShowAssociatedIcon = ShowAssociatedIcon;
            DamageNumberSettings.ShowDamageTypeTag = ShowDamageTypeTag;
            DamageNumberSettings.FormatAbbreviatedThousands = FormatAbbreviatedThousands;
            DamageNumberSettings.AggregateOnlySameDamageType = AggregateOnlySameDamageType;

            DamageNumberSettings.BaseTextScale = BaseTextScale;
            DamageNumberSettings.PopBounceScale = PopBounceScale;
            DamageNumberSettings.PopBounceDuration = PopBounceDuration;
            DamageNumberSettings.ReferenceDistance = ReferenceDistance;
            DamageNumberSettings.DistanceScaleExponent = DistanceScaleExponent;
            DamageNumberSettings.MinTextScale = MinTextScale;
            DamageNumberSettings.MaxTextScale = MaxTextScale;

            DamageNumberSettings.DistanceElevationFactor = DistanceElevationFactor;
            DamageNumberSettings.DistanceElevationExponent = DistanceElevationExponent;
            DamageNumberSettings.MaxDistanceElevation = MaxDistanceElevation;
            DamageNumberSettings.StackSpacingMultiplier = StackSpacingMultiplier;
            DamageNumberSettings.StackProximityDistance = StackProximityDistance;

            DamageNumberSettings.TrackConstructMovement = TrackConstructMovement;
            DamageNumberSettings.NumberLifetime = NumberLifetime;
            DamageNumberSettings.LargeHitLifetimeMultiplier = LargeHitLifetimeMultiplier;
            DamageNumberSettings.FadeOutDuration = FadeOutDuration;
            DamageNumberSettings.LargeHitThreshold = LargeHitThreshold;

            DamageNumberSettings.BaseSpawnOffset = new Vector3(0f, BaseSpawnOffsetY, 0f);

            DamageNumberSettings.MinDamageToDisplay = MinDamageToDisplay;
            DamageNumberSettings.MaxNumbersPerShip = MaxNumbersPerShip;
            DamageNumberSettings.MaxWorldPoolSize = MaxWorldPoolSize;
            DamageNumberSettings.AccumulateSustainedFire = AccumulateSustainedFire;
            DamageNumberSettings.AccumulationWindowSeconds = AccumulationWindowSeconds;
            DamageNumberSettings.MaxAccumulationDistance = MaxAccumulationDistance;
            DamageNumberSettings.HoverOverLastPositionHit = HoverOverLastPositionHit;
        }

        #region Properties

        public bool EnableDamageNumbers
        {
            get => Internal.EnableDamageNumbers;
            set { Internal.EnableDamageNumbers = value; DamageNumberSettings.EnableDamageNumbers = value; }
        }

        //public bool ShowHealingNumbers
        //{
        //    get => Internal.ShowHealingNumbers;
        //    set { Internal.ShowHealingNumbers = value; DamageNumberSettings.ShowHealingNumbers = value; }
        //}

        public bool EnableDetailedSettings
        {
            get => Internal.EnableDetailedSettings;
            set { Internal.EnableDetailedSettings = value; DamageNumberSettings.EnableDetailedSettings = value; }
        }

        public bool ShowAssociatedIcon
        {
            get => Internal.ShowAssociatedIcon;
            set { Internal.ShowAssociatedIcon = value; DamageNumberSettings.ShowAssociatedIcon = value; }
        }

        public bool ShowDamageTypeTag
        {
            get => Internal.ShowDamageTypeTag;
            set { Internal.ShowDamageTypeTag = value; DamageNumberSettings.ShowDamageTypeTag = value; }
        }

        public bool FormatAbbreviatedThousands
        {
            get => Internal.FormatAbbreviatedThousands;
            set { Internal.FormatAbbreviatedThousands = value; DamageNumberSettings.FormatAbbreviatedThousands = value; }
        }

        public bool AggregateOnlySameDamageType
        {
            get => Internal.AggregateOnlySameDamageType;
            set { Internal.AggregateOnlySameDamageType = value; DamageNumberSettings.AggregateOnlySameDamageType = value; }
        }

        public float BaseTextScale
        {
            get => Internal.BaseTextScale;
            set { Internal.BaseTextScale = value; DamageNumberSettings.BaseTextScale = value; }
        }

        public float PopBounceScale
        {
            get => Internal.PopBounceScale;
            set { Internal.PopBounceScale = value; DamageNumberSettings.PopBounceScale = value; }
        }

        public float PopBounceDuration
        {
            get => Internal.PopBounceDuration;
            set { Internal.PopBounceDuration = value; DamageNumberSettings.PopBounceDuration = value; }
        }

        public float ReferenceDistance
        {
            get => Internal.ReferenceDistance;
            set { Internal.ReferenceDistance = value; DamageNumberSettings.ReferenceDistance = value; }
        }

        public float DistanceScaleExponent
        {
            get => Internal.DistanceScaleExponent;
            set { Internal.DistanceScaleExponent = value; DamageNumberSettings.DistanceScaleExponent = value; }
        }

        public float MinTextScale
        {
            get => Internal.MinTextScale;
            set { Internal.MinTextScale = value; DamageNumberSettings.MinTextScale = value; }
        }

        public float MaxTextScale
        {
            get => Internal.MaxTextScale;
            set { Internal.MaxTextScale = value; DamageNumberSettings.MaxTextScale = value; }
        }

        public float DistanceElevationFactor
        {
            get => Internal.DistanceElevationFactor;
            set { Internal.DistanceElevationFactor = value; DamageNumberSettings.DistanceElevationFactor = value; }
        }

        public float DistanceElevationExponent
        {
            get => Internal.DistanceElevationExponent;
            set { Internal.DistanceElevationExponent = value; DamageNumberSettings.DistanceElevationExponent = value; }
        }

        public float MaxDistanceElevation
        {
            get => Internal.MaxDistanceElevation;
            set { Internal.MaxDistanceElevation = value; DamageNumberSettings.MaxDistanceElevation = value; }
        }

        public float StackSpacingMultiplier
        {
            get => Internal.StackSpacingMultiplier;
            set { Internal.StackSpacingMultiplier = value; DamageNumberSettings.StackSpacingMultiplier = value; }
        }

        public float StackProximityDistance
        {
            get => Internal.StackProximityDistance;
            set { Internal.StackProximityDistance = value; DamageNumberSettings.StackProximityDistance = value; }
        }

        public bool TrackConstructMovement
        {
            get => Internal.TrackConstructMovement;
            set { Internal.TrackConstructMovement = value; DamageNumberSettings.TrackConstructMovement = value; }
        }

        public float NumberLifetime
        {
            get => Internal.NumberLifetime;
            set { Internal.NumberLifetime = value; DamageNumberSettings.NumberLifetime = value; }
        }

        public float LargeHitLifetimeMultiplier
        {
            get => Internal.LargeHitLifetimeMultiplier;
            set { Internal.LargeHitLifetimeMultiplier = value; DamageNumberSettings.LargeHitLifetimeMultiplier = value; }
        }

        public float FadeOutDuration
        {
            get => Internal.FadeOutDuration;
            set { Internal.FadeOutDuration = value; DamageNumberSettings.FadeOutDuration = value; }
        }

        public float LargeHitThreshold
        {
            get => Internal.LargeHitThreshold;
            set { Internal.LargeHitThreshold = value; DamageNumberSettings.LargeHitThreshold = value; }
        }

        public float BaseSpawnOffsetY
        {
            get => Internal.BaseSpawnOffsetY;
            set
            {
                Internal.BaseSpawnOffsetY = value;
                DamageNumberSettings.BaseSpawnOffset = new Vector3(0f, value, 0f);
            }
        }

        public float MinDamageToDisplay
        {
            get => Internal.MinDamageToDisplay;
            set { Internal.MinDamageToDisplay = value; DamageNumberSettings.MinDamageToDisplay = value; }
        }

        public int MaxNumbersPerShip
        {
            get => Internal.MaxNumbersPerShip;
            set { Internal.MaxNumbersPerShip = value; DamageNumberSettings.MaxNumbersPerShip = value; }
        }

        public int MaxWorldPoolSize
        {
            get => Internal.MaxWorldPoolSize;
            set { Internal.MaxWorldPoolSize = value; DamageNumberSettings.MaxWorldPoolSize = value; }
        }

        public bool AccumulateSustainedFire
        {
            get => Internal.AccumulateSustainedFire;
            set { Internal.AccumulateSustainedFire = value; DamageNumberSettings.AccumulateSustainedFire = value; }
        }

        public float AccumulationWindowSeconds
        {
            get => Internal.AccumulationWindowSeconds;
            set { Internal.AccumulationWindowSeconds = value; DamageNumberSettings.AccumulationWindowSeconds = value; }
        }

        public float MaxAccumulationDistance
        {
            get => Internal.MaxAccumulationDistance;
            set { Internal.MaxAccumulationDistance = value; DamageNumberSettings.MaxAccumulationDistance = value; }
        }

        public bool HoverOverLastPositionHit
        {
            get => Internal.HoverOverLastPositionHit;
            set { Internal.HoverOverLastPositionHit = value; DamageNumberSettings.HoverOverLastPositionHit = value; }
        }

        #endregion
    }

    /// <summary>
    /// UI helper methods for generating console interpretters (toggles, sliders, dropdowns).
    /// </summary>
    public static class UIHelper
    {
        public static SubjectiveToggle<T> Bool<T>(
            T focus,
            string label,
            string tooltip,
            Expression<Func<T, bool>> property,
            Action<bool>? onValueChanged = null)
        {
            var member = (MemberExpression)property.Body;
            var propInfo = (PropertyInfo)member.Member;

            return SubjectiveToggle<T>.Quick(
                focus,
                label,
                new ToolTip(tooltip),
                (obj, value) =>
                {
                    propInfo.SetValue(obj, value);
                    onValueChanged?.Invoke(value);
                },
                obj => (bool)propInfo.GetValue(obj)
            );
        }

        public static SubjectiveFloatClampedWithBarFromMiddle<T> FloatSlider<T>(
            T focus,
            float min,
            float max,
            float inc,
            float middle,
            string display,
            Expression<Func<T, float>> property,
            string tooltip,
            Action<float>? onValueChanged = null)
        {
            var member = (MemberExpression)property.Body;
            var propInfo = (PropertyInfo)member.Member;
            var getter = property.Compile();

            return SubjectiveFloatClampedWithBarFromMiddle<T>.Quick(
                focus,
                min,
                max,
                inc,
                middle,
                M.m((T obj) => getter(obj)),
                display,
                (obj, value) =>
                {
                    propInfo.SetValue(obj, value);
                    onValueChanged?.Invoke(value);
                },
                new ToolTip(tooltip)
            );
        }

        public static SubjectiveFloatClampedWithBarFromMiddle<T> IntSlider<T>(
            T focus,
            int min,
            int max,
            int inc,
            int middle,
            string display,
            Expression<Func<T, int>> property,
            string tooltip,
            Action<int>? onValueChanged = null)
        {
            var member = (MemberExpression)property.Body;
            var propInfo = (PropertyInfo)member.Member;
            var getter = property.Compile();

            return SubjectiveFloatClampedWithBarFromMiddle<T>.Quick(
                focus,
                min,
                max,
                inc,
                middle,
                M.m((T obj) => (float)getter(obj)),
                display,
                (obj, value) =>
                {
                    int intVal = Mathf.RoundToInt(value);
                    propInfo.SetValue(obj, intVal);
                    onValueChanged?.Invoke(intVal);
                },
                new ToolTip(tooltip)
            );
        }
    }

    /// <summary>
    /// Mod settings tab integrated into From The Depths options menu.
    /// Shared settings remain visible at all times.
    /// Simple and Detailed settings are mutually exclusive based on selected mode.
    /// </summary>
    public class DamageNumberSettingsTab : SuperScreen<DamageNumberSettingsConfig>
    {
        private const float Spacing = 20f;

        public override Content Name => new Content(
            "<color=#4FC3F7>Damage Numbers</color>",
            new ToolTip("Configure floating combat damage numbers, visual appearance, and performance settings.")
        );

        public DamageNumberSettingsTab(ConsoleWindow window, DamageNumberSettingsConfig config)
            : base(window, config)
        {
        }

        public override void Build()
        {
            MasterSection();
            ModeSection();
            SharedSettingsSection();

            if (!_focus.EnableDetailedSettings)
            {
                SimpleSettingsSection();
            }
            else
            {
                DetailedSettingsSection();
            }
        }

        private void MasterSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 1);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Master Controls";

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Enable Floating Damage Numbers",
                "Master toggle to enable or disable floating damage numbers entirely.",
                I => I.EnableDamageNumbers
            ));

            //table.AddInterpretter(UIHelper.Bool(
            //    _focus,
            //    "Show Repair Numbers",
            //    "Display green floating numbers when damaged blocks are repaired.",
            //    I => I.ShowHealingNumbers
            //));
        }

        private void ModeSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 1);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Configuration Mode (Simple vs Detailed)";

            DropDownMenuAlt<SettingsMode> modeDropdown = new DropDownMenuAlt<SettingsMode>();
            modeDropdown.SetItems(
                new DropDownMenuAltItem<SettingsMode>
                {
                    Name = "<b>Simple Mode</b>",
                    ObjectForAction = SettingsMode.Simple,
                    ToolTip = "Pins numbers above Center-of-Mass. Little visual clutter."
                },
                new DropDownMenuAltItem<SettingsMode>
                {
                    Name = "<b>Detailed Mode</b>",
                    ObjectForAction = SettingsMode.Detailed,
                    ToolTip = "Shows damage at exact hit positions with full aggregation and pooling controls."
                }
            );

            table.AddInterpretter(
                new DropDown<DamageNumberSettingsConfig, SettingsMode>(
                    _focus,
                    modeDropdown,
                    (I, m) => (I.EnableDetailedSettings ? SettingsMode.Detailed : SettingsMode.Simple) == m,
                    (I, m) =>
                    {
                        I.EnableDetailedSettings = (m == SettingsMode.Detailed);
                        TriggerScreenRebuild();
                    }
                )
            );

            string modeDesc = _focus.EnableDetailedSettings
                ? "<color=#81D4FA><b>Detailed Mode:</b> Numbers appear at individual hit coordinates in 3D world space with per-hit aggregation.</color>"
                : "<color=#A5D6A7><b>Simple Mode:</b> Consolidated damage numbers stack cleanly above the construct's Center of Mass.</color>";

            table.AddInterpretter(StringDisplay.Quick(modeDesc));
        }

        private void SharedSettingsSection()
        {
            SharedDisplaySection();
            SharedScaleSection();
            SharedElevationAndStackingSection();
            SharedMovementAndLifetimeSection();
        }

        private void SharedDisplaySection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 2);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Display & Formatting";

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Show Damage Type Icons",
                "Show damage type icon next to the damage number.",
                I => I.ShowAssociatedIcon
            ), 0, 0);

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Show Damage Type Tag",
                "Append damage type text tag (e.g. [EMP], [KINETIC]).",
                I => I.ShowDamageTypeTag
            ), 0, 1);

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Abbreviate Large Numbers",
                "Format thousands with 'k' abbreviation (e.g. 12.5k, 512.5k).",
                I => I.FormatAbbreviatedThousands
            ), 1, 0);

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Aggregate Same Damage Type Only",
                "Only merge numbers of identical damage type together into the same stack. (default: true)",
                I => I.AggregateOnlySameDamageType
            ), 1, 1);

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Accumulate Sustained Fire",
                "Merge successive hits into one number if they occur within the accumulation window and distance (default: true). eg 3 hits in 1 second (or simple is on) = 1 number",
                I => I.AccumulateSustainedFire
            ), 0, 0);
        }

        private void SharedScaleSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 4);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Scale & Distance Dynamics";

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.01f, 0.10f, 0.005f, 0.035f,
                "Base Text Scale",
                I => I.BaseTextScale,
                "Base font scale of floating text at reference distance (default 0.035)."
            ), 0, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 1.0f, 2.5f, 0.05f, 1.25f,
                "Pop Bounce Scale",
                I => I.PopBounceScale,
                "Scale bounce multiplier when a large hit pops (default 1.25)."
            ), 0, 1);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.1f, 1.5f, 0.05f, 0.5f,
                "Pop Bounce Duration (s)",
                I => I.PopBounceDuration,
                "Duration in seconds of the pop scale animation (default 0.5s)."
            ), 1, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 10f, 200f, 5f, 60f,
                "Reference Distance (m)",
                I => I.ReferenceDistance,
                "Camera distance in meters where scale is considered 1.0x (default 60m)."
            ), 1, 1);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.0f, 1.5f, 0.05f, 0.75f,
                "Distance Scale Exponent",
                I => I.DistanceScaleExponent,
                "Distance scaling curve exponent. 1.0 = linear, 0 = fixed size regardless of distance (default 0.75)."
            ), 2, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.1f, 3.0f, 0.1f, 0.5f,
                "Min Text Scale Clamp",
                I => I.MinTextScale,
                "Minimum clamped 3D text scale for extreme close-ups (default 0.5)."
            ), 2, 1);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 5.0f, 80.0f, 1.0f, 40.0f,
                "Max Text Scale Clamp",
                I => I.MaxTextScale,
                "Maximum clamped 3D text scale for distant targets (default 40.0)."
            ), 3, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 500f, 50000f, 500f, 10000f,
                "Large Hit Threshold",
                I => I.LargeHitThreshold,
                "Damage threshold to trigger pop animation bounce (default 10,000)."
            ), 3, 1);
        }

        private void SharedElevationAndStackingSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 3);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Dynamic Sky Elevation & Stacking";

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.0f, 4.0f, 0.05f, 1.25f,
                "Sky Elevation Factor",
                I => I.DistanceElevationFactor,
                "Lifts numbers into the sky above the target silhouette at range to prevent blocking craft view (default 1.25)."
            ), 0, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.0f, 1.5f, 0.01f, 0.65f,
                "Elevation Curve Exponent",
                I => I.DistanceElevationExponent,
                "Sub-linear power curve for elevation growth with distance (default 0.65)."
            ), 0, 1);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 10f, 300f, 10f, 140f,
                "Max Sky Elevation (m)",
                I => I.MaxDistanceElevation,
                "Absolute ceiling clamp in meters for sky elevation (default 140m)."
            ), 1, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.5f, 3.0f, 0.05f, 1.35f,
                "Stack Spacing Multiplier",
                I => I.StackSpacingMultiplier,
                "Vertical spacing between stacked damage numbers (default 1.35)."
            ), 1, 1);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 1.0f, 50.0f, 1.0f, 15.0f,
                "Stack Proximity Distance (m)",
                I => I.StackProximityDistance,
                "Distance threshold in meters to consider numbers co-located and stack them vertically (default 15m)."
            ), 2, 0);

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Track Construct Movement",
                "Floating numbers follow the target ship as it maneuvers through the world. (default: true)",
                I => I.TrackConstructMovement
            ), 2, 1);
        }

        private void SharedMovementAndLifetimeSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 2);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Lifetime & Animation";

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.5f, 8.0f, 0.25f, 3.0f,
                "Number Lifetime (s)",
                I => I.NumberLifetime,
                "Base duration before damage numbers fade out (default 3.0s)."
            ), 0, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 1.0f, 3.0f, 0.1f, 1.2f,
                "Large Hit Life Multiplier",
                I => I.LargeHitLifetimeMultiplier,
                "Extra lifetime multiplier awarded to hits exceeding the large hit threshold (default 1.2x)."
            ), 0, 1);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.1f, 2.0f, 0.05f, 0.5f,
                "Fade-Out Duration (s)",
                I => I.FadeOutDuration,
                "Duration in seconds of the transparency fade-out (default 0.5s)."
            ), 1, 0);
            table.AddInterpretter(StringDisplay.Quick(""), 1, 1);
        }
        private void SimpleSettingsSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 2);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Simple Mode Settings";

            table.AddInterpretter(StringDisplay.Quick(
                "<color=#E0E0E0>In <b>Simple Mode</b>, damage is consolidated per damage type above each vessel's Center of Mass.\n" +
                "This is really to reduce clutter and improve readability.</color>"), 0, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus,
                0f, 60f, 1f, 10f,
                "COM Height Offset (m)",
                I => I.BaseSpawnOffsetY,
                "Height above the ship Center-of-Mass where floating damage numbers are anchored (default 10m)."
            ), 0, 1);

            table.AddInterpretter(StringDisplay.Quick(""), 1, 1);
        }

        private void DetailedSettingsSection()
        {
            ScreenSegmentTable table = CreateTableSegment(2, 5);
            table.SqueezeTable = false;
            table.SpaceBelow = Spacing;
            table.BackgroundStyleWhereApplicable = ConsoleStyles.Instance.Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            table.NameWhereApplicable = "Detailed Mode Settings";

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0f, 50f, 1f, 5f,
                "Min Damage To Display",
                I => I.MinDamageToDisplay,
                "Minimum damage required to spawn a number (filters out machine gun / fragment noise, default 5)."
            ), 0, 0);

            table.AddInterpretter(UIHelper.IntSlider(
                _focus, 1, 256, 1, 8,
                "Max Numbers Per Ship",
                I => I.MaxNumbersPerShip,
                "Maximum simultaneous damage numbers allowed per individual ship (default 8)."
            ), 0, 1);

            table.AddInterpretter(UIHelper.IntSlider(
                _focus, 10, 1024, 2, 64,
                "Max World Pool Size",
                I => I.MaxWorldPoolSize,
                "Hard cap on total 3D text objects rendered across the battlefield (default 64)."
            ), 1, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 0.1f, 5.0f, 0.1f, 1.0f,
                "Accumulation Window (s)",
                I => I.AccumulationWindowSeconds,
                "Time window in seconds during which successive hits add into an existing number (default 1.0s)."
            ), 2, 0);

            table.AddInterpretter(UIHelper.FloatSlider(
                _focus, 1.0f, 100.0f, 1.0f, 25.0f,
                "Max Accumulation Distance (m)",
                I => I.MaxAccumulationDistance,
                "Maximum distance between hits on the same ship to merge into one number (default 25m)."
            ), 2, 1);

            table.AddInterpretter(UIHelper.Bool(
                _focus,
                "Hover Over Last Hit Position",
                "Anchor merged numbers above the latest hit location rather than the initial hit.",
                I => I.HoverOverLastPositionHit
            ), 3, 0);

            table.AddInterpretter(StringDisplay.Quick(
                "If you really want to see all numbers, disable \"Accumulate Sustained Fire\" and tinker with scale and pool size."),
                4, 0);

            table.AddInterpretter(StringDisplay.Quick(""), 4, 1);
        }
    }

    /// <summary>
    /// Harmony patch adding the Damage Numbers settings tab to the in-game Options Menu (F1 -> Options).
    /// </summary>
    [HarmonyPatch(typeof(OptionsMenuUi), "BuildInterface")]
    public static class DamageNumberSettingsUIPatch
    {
        private static void Postfix(ref ConsoleWindow __result)
        {
            try
            {
                var config = DamageNumberSettingsConfig.Get();
                if (config != null)
                {
                    __result.AllScreens.Add(new DamageNumberSettingsTab(__result, config));
                }
            }
            catch (Exception ex)
            {
                BrilliantSkies.Core.Logger.AdvLogger.LogError($"[FloatingDamageNumbers] Failed to add SettingsTab to OptionsMenu: {ex}", BrilliantSkies.Core.Logger.LogOptions.None);
            }
        }
    }
}

