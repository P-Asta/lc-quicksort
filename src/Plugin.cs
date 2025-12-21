using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using GameNetcodeStuff;

namespace QuickSort
{
    // Per ChatCommandAPI README: make this a hard dependency so load order is guaranteed.
    [BepInDependency("baer1.ChatCommandAPI", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("pasta.quicksort", "QuickSort", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log = null!;
        public static ConfigFile config;
        public static Plugin Instance { get; private set; }
        private static Harmony harmony;
        public static GameObject sorterObject;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            config = Config;

            QuickSort.Log.Init(Logger);
            QuickSort.Log.Info("QuickSort - Item Sorter loading...");

            // Initialize Harmony patches
            harmony = new Harmony("pasta.quicksort");
            harmony.PatchAll(typeof(QuickSort.Ship));
            harmony.PatchAll(typeof(Startup));
            harmony.PatchAll(typeof(GrabPatch));

            // Register command immediately (ChatCommandAPI should be loaded by now)
            try
            {
                new QuickSort.SortCommand();
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