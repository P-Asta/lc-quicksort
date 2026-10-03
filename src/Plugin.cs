using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using GameNetcodeStuff;
using System.IO;

namespace QuickSort
{
    // Per ChatCommandAPI README: make this a hard dependency so load order is guaranteed.
    [BepInDependency("baer1.ChatCommandAPI", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("pasta.quicksort", "QuickSort", "0.1.23")]
    public class Plugin : BaseUnityPlugin
    {
        private const string CurrentConfigSchemaVersion = "0.1.10";

        public static ManualLogSource Log = null!;
        public static ConfigFile config;
        public static ConfigEntry<string> configVersion = null!;
        public static ConfigEntry<bool> SyncHostProfile = null!;
        public static Plugin Instance { get; private set; }
        private static Harmony harmony;
        public static GameObject sorterObject;
        private static bool applicationIsQuitting;

        private void Awake()
        {
            applicationIsQuitting = false;
            Instance = this;
            Log = Logger;
            config = Config;
            SyncHostProfile = config.Bind("Profiles", "Sync Host Profile", true,
                "Automatically select a temporary copy of the host's QuickSort profile while in their lobby. When disabled, /pu host still selects it manually.");
            SyncHostProfile.SettingChanged -= OnSyncHostProfileSettingChanged;
            SyncHostProfile.SettingChanged += OnSyncHostProfileSettingChanged;

            // Config version (for migrations). If the key does not exist, BepInEx will create it with default.
            // IMPORTANT: Bind() will CREATE the key if missing, so we must detect presence BEFORE binding.
            bool configFileExistedBeforeBind = File.Exists(config.ConfigFilePath);
            bool hadConfigVersionKey = config.TryGetEntry<string>(new ConfigDefinition("General", "configVersion"), out _);
            configVersion = config.Bind<string>("General", "configVersion", CurrentConfigSchemaVersion,
                "Config schema version (used for internal migrations).");
            bool needsSave = false;
            string currentVer = configVersion.Value ?? "";
            if (string.IsNullOrWhiteSpace(currentVer))
            {
                // Treat blank as "unknown legacy" so migrations can run (but do not assume fresh install).
                currentVer = "0.0.0";
                configVersion.Value = currentVer;
                needsSave = true;
            }

            // Migrations: if config is from an older version (or missing), adjust defaults safely.
            // Requested migration: if sortOriginY is 0.5, change to 0.1.
            // NOTE: If config file does not exist, this is a fresh install: do NOT create/overwrite keys here.
            bool shouldRunMigrations = configFileExistedBeforeBind && (!hadConfigVersionKey || IsVersionLessThan(currentVer, CurrentConfigSchemaVersion));

            if (shouldRunMigrations && (!hadConfigVersionKey || IsVersionLessThan(currentVer, "0.1.5")))
            {
                var sortOriginY = config.Bind<float>("Sorter", "sortOriginY", 0.1f,
                    "Y coordinate of the origin position for sorting items (relative to ship)");
                if (Mathf.Abs(sortOriginY.Value - 0.5f) < 0.0001f)
                {
                    sortOriginY.Value = 0.1f;
                    needsSave = true;
                }

            }

            // Preserve user-edited skippedItems across schema upgrades. Older migrations removed
            // shotgun/ammo or reset a list containing only those two entries, which could erase
            // intentional skip selections.

            // After all migrations, record current schema version for existing configs.
            if (shouldRunMigrations && (!string.Equals(configVersion.Value, CurrentConfigSchemaVersion, System.StringComparison.Ordinal)))
            {
                configVersion.Value = CurrentConfigSchemaVersion;
                needsSave = true;
            }

            if (needsSave)
                config.Save();

            QuickSort.Log.Init(Logger);
            QuickSort.Log.Info("QuickSort - Item Sorter loading...");

            // Create shortcuts file (user-editable) early so it's easy to find in BepInEx/config
            SortShortcuts.EnsureFileExists();
            SortPositions.EnsureFileExists();
            CruiserPositions.EnsureFileExists();
            SortProfiles.EnsureFileExists();

            // Initialize Harmony patches
            harmony = new Harmony("pasta.quicksort");
            harmony.PatchAll(typeof(QuickSort.Ship));
            harmony.PatchAll(typeof(Startup));
            harmony.PatchAll(typeof(GrabPatch));
            harmony.PatchAll(typeof(InteractionLockPatch));
            harmony.PatchAll(typeof(CruiserShelfAnchorPatch));

            // Register command immediately (ChatCommandAPI should be loaded by now)
            try
            {
                Startup.RegisterCommandsOnce();
                QuickSort.Log.Info("Sort command registered in Awake");
            }
            catch (System.Exception e)
            {
                QuickSort.Log.Error("Failed to register sort command in Awake: " + e.Message);
                QuickSort.Log.Error("Stack trace: " + e.StackTrace);
            }

            // Ensure Sorter exists even before the local player is created.
            // The command can be invoked from chat very early; this avoids "Sorter not initialized yet".
            try
            {
                if (sorterObject == null)
                {
                    sorterObject = new GameObject("PastaSorter");
                    sorterObject.AddComponent<Sorter>();
                    sorterObject.AddComponent<ProfileNetworkBehaviour>();
                    Object.DontDestroyOnLoad(sorterObject);
                    QuickSort.Log.Info("Sorter initialized in Awake");
                }
                else if (sorterObject.GetComponent<ProfileNetworkBehaviour>() == null)
                    sorterObject.AddComponent<ProfileNetworkBehaviour>();
                SortProfiles.RecoverInterruptedHostSession();
                SortProfiles.EnsureDefaultProfile();
            }
            catch (System.Exception e)
            {
                QuickSort.Log.Error("Failed to initialize Sorter in Awake: " + e.Message);
                QuickSort.Log.Error("Stack trace: " + e.StackTrace);
            }

            QuickSort.Log.Info("QuickSort - Item Sorter loaded!");
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null!;

            // Some BepInEx/community-pack setups hide or otherwise meddle with the manager object.
            // If this component is destroyed unexpectedly during runtime, do not tear down Harmony and
            // self-disable the entire mod. Restrict cleanup to actual application shutdown.
            if (!applicationIsQuitting)
            {
                QuickSort.Log.Warning("Plugin OnDestroy fired before application quit; skipping cleanup to avoid self-unpatching.");
                return;
            }

            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }

            if (sorterObject != null)
            {
                Destroy(sorterObject);
            }
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
            ProfileNetwork.EndSession();
        }

        private static void OnSyncHostProfileSettingChanged(object sender, System.EventArgs args)
        {
            SortProfiles.OnSyncHostProfileChanged();
        }

        private static bool IsVersionLessThan(string a, string b)
        {
            // Very small semver-ish compare for x.y.z where missing parts are treated as 0.
            static int[] Parse(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return new[] { 0, 0, 0 };
                var parts = s.Trim().Split('.');
                int[] v = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    if (i < parts.Length && int.TryParse(parts[i], out int n))
                        v[i] = n;
                    else
                        v[i] = 0;
                }
                return v;
            }

            var va = Parse(a);
            var vb = Parse(b);
            for (int i = 0; i < 3; i++)
            {
                if (va[i] < vb[i]) return true;
                if (va[i] > vb[i]) return false;
            }
            return false;
        }

    }

    public static class Startup
    {
        private static bool commandRegistered = false;

        public static void RegisterCommandsOnce()
        {
            if (commandRegistered)
                return;

            new QuickSort.SortCommand();
            new QuickSort.SortBindCommand();
            new QuickSort.SortSetCommand();
            new QuickSort.SortResetCommand();
            new QuickSort.SortPositionsCommand();
            new QuickSort.SortBindingsListCommand();
            new QuickSort.SortSkipCommand();
            new QuickSort.PileCommand();
            new QuickSort.CruiserSetCommand();
            new QuickSort.CruiserSortCommand();
            new QuickSort.ProfileCommand();
            new QuickSort.ProfileSaveShortcutCommand();
            new QuickSort.ProfileUseShortcutCommand();
            new QuickSort.ProfileListShortcutCommand();
            new QuickSort.ProfileDeleteShortcutCommand();
            commandRegistered = true;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        [HarmonyPostfix]
        private static void OnLocalPlayerCreated(PlayerControllerB __instance)
        {
            if (__instance != StartOfRound.Instance.localPlayerController)
                return;

            // Register command when player is created (ChatCommandAPI should be loaded by then)
            if (!commandRegistered)
            {
                try
                {
                    RegisterCommandsOnce();
                    QuickSort.Log.Info("Sort command registered");
                }
                catch (System.Exception e)
                {
                    QuickSort.Log.Error("Failed to register sort command: " + e.Message);
                }
            }

            if (Plugin.sorterObject == null)
            {
                Plugin.sorterObject = new GameObject("PastaSorter");
                Plugin.sorterObject.AddComponent<Sorter>();
                Object.DontDestroyOnLoad(Plugin.sorterObject);
            }
            else if (Plugin.sorterObject.GetComponent<Sorter>() == null)
                Plugin.sorterObject.AddComponent<Sorter>();
            if (Plugin.sorterObject.GetComponent<ProfileNetworkBehaviour>() == null)
                Plugin.sorterObject.AddComponent<ProfileNetworkBehaviour>();
            SortProfiles.EnsureDefaultProfile();
            ProfileNetwork.OnLocalPlayerCreated();
        }

        [HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Disconnect))]
        [HarmonyPrefix]
        private static void OnLobbyDisconnect()
        {
            ProfileNetwork.EndSession();
        }
    }
}
