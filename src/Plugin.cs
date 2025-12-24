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
    [BepInPlugin("pasta.quicksort", "QuickSort", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string CurrentConfigSchemaVersion = "0.1.7";

        public static ManualLogSource Log = null!;
        public static ConfigFile config;
        public static ConfigEntry<string> configVersion = null!;
        public static Plugin Instance { get; private set; }
        private static Harmony harmony;
        public static GameObject sorterObject;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            config = Config;

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

                // Requested migration: add "shotgun" and "ammo" to skippedItems (if missing).
                var skippedItems = config.Bind<string>("Sorter", "skippedItems", Sorter.DefaultSkippedItems,
                    "Global skip list (comma-separated, substring match). Applies to all grabbable items.");
                string migratedSkip = AddTokensToCommaList(skippedItems.Value, "shotgun", "ammo");
                if (!string.Equals(migratedSkip, skippedItems.Value, System.StringComparison.Ordinal))
                {
                    skippedItems.Value = migratedSkip;
                    needsSave = true;
                }
            }

            // 0.1.7 migration:
            // Bugfix: some users ended up with skippedItems == "shotgun, ammo" only (typically created on first run).
            // If that exact case is detected, reset to the real default list.
            if (shouldRunMigrations && (!hadConfigVersionKey || IsVersionLessThan(currentVer, "0.1.7")))
            {
                if (config.TryGetEntry<string>(new ConfigDefinition("Sorter", "skippedItems"), out var existingSkipped))
                {
                    if (IsOnlyShotgunAmmo(existingSkipped.Value))
                    {
                        existingSkipped.Value = Sorter.DefaultSkippedItems;
                        needsSave = true;
                    }
                }
            }

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

            // Initialize Harmony patches
            harmony = new Harmony("pasta.quicksort");
            harmony.PatchAll(typeof(QuickSort.Ship));
            harmony.PatchAll(typeof(Startup));
            harmony.PatchAll(typeof(GrabPatch));

            // Register command immediately (ChatCommandAPI should be loaded by now)
            try
            {
                new QuickSort.SortCommand();
                new QuickSort.SortBindCommand();
                new QuickSort.SortSetCommand();
                new QuickSort.SortResetCommand();
                new QuickSort.SortPositionsCommand();
                new QuickSort.SortBindingsListCommand();
                new QuickSort.SortSkipCommand();
                new QuickSort.PileCommand();
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
                    Object.DontDestroyOnLoad(sorterObject);
                    QuickSort.Log.Info("Sorter initialized in Awake");
                }
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
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }

            if (sorterObject != null)
            {
                Destroy(sorterObject);
            }
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

        private static string AddTokensToCommaList(string? list, params string[] tokensToAdd)
        {
            // Normalize tokens to item-key style (underscores) and de-dupe.
            var tokens = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>();

            void Add(string raw)
            {
                string t = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(t)) return;
                t = QuickSort.Extensions.NormalizeName(t).Trim('_');
                if (string.IsNullOrWhiteSpace(t)) return;
                if (seen.Add(t)) tokens.Add(t);
            }

            if (!string.IsNullOrWhiteSpace(list))
            {
                foreach (var part in list.Split(','))
                    Add(part);
            }

            if (tokensToAdd != null)
            {
                foreach (var raw in tokensToAdd)
                    Add(raw);
            }

            return string.Join(", ", tokens);
        }

        private static bool IsOnlyShotgunAmmo(string? list)
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            void Add(string raw)
            {
                string t = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(t)) return;
                t = QuickSort.Extensions.NormalizeName(t).Trim('_');
                if (string.IsNullOrWhiteSpace(t)) return;
                seen.Add(t);
            }

            if (!string.IsNullOrWhiteSpace(list))
            {
                foreach (var part in list.Split(','))
                    Add(part);
            }

            return seen.Count == 2 && seen.Contains("shotgun") && seen.Contains("ammo");
        }
    }

    public static class Startup
    {
        private static bool commandRegistered = false;

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
                    new QuickSort.SortCommand();
                    new QuickSort.SortBindCommand();
                    new QuickSort.SortSetCommand();
                    new QuickSort.SortResetCommand();
                    new QuickSort.SortPositionsCommand();
                    new QuickSort.SortBindingsListCommand();
                    new QuickSort.SortSkipCommand();
                    new QuickSort.PileCommand();
                    commandRegistered = true;
                    QuickSort.Log.Info("Sort command registered");
                }
                catch (System.Exception e)
                {
                    QuickSort.Log.Error("Failed to register sort command: " + e.Message);
                }
            }

            if (Plugin.sorterObject != null)
            {
                Object.Destroy(Plugin.sorterObject);
            }

            Plugin.sorterObject = new GameObject("PastaSorter");
            Plugin.sorterObject.AddComponent<Sorter>();
            Object.DontDestroyOnLoad(Plugin.sorterObject);
        }
    }
}