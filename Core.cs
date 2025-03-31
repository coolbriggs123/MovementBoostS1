using MelonLoader;
using HarmonyLib;
using UnityEngine;
using System.IO;
using System.Text;
using System;
using System.Collections.Generic;
using Il2CppSteamworks;

[assembly: MelonInfo(typeof(SprintSpeed.Core), "MovementBoost", "1.0.0", "Coolbriggs", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace SprintSpeed
{
    public class SpeedConfig
    {
        public float SpeedMultiplier = 1.5f;
        public bool EnableSpeedBoost = true;
        public bool SyncConfig = true;
        
        // Simple JSON serialization
        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"SpeedMultiplier\": {SpeedMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"EnableSpeedBoost\": {EnableSpeedBoost.ToString().ToLower()},");
            sb.AppendLine($"  \"SyncConfig\": {SyncConfig.ToString().ToLower()}");
            sb.AppendLine("}");
            return sb.ToString();
        }
        
        // Simple JSON deserialization
        public static SpeedConfig FromJson(string json)
        {
            SpeedConfig config = new SpeedConfig();
            
            try
            {
                // Parse float values
                config.SpeedMultiplier = ParseFloatFromJson(json, "SpeedMultiplier", config.SpeedMultiplier);
                
                // Parse boolean values
                config.EnableSpeedBoost = ParseBoolFromJson(json, "EnableSpeedBoost", config.EnableSpeedBoost);
                config.SyncConfig = ParseBoolFromJson(json, "SyncConfig", config.SyncConfig);
            }
            catch (Exception)
            {
                // If parsing fails, return default config
            }
            
            return config;
        }
        
        private static float ParseFloatFromJson(string json, string key, float defaultValue)
        {
            string pattern = $"\"{key}\"\\s*:\\s*([0-9.]+)";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                if (float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out float result))
                {
                    return result;
                }
            }
            return defaultValue;
        }
        
        private static bool ParseBoolFromJson(string json, string key, bool defaultValue)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(true|false)";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                if (bool.TryParse(match.Groups[1].Value, out bool result))
                {
                    return result;
                }
            }
            return defaultValue;
        }
    }

    public class Core : MelonMod
    {
        private static SpeedConfig config = new SpeedConfig();
        private static string configFolderPath;
        private static string configFilePath;
        
        // Network-related fields
        private static CSteamID localSteamID;
        private static bool isHost = false;
        private static bool configSynced = false;
        private static bool isInitialized = false;
        private static bool debugMode = false;
        private const string CONFIG_MESSAGE_PREFIX = "SPEED_CONFIG:";
        private const int CONFIG_SYNC_INTERVAL = 10; // Seconds between config sync attempts
        private static float lastConfigSyncTime = 0f;
        
        // MelonPrefs categories and entries
        private const string CATEGORY_GENERAL = "SpeedBoost";
        private const string SETTING_SPEED_MULTIPLIER = "SpeedMultiplier";
        private const string SETTING_ENABLE_SPEED_BOOST = "EnableSpeedBoost";
        private const string SETTING_SYNC_CONFIG = "SyncConfig";

        public override void OnInitializeMelon()
        {
            // Set up config paths
            configFolderPath = Path.Combine("UserData", "SpeedBoost");
            configFilePath = Path.Combine(configFolderPath, "config.json");
            
            // Register MelonPrefs
            MelonPreferences.CreateCategory(CATEGORY_GENERAL, "Speed Boost Settings");
            MelonPreferences.CreateEntry(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER, config.SpeedMultiplier, "Speed Multiplier");
            MelonPreferences.CreateEntry(CATEGORY_GENERAL, SETTING_ENABLE_SPEED_BOOST, config.EnableSpeedBoost, "Enable Speed Boost");
            MelonPreferences.CreateEntry(CATEGORY_GENERAL, SETTING_SYNC_CONFIG, config.SyncConfig, "Sync Config (Host Only)");
            
            // Create directory if it doesn't exist
            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }

            // Load or create config
            LoadConfig();
            
            // Try to get local player's Steam ID
            try
            {
                localSteamID = SteamUser.GetSteamID();
            }
            catch (System.Exception)
            {
                // Silent catch
            }
            
            // Register for Steam callbacks
            SteamCallbacks.RegisterCallbacks();
            
            LoggerInstance.Msg($"MovementBoost mod initialized with speed multiplier: {config.SpeedMultiplier}");
            LoggerInstance.Msg("Config will be automatically synced in multiplayer if you're the host");
            LoggerInstance.Msg("If you need any help join https://discord.gg/PCawAVnhMH");
            LoggerInstance.Msg("Happy Selling!");
        }

        private void LoadConfig()
        {
            try
            {
                // If config file exists, load it
                if (File.Exists(configFilePath))
                {
                    string json = File.ReadAllText(configFilePath);
                    config = SpeedConfig.FromJson(json);
                    
                    // Update MelonPrefs to match loaded config
                    MelonPreferences.SetEntryValue(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER, config.SpeedMultiplier);
                    MelonPreferences.SetEntryValue(CATEGORY_GENERAL, SETTING_ENABLE_SPEED_BOOST, config.EnableSpeedBoost);
                    MelonPreferences.SetEntryValue(CATEGORY_GENERAL, SETTING_SYNC_CONFIG, config.SyncConfig);
                    
                    LoggerInstance.Msg("Config loaded from file");
                }
                else
                {
                    // If no config file exists, create one with default values
                    config = new SpeedConfig();
                    SaveConfig();
                    LoggerInstance.Msg("Created new config file with default values");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error loading config: {ex.Message}");
                // Use default config if loading fails
                config = new SpeedConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                // Update config from MelonPrefs
                config.SpeedMultiplier = MelonPreferences.GetEntryValue<float>(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER);
                config.EnableSpeedBoost = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_ENABLE_SPEED_BOOST);
                config.SyncConfig = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_SYNC_CONFIG);

                // Create directory if it doesn't exist
                if (!Directory.Exists(configFolderPath))
                {
                    Directory.CreateDirectory(configFolderPath);
                }

                // Save config to file using our custom JSON serializer
                string json = config.ToJson();
                File.WriteAllText(configFilePath, json);
                
                // Also save MelonPrefs for compatibility
                MelonPreferences.Save();
                
                LoggerInstance.Msg("Config saved to file");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error saving config: {ex.Message}");
            }
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
            yield return new WaitForSeconds(1.0f); // Wait for scene to fully load
            if (GameObject.Find("Player") != null) // Only proceed if we're in a valid scene with players
            {
                DetermineIfHost();
                isInitialized = true;
            }
        }
        
        public override void OnUpdate()
        {
            if (!isInitialized || !isHost) return;

            // Only sync config in Main scene
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main") return;
            
            // If we're the host and config syncing is enabled, periodically sync config
            if (isHost && config.SyncConfig && !configSynced && Time.time - lastConfigSyncTime > CONFIG_SYNC_INTERVAL)
            {
                SyncConfigToClients();
                lastConfigSyncTime = Time.time;
            }
        }
        
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName != "Main")
            {
                isInitialized = false;
                configSynced = false;
            }
        }
        
        private void DetermineIfHost()
        {
            try
            {
                // Try to determine if we're the host
                // Method 1: Check if we're in a lobby and are the owner
                if (SteamMatchmaking.GetLobbyOwner(SteamMatchmaking.GetLobbyByIndex(0)).m_SteamID == localSteamID.m_SteamID)
                {
                    isHost = true;
                    LoggerInstance.Msg("You are the host - config sync enabled");
                    return;
                }
                
                // Method 2: Check if we're the first player or have a specific name
                GameObject playerObj = GameObject.Find("Player");
                if (playerObj != null)
                {
                    string name = playerObj.name.ToLower();
                    if (name.Contains("host") || name.Contains("server") || name.Contains("owner"))
                    {
                        isHost = true;
                        LoggerInstance.Msg("You are the host - config sync enabled");
                        return;
                    }
                }
                
                isHost = false;
                LoggerInstance.Msg("You are a client - waiting for host config");
            }
            catch (System.Exception)
            {
                // If we can't determine, assume we're not the host
                isHost = false;
            }
        }
        
        // Config synchronization methods
        private void SyncConfigToClients()
        {
            if (!isHost || !config.SyncConfig) return;
            
            try
            {
                // Create a string representation of the config
                string configData = $"{CONFIG_MESSAGE_PREFIX}{config.SpeedMultiplier}|{config.EnableSpeedBoost}";
                
                // Send to all players in the lobby
                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(0);
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                
                for (int i = 0; i < memberCount; i++)
                {
                    CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                    if (memberId.m_SteamID != localSteamID.m_SteamID) // Don't send to self
                    {
                        SteamNetworking.SendP2PPacket(memberId, Encoding.UTF8.GetBytes(configData), (uint)configData.Length, EP2PSend.k_EP2PSendReliable);
                    }
                }
                
                configSynced = true;
                LoggerInstance.Msg("Config synced to all clients");
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"Failed to sync config: {ex.Message}");
            }
        }
        
        // Steam callbacks for networking
        private static class SteamCallbacks
        {
            private static Callback<P2PSessionRequest_t> p2pSessionRequestCallback;
            private static Callback<P2PSessionConnectFail_t> p2pSessionConnectFailCallback;
            
            public static void RegisterCallbacks()
            {
                // Use the static Create method with an Action delegate
                p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(new Action<P2PSessionRequest_t>(OnP2PSessionRequest));
                p2pSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(new Action<P2PSessionConnectFail_t>(OnP2PSessionConnectFail));
                
                // Start polling for messages
                MelonCoroutines.Start(PollForMessages());
            }
            
            private static void OnP2PSessionRequest(P2PSessionRequest_t param)
            {
                // Accept all session requests
                SteamNetworking.AcceptP2PSessionWithUser(param.m_steamIDRemote);
            }
            
            private static void OnP2PSessionConnectFail(P2PSessionConnectFail_t param)
            {
                if (debugMode)
                {
                    MelonLogger.Warning($"P2P connection failed: {param.m_eP2PSessionError}");
                }
            }
            
            private static System.Collections.IEnumerator PollForMessages()
            {
                while (true)
                {
                    yield return new WaitForSeconds(0.5f);
                    
                    uint msgSize;
                    while (SteamNetworking.IsP2PPacketAvailable(out msgSize))
                    {
                        byte[] data = new byte[msgSize];
                        CSteamID senderId;
                        
                        if (SteamNetworking.ReadP2PPacket(data, msgSize, out msgSize, out senderId))
                        {
                            string message = Encoding.UTF8.GetString(data);
                            
                            // Check if it's a config message
                            if (message.StartsWith(CONFIG_MESSAGE_PREFIX))
                            {
                                MelonLogger.Msg($"Received config from {senderId.m_SteamID}");
                                // Use the static method to process the message
                                ProcessReceivedConfigMessage(message);
                            }
                        }
                    }
                }
            }
            
            // Static method to process config messages
            private static void ProcessReceivedConfigMessage(string message)
            {
                if (!message.StartsWith(CONFIG_MESSAGE_PREFIX)) return;
                
                try
                {
                    string configData = message.Substring(CONFIG_MESSAGE_PREFIX.Length);
                    string[] parts = configData.Split('|');
                    
                    if (parts.Length >= 2)
                    {
                        config.SpeedMultiplier = float.Parse(parts[0]);
                        config.EnableSpeedBoost = bool.Parse(parts[1]);
                        
                        // Update the UI preferences to match received config
                        MelonPreferences.SetEntryValue(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER, config.SpeedMultiplier);
                        MelonPreferences.SetEntryValue(CATEGORY_GENERAL, SETTING_ENABLE_SPEED_BOOST, config.EnableSpeedBoost);
                        
                        // Save the received config to file
                        string json = config.ToJson();
                        File.WriteAllText(configFilePath, json);
                        
                        MelonLogger.Msg("Received and applied config from host");
                        configSynced = true;
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Failed to process config message: {ex.Message}");
                }
            }
        }

        // Handle config changes from the MelonPrefs menu
        public override void OnPreferencesSaved()
        {
            bool configChanged = false;
            
            float newSpeedMultiplier = MelonPreferences.GetEntryValue<float>(CATEGORY_GENERAL, SETTING_SPEED_MULTIPLIER);
            if (newSpeedMultiplier != config.SpeedMultiplier)
            {
                config.SpeedMultiplier = newSpeedMultiplier;
                configChanged = true;
            }
            
            bool newEnableSpeedBoost = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_ENABLE_SPEED_BOOST);
            if (newEnableSpeedBoost != config.EnableSpeedBoost)
            {
                config.EnableSpeedBoost = newEnableSpeedBoost;
                configChanged = true;
            }
            
            bool newSyncConfig = MelonPreferences.GetEntryValue<bool>(CATEGORY_GENERAL, SETTING_SYNC_CONFIG);
            if (newSyncConfig != config.SyncConfig)
            {
                config.SyncConfig = newSyncConfig;
                configChanged = true;
            }
            
            // If config changed, save to file and sync if host
            if (configChanged)
            {
                SaveConfig();
                
                if (isHost && config.SyncConfig)
                {
                    configSynced = false;
                    lastConfigSyncTime = 0f; // Force immediate sync
                    LoggerInstance.Msg("Config changed - will sync to clients");
                }
            }
        }
        
        // Handle game quit
        public override void OnApplicationQuit()
        {
            // Save any pending config changes
            SaveConfig();
        }

        [HarmonyPatch(typeof(CharacterController), "Move")]
        private static class MovementPatch
        {
            private static void Prefix(CharacterController __instance, ref Vector3 motion)
            {
                if (__instance == null || !config.EnableSpeedBoost) return;

                // Multiply the movement vector by our configurable speed multiplier
                motion *= config.SpeedMultiplier;
            }
        }
    }
}
