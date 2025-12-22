using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Unity.Netcode;
using ChatCommandAPI;
using System;

namespace QuickSort
{
    public class Sorter : MonoBehaviour
    {
        public ConfigEntry<float> sortOriginX;
        public ConfigEntry<float> sortOriginY;
        public ConfigEntry<float> sortOriginZ;
        public ConfigEntry<float> itemSpacing;
        public ConfigEntry<float> rowSpacing;
        public ConfigEntry<int> itemsPerRow;
        public ConfigEntry<string> skippedItems;
        public ConfigEntry<float> sortAreaWidth;
        public ConfigEntry<float> sortAreaDepth;
        public ConfigEntry<float> wallPadding;
        public ConfigEntry<bool> stackSameTypeTogether;
        public ConfigEntry<float> sameTypeStackStepY;

        private List<GrabbableObject> scrap;
        public static bool inProgress;

        private Vector3 SortOrigin => new Vector3(sortOriginX.Value, sortOriginY.Value, sortOriginZ.Value);
        private bool CanSort => Ship.InOrbit || Ship.Stationary;

        private void Awake()
        {
            sortOriginX = Plugin.config.Bind<float>("Sorter", "sortOriginX", -2.8f,
                "X coordinate of the origin position for sorting items (relative to ship)");
            sortOriginY = Plugin.config.Bind<float>("Sorter", "sortOriginY", 0.5f,
                "Y coordinate of the origin position for sorting items (relative to ship)");
            sortOriginZ = Plugin.config.Bind<float>("Sorter", "sortOriginZ", -4.8f,
                "Z coordinate of the origin position for sorting items (relative to ship)");
            itemSpacing = Plugin.config.Bind<float>("Sorter", "itemSpacing", 1f,
                "Spacing between items horizontally");
            rowSpacing = Plugin.config.Bind<float>("Sorter", "rowSpacing", 0.8f,
                "Spacing between rows vertically");
            itemsPerRow = Plugin.config.Bind<int>("Sorter", "itemsPerRow", 7,
                "Number of items per row");
            skippedItems = Plugin.config.Bind<string>("Sorter", "skippedItems", "body, clipboard, sticky_note, boombox",
                "Which items should be skipped when organizing");

            // When there are too many ITEM TYPES, we don't push further into the ship (door/walls).
            // Instead we lift the next "type page" up on Y and restart from the first slot.
            sortAreaWidth = Plugin.config.Bind<float>("Sorter", "sortAreaWidth", 9.0f,
                "Reserved: used only for future bounding. (Keeping for compatibility)");
            sortAreaDepth = Plugin.config.Bind<float>("Sorter", "sortAreaDepth", 6.0f,
                "How far forward (Z) types are allowed to expand from sortOriginZ before starting a new type-layer above.");
            wallPadding = Plugin.config.Bind<float>("Sorter", "wallPadding", 0.25f,
                "Padding used for depth calculation (prevents placing type rows into doors/walls).");

            stackSameTypeTogether = Plugin.config.Bind<bool>("Sorter", "stackSameTypeTogether", true,
                "If true, items of the same type will be stacked at the exact same X/Z position (only Y increases), instead of being spread in a small grid.");
            sameTypeStackStepY = Plugin.config.Bind<float>("Sorter", "sameTypeStackStepY", 0.0f,
                "Vertical spacing between items when stackSameTypeTogether is enabled. Set to 0 for exact overlap; increase (e.g. 0.1~0.2) if physics makes items push apart.");

            // Command is registered in Plugin.Awake
        }

        private void Update()
        {
            if (inProgress && ((ButtonControl)Keyboard.current.escapeKey).wasPressedThisFrame)
            {
                inProgress = false;
                Log.Chat("Sorting cancelled", "FF0000");
            }
        }

        private void SortCommandHandler(string[] args)
        {
            if (args.Length > 0 && args[0] == "help")
            {
                Log.Chat("Usage: /sort [-r|-redo] [help]", "FFFF00");
                return;
            }

            if (!CanSort)
            {
                Log.NotifyPlayer("Sorter Error", "Must be in orbit or stationary at company", isWarning: true);
                return;
            }

            if (inProgress)
            {
                Log.NotifyPlayer("Sorter Error", "Operation in progress", isWarning: true);
                return;
            }

            bool force = args.Contains("-r") || args.Contains("-redo");
            CategorizeItems();
            Log.ConfirmSound();
            StartCoroutine(SortItems(force));
        }

        public IEnumerator SortItems(bool force)
        {
            inProgress = true;
            Log.Chat("Press [Escape] to cancel sorting", "FFFF00");

            // LethalShipSort-style: move items by calling vanilla ServerRpcs on the local PlayerControllerB
            // (ThrowObjectServerRpc / SetObjectAsNoLongerHeld). This works even if the host doesn't have this mod.
            if (Player.Local == null)
            {
                Log.NotifyPlayer("Sorter Error", "Local player not ready yet", isWarning: true);
                inProgress = false;
                yield break;
            }

            // Ship root used for positioning and raycasts (matches LethalShipSort)
            GameObject ship = GameObject.Find("Environment/HangarShip");
            if (ship == null)
            {
                Log.NotifyPlayer("Sorter Error", "Ship not found", isWarning: true);
                inProgress = false;
                yield break;
            }

            Vector3 originLocal = SortOrigin; // ship-local origin

            // Group items by name
            Dictionary<string, List<GrabbableObject>> groupedItems = new Dictionary<string, List<GrabbableObject>>();

            foreach (GrabbableObject item in scrap)
            {
                if (ShouldSkip(item))
                    continue;

                string itemName = item.Name();
                if (!groupedItems.ContainsKey(itemName))
                {
                    groupedItems[itemName] = new List<GrabbableObject>();
                }
                groupedItems[itemName].Add(item);
            }

            // Create layout: bounded to a configurable ship-local rectangle to prevent wall clipping.
            // Vector3: x = offsetX, y = typeLayerY, z = offsetZ
            Dictionary<string, Vector3> layout = CreateLayout(groupedItems.Keys.ToList());

            int itemIndex = 0;
            foreach (var group in groupedItems)
            {
                string itemName = group.Key;
                List<GrabbableObject> items = group.Value;

                if (!layout.ContainsKey(itemName))
                    continue;

                Vector3 typePos = layout[itemName];

                // Determine the "lowest" (ground) Y once for this pile, then build layers upward from it.
                // This avoids per-item raycasts hitting other items and causing upward drift.
                const int LAYER_MASK = 268437761; // Copied from GrabbableObject.GetItemFloorPosition via LethalShipSort
                Vector3 pileCenterLocal = originLocal + new Vector3(typePos.x, 0f, typePos.z);
                float groundYLocal = pileCenterLocal.y;
                {
                    Vector3 rayStartCenter = ship.transform.TransformPoint(pileCenterLocal + Vector3.up * 2f);
                    if (Physics.Raycast(rayStartCenter, Vector3.down, out RaycastHit hitCenter, 80f, LAYER_MASK, QueryTriggerInteraction.Ignore))
                    {
                        groundYLocal = ship.transform.InverseTransformPoint(hitCenter.point).y;
                    }
                }

                for (int stackIndex = 0; stackIndex < items.Count; stackIndex++)
                {
                    GrabbableObject item = items[stackIndex];
                    if (ShouldBreak(item))
                    {
                        Log.NotifyPlayer("Sorter Stopping", "Operation cancelled or ship is in motion", isWarning: true);
                        inProgress = false;
                        yield break;
                    }

                    // Same-type stacking:
                    // If enabled, keep all items of the same type at the EXACT same X/Z (no spreading),
                    // and only raise Y slightly per item so they visually "overlap/stack" instead of being scattered.
                    float pileX = 0f;
                    float pileZ = 0f;
                    float pileY;

                    if (stackSameTypeTogether.Value)
                    {
                        pileY = stackIndex * Mathf.Max(0f, sameTypeStackStepY.Value);
                    }
                    else
                    {
                        // Legacy behavior: spread items in a small X/Z grid, then stack in layers.
                        int cols = Mathf.Max(1, itemsPerRow.Value);
                        int rows = cols; // square grid
                        int perLayer = cols * rows;
                        int layer = stackIndex / perLayer;
                        int inLayer = stackIndex % perLayer;
                        int r = inLayer / cols;
                        int c = inLayer % cols;

                        const float pileSpacing = 0.10f;  // X/Z spacing within a pile
                        const float pileLayerHeight = 0.07f; // Y spacing between layers

                        pileX = (c - (cols - 1) / 2f) * pileSpacing;
                        pileZ = (r - (rows - 1) / 2f) * pileSpacing;
                        pileY = layer * pileLayerHeight;
                    }

                    // Final ship-local target:
                    // - X/Z grid around pileCenterLocal
                    // - Y starts from the ground at pile center + item verticalOffset, then layers stack upward
                    Vector3 targetLocal = new Vector3(
                        pileCenterLocal.x + pileX,
                        groundYLocal + (item.itemProperties.verticalOffset - 0.05f) + typePos.y + pileY,
                        pileCenterLocal.z + pileZ
                    );

                    Vector3 worldPos = ship.transform.TransformPoint(targetLocal);

                    if (!force && Vector3.Distance(worldPos, item.transform.position) < 0.25f)
                    {
                        continue;
                    }

                    yield return GrabbableRetry(item);

                    if (!ShouldSkip(item))
                    {
                        // Teleport item into place (server will apply via ServerRpc)
                        item.floorYRot = -1;
                        if (!MoveUtils.MoveItemOnShipLocal(item, targetLocal, item.floorYRot))
                        {
                            Log.Warning($"Failed to move {item.itemProperties?.itemName ?? item.name}");
                        }

                        int retry = 15;
                        while (!Player.CanGrabObject(item) && retry > 0)
                        {
                            yield return new WaitForEndOfFrame();
                            retry--;
                        }
                    }
                }

                itemIndex++;
            }

            Log.Chat("Sorting complete!", "00FF00");
            inProgress = false;
        }

        private Dictionary<string, Vector3> CreateLayout(List<string> itemNames)
        {
            Dictionary<string, Vector3> layout = new Dictionary<string, Vector3>();

            // Sort items by name for consistent ordering
            itemNames = itemNames.OrderBy(name => name).ToList();

            // User request: do NOT advance Z for type layout (prevents pushing into doors/walls).
            // Types spread only on X (left/right). If there are too many types, we go UP on Y and restart from first X.
            int cols = Mathf.Max(1, itemsPerRow.Value);

            for (int i = 0; i < itemNames.Count; i++)
            {
                string itemName = itemNames[i];

                int layer = i / cols;
                int col = i % cols;

                float centerOffset = (cols - 1) * 0.5f;
                float xOffset = (col - centerOffset) * itemSpacing.Value;
                float zOffset = 0f;
                float yOffset = layer * rowSpacing.Value + 0.05f;

                layout[itemName] = new Vector3(xOffset, yOffset, zOffset);
            }

            return layout;
        }

        public void CategorizeItems()
        {
            scrap = new List<GrabbableObject>();

            GrabbableObject[] allItems = UnityEngine.Object.FindObjectsOfType<GrabbableObject>();

            foreach (GrabbableObject item in allItems)
            {
                if (!ShouldSkip(item) && item.isInShipRoom && item.itemProperties.isScrap)
                {
                    scrap.Add(item);
                }
            }

            // Sort by value and name for consistent ordering
            scrap = scrap.OrderBy(item => item.scrapValue)
                        .ThenBy(item => item.Name())
                        .ToList();
        }

        private IEnumerator GrabbableRetry(GrabbableObject item)
        {
            int retry = 15;
            while (!Player.CanGrabObject(item) && retry > 0)
            {
                yield return new WaitForEndOfFrame();
                retry--;
            }
        }

        private bool ShouldBreak(GrabbableObject item)
        {
            return !inProgress || !Ship.Stationary ||
                   (Player.Local != null && (Player.Local.beamOutParticle.isPlaying || Player.Local.beamUpParticle.isPlaying));
        }

        private bool ShouldSkip(GrabbableObject item)
        {
            if (item == null)
            {
                return true;
            }

            // Don't rely on player "can grab right now" state here; this is a filtering function.
            if (!item.grabbable || item.deactivated || item.isHeld || item.isPocketed)
            {
                return true;
            }

            if (item.Name() == "body")
            {
                return true;
            }

            if (!item.isInShipRoom)
            {
                return true;
            }

            string itemName = item.Name();
            string[] skipped = skippedItems.Value.Split(',');
            foreach (string skippedItem in skipped)
            {
                if (itemName.Contains(skippedItem.Trim().ToLower()))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class SortCommand : Command
    {
        public SortCommand()
        {
            // Command is automatically registered when instantiated
            QuickSort.Log.Info("SortCommand constructor called");
        }

        public override string Name => "sort";
        public override string[] Commands => new[] { "sort", Name };
        public override string Description => "Sorts items on the ship. Usage: /sort [-r|-redo] [help]";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            error = null;

            QuickSort.Log.Info($"SortCommand.Invoke called with args: [{string.Join(", ", args)}]");

            if (args.Length > 0 && args[0] == "help")
            {
                ChatCommandAPI.ChatCommandAPI.Print(Description);
                return true;
            }

            if (!Ship.Stationary)
            {
                error = "Must be in orbit or stationary at company";
                QuickSort.Log.Warning($"SortCommand failed: {error}");
                return false;
            }

            if (Sorter.inProgress)
            {
                error = "Operation in progress";
                QuickSort.Log.Warning($"SortCommand failed: {error}");
                return false;
            }

            // Works on non-host too (vanilla ServerRpc calls from local player).
            bool force = args.Contains("-r") || args.Contains("-redo");

            // Get Sorter instance from Plugin (Unity-safe null checks: destroyed objects compare == null,
            // but C# null-conditional (?.) does NOT use Unity's overloaded null semantics)
            Sorter sorter = null;
            if (Plugin.sorterObject != null)
            {
                sorter = Plugin.sorterObject.GetComponent<Sorter>();
            }
            if (sorter == null)
            {
                // Lazy init in case something destroyed our object or the command ran extremely early.
                try
                {
                    if (Plugin.sorterObject == null)
                    {
                        Plugin.sorterObject = new GameObject("PastaSorter");
                        UnityEngine.Object.DontDestroyOnLoad(Plugin.sorterObject);
                        QuickSort.Log.Info("Sorter lazily initialized from SortCommand");
                    }

                    // Ensure component exists
                    sorter = Plugin.sorterObject.GetComponent<Sorter>();
                    if (sorter == null)
                    {
                        sorter = Plugin.sorterObject.AddComponent<Sorter>();
                        QuickSort.Log.Info("Sorter component added from SortCommand");
                    }
                }
                catch (Exception e)
                {
                    error = "Sorter not initialized yet";
                    QuickSort.Log.Error($"SortCommand failed: {error}");
                    QuickSort.Log.Error("Exception: " + e.Message);
                    return false;
                }

                if (sorter == null)
                {
                    error = "Sorter not initialized yet";
                    QuickSort.Log.Warning($"SortCommand failed: {error}");
                    return false;
                }
            }

            QuickSort.Log.Info("SortCommand executing...");
            sorter.CategorizeItems();
            Log.ConfirmSound();
            sorter.StartCoroutine(sorter.SortItems(force));
            QuickSort.Log.Info("SortCommand executed successfully");
            return true;
        }
    }
}

