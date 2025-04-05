using System;
using System.Text;
using HarmonyLib;
using Il2CppSteamworks;
using MelonLoader;
using MelonLoader.Preferences;
using UnityEngine;

[assembly: MelonInfo(typeof(SprintSpeed.Core), "MovementBoost", "1.2.0", "Coolbriggs", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace SprintSpeed
{
    public class Core : MelonMod
    {
        private const string CATEGORY_GENERAL = "MovementBoost";
        private const string SETTING_SPEED_MULTIPLIER = "SpeedMultiplier";
        private const string SETTING_ENABLE_BOOST = "EnableSpeedBoost";
        private const string SETTING_SYNC_CONFIG = "SyncConfig";

        // Network-related fields
        private static CSteamID localSteamID;
        private static bool isHost = false;
        private static bool configSynced = false;
        private static bool isInitialized = false;
        private const string CONFIG_MESSAGE_PREFIX = "SPEED_CONFIG:";
        private const int CONFIG_SYNC_INTERVAL = 10;
        private static float lastConfigSyncTime = 0f;

        // Gameplay values
        private static float hostSpeedMultiplier = 1.5f;
        private static float clientSpeedMultiplier = 1.5f;
        private static bool hostSpeedBoost = true;
        private static bool clientSpeedBoost = true;

        public override void OnInitializeMelon()
        {
            // Register MelonPrefs with descriptions
            var category = MelonPreferences.CreateCategory(CATEGORY_GENERAL, "Movement Boost Settings");
            
            // Create speed multiplier entry with min/max values
            category.CreateEntry(
                SETTING_SPEED_MULTIPLIER, 
                1.5f, 
                "Speed Multiplier",
                "How much to multiply movement speed (0.1 = 10% speed, 5.0 = 500% speed)",
                validator: new ValueRange<float>(0.1f, 5.0f)
            );
            
            category.CreateEntry(
                SETTING_ENABLE_BOOST, 
                true, 
                "Enable Speed Boost",
                "Toggle speed boost on/off"
            );
            
            category.CreateEntry(
                SETTING_SYNC_CONFIG, 
                true, 
                "Sync Config (Host Only)",
                "When enabled, host's settings will be synced to all clients"
            );

            // Initialize gameplay values
            hostSpeedMultiplier = MelonPreferences.GetEntryValue<float>(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER);
            hostSpeedBoost = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_ENABLE_BOOST);
            clientSpeedMultiplier = hostSpeedMultiplier;
            clientSpeedBoost = hostSpeedBoost;

            LoggerInstance.Msg($"MovementBoost initialized with multiplier: {hostSpeedMultiplier}");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName != "Main") return;

            isInitialized = false;
            configSynced = false;
            MelonCoroutines.Start(DelayedInit());
        }

        private System.Collections.IEnumerator DelayedInit()
        {
            yield return new WaitForSeconds(1.0f);
            if (GameObject.Find("Player") != null)
            {
                DetermineIfHost();
                isInitialized = true;
                LoggerInstance.Msg($"MovementBoost mod initialized with speed multiplier: {(isHost ? hostSpeedMultiplier : clientSpeedMultiplier)}");
                
                bool syncEnabled = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_SYNC_CONFIG);
                if (isHost && syncEnabled)
                {
                    LoggerInstance.Msg("Config will be automatically synced in multiplayer");
                }
                
                LoggerInstance.Msg("If you need any help join https://discord.gg/PCawAVnhMH");
                LoggerInstance.Msg("Happy Selling!");
            }
        }

        public override void OnPreferencesSaved()
        {
            float newSpeedMultiplier = Mathf.Clamp(
                MelonPreferences.GetEntryValue<float>(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER),
                0.1f, // min value
                5.0f  // max value
            );
            bool newEnableBoost = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_ENABLE_BOOST);
            bool newSyncConfig = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_SYNC_CONFIG);

            bool configChanged = false;

            if (Mathf.Abs(newSpeedMultiplier - hostSpeedMultiplier) > 0.01f)
            {
                hostSpeedMultiplier = newSpeedMultiplier;
                configChanged = true;
            }

            if (newEnableBoost != hostSpeedBoost)
            {
                hostSpeedBoost = newEnableBoost;
                configChanged = true;
            }

            if (configChanged && isHost)
            {
                configSynced = false;
                lastConfigSyncTime = 0f;
                LoggerInstance.Msg($"Config changed - Speed Multiplier: {hostSpeedMultiplier:F2}, Boost Enabled: {hostSpeedBoost}");
            }
        }

        public override void OnUpdate()
        {
            if (!isInitialized || !isHost) return;

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main") return;

            bool syncEnabled = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_SYNC_CONFIG);
            if (isHost && syncEnabled && !configSynced && Time.time - lastConfigSyncTime > CONFIG_SYNC_INTERVAL)
            {
                SyncConfigToClients();
                lastConfigSyncTime = Time.time;
            }
        }

        private void SyncConfigToClients()
        {
            if (!isHost) return;

            try
            {
                string configData = $"{CONFIG_MESSAGE_PREFIX}{hostSpeedMultiplier}|{hostSpeedBoost}";

                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(0);
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);

                for (int i = 0; i < memberCount; i++)
                {
                    CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                    if (memberId.m_SteamID != localSteamID.m_SteamID)
                    {
                        SteamNetworking.SendP2PPacket(memberId, Encoding.UTF8.GetBytes(configData), (uint)configData.Length, EP2PSend.k_EP2PSendReliable);
                    }
                }

                configSynced = true;
                LoggerInstance.Msg($"Speed values synced to all clients - Multiplier: {hostSpeedMultiplier:F2}, Boost Enabled: {hostSpeedBoost}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to sync speed values: {ex.Message}");
            }
        }

        public override void OnApplicationQuit()
        {
            if (isInitialized)
            {
                var playerObj = GameObject.Find("Player");
                if (playerObj != null)
                {
                    var controller = playerObj.GetComponent<CharacterController>();
                    if (controller != null)
                    {
                        var motion = Vector3.zero;
                        controller.Move(motion);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CharacterController), "Move")]
        private static class MovementPatch
        {
            private static void Prefix(CharacterController __instance, ref Vector3 motion)
            {
                if (__instance == null || !(isHost ? hostSpeedBoost : clientSpeedBoost)) return;

                motion *= isHost ? hostSpeedMultiplier : clientSpeedMultiplier;
            }
        }

        private void DetermineIfHost()
        {
            try
            {
                localSteamID = SteamUser.GetSteamID();
                CSteamID lobbyOwner = SteamMatchmaking.GetLobbyOwner(SteamMatchmaking.GetLobbyByIndex(0));
                isHost = lobbyOwner.m_SteamID == localSteamID.m_SteamID;
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to determine host status: {ex.Message}");
                isHost = false;
            }
        }
    }
}
