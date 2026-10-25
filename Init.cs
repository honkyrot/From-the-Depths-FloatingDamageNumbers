using System;
using System.Reflection;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.Modding;
using HarmonyLib;

namespace FloatingDamageNumbers
{
    public class Init : GamePlugin, GamePlugin_PostLoad
    {
        public string name => "FloatingDamageNumbers";

        public Version version => new(1, 0, 5);

        public void OnLoad()
        {
            GameEvents.StartEvent.RegWithEvent(OnStart);
            GameEvents.ProfileChange.RegWithEvent(OnProfileChanged);

            try
            {
                new Harmony("FloatingDamageNumbers").PatchAll(Assembly.GetExecutingAssembly());
                AdvLogger.LogEvent("[FloatingDamageNumbers] Harmony patches registered.");
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in OnLoad Harmony: {ex}", LogOptions._AlertDevAndCustomerInGame);
            }

            // OnLoad runs on background thread in FTD. Dispatch GameObject creation to main thread.
            try
            {
                GameEvents.Callbacks.DispatchToMainThread(() =>
                {
                    FloatingDamageNumberRenderer.EnsureInstance();
                });
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error dispatching EnsureInstance: {ex}", LogOptions._AlertDevAndCustomerInGame);
            }
        }

        private void OnProfileChanged()
        {
            try
            {
                DamageNumberSettingsConfig.Get()?.ApplyToSettings();
                GameEvents.Callbacks.DispatchToMainThread(() =>
                {
                    DamageIconCache.Initialize();
                });
            }
            catch { }
        }

        public void OnStart()
        {
            GameEvents.StartEvent.UnregWithEvent(OnStart);
            try
            {
                DamageNumberSettingsConfig.Get()?.ApplyToSettings();
                GameEvents.Callbacks.DispatchToMainThread(() =>
                {
                    DamageIconCache.Initialize();
                });
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error loading settings config: {ex}", LogOptions.None);
            }

            try
            {
                ModInformation.VersionConfirmation();
            }
            catch (Exception ex)
            {
                AdvLogger.LogError($"[FloatingDamageNumbers] Error in VersionConfirmation: {ex}", LogOptions._AlertDevAndCustomerInGame);
            }
        }

        public void OnSave() { }

        public bool AfterAllPluginsLoaded()
        {
            try
            {
                DamageNumberSettingsConfig.Get()?.ApplyToSettings();

                GameEvents.Callbacks.DispatchToMainThread(() =>
                {
                    FloatingDamageNumberRenderer.EnsureInstance();
                    DamageIconCache.Initialize();
                });
            }
            catch { }
            return true;
        }
    }
}

