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

        public Version version => new Version(0, 0, 1);

        public void OnLoad()
        {
            GameEvents.StartEvent.RegWithEvent(OnStart);

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

        public void OnStart()
        {
            GameEvents.StartEvent.UnregWithEvent(OnStart);
            try
            {
                DamageNumberSettingsConfig.Get()?.ApplyToSettings();
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
                GameEvents.Callbacks.DispatchToMainThread(() =>
                {
                    FloatingDamageNumberRenderer.EnsureInstance();
                });
            }
            catch { }
            return true;
        }
    }
}