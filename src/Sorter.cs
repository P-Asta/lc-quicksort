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
using System.Reflection;

namespace QuickSort
{
    public class Sorter : MonoBehaviour
    {
        // Keep this in one place so config defaults and migrations stay consistent.
        public const string DefaultSkippedItems =
            "body, clipboard, sticky_note, boombox, shovel, jetpack, flashlight, pro_flashlight, key, stun_grenade, lockpicker, mapper, extension_ladder, tzp_inhalant, walkie_talkie, zap_gun, kitchen_knife, weed_killer, radar_booster, spray_paint, belt_bag, shotgun, ammo";

        // Some specific bulky props behave better when placed slightly lower than the default computed floor+offset.
        // NOTE: keys are normalized (underscores) to match item.Name().
        private static readonly HashSet<string> LowerYOffsetTypes = new HashSet<string>
        {
            "toilet_paper",
            "chemical_jug",
            "cash_register",
            "fancy_lamp",
            "large_axle",
            "v_type_engine",
        };

        private static bool ShouldLowerYOffset(GrabbableObject item)
        {
            if (item == null) return false;
            string key = item.Name();
            if (string.IsNullOrWhiteSpace(key)) return false;
            return LowerYOffsetTypes.Contains(key);
        }

        // Default input aliases (no config): normalize common alternate internal names to the canonical type key.
        private static string ApplyDefaultInputAliases(string normalizedKey)
        {
            if (string.IsNullOrWhiteSpace(normalizedKey)) return normalizedKey;

            // User request:
            // - double_barrel -> shotgun
            // - shotgun_shell -> ammo
            return normalizedKey switch
            {
                "double_barrel" => "shotgun",
                "shotgun_shell" => "ammo",
                _ => normalizedKey
            };
        }
        public ConfigEntry<float> sortOriginX;
        public ConfigEntry<float> sortOriginY;
        public ConfigEntry<float> sortOriginZ;
        public ConfigEntry<float> itemSpacing;
        public ConfigEntry<float> rowSpacing;
        public ConfigEntry<int> itemsPerRow;
        public ConfigEntry<string> skippedItems; // legacy name: now used for SCRAP skip list
        public ConfigEntry<float> sortAreaWidth;
        public ConfigEntry<float> sortAreaDepth;
        public ConfigEntry<float> wallPadding;
        public ConfigEntry<bool> stackSameTypeTogether;
        public ConfigEntry<float> sameTypeStackStepY;

        private List<GrabbableObject> scrap;
        public static bool inProgress;

        // Weight snapshot + interaction lock during sorting/moving.
        private static bool _interactionLocked;
        private static int _interactionBypassCount;
        private static List<(string name, bool isProperty, float value)>? _savedWeight;

        private Vector3 SortOrigin => new Vector3(sortOriginX.Value, sortOriginY.Value, sortOriginZ.Value);
        // User request: allow sort commands even during takeoff/landing.
        // We rely on "inside ship" + ship existence checks instead of ship state flags.
        private bool CanSort => true;

        internal static bool EnsureLocalPlayerInShip(out string? error)
        {
            error = null;

            var player = Player.Local;
            if (player == null)
            {
                error = "Local player not ready yet";
                return false;
            }

            // Prefer a real game flag if present (varies by LC version / publicizer output).
            try
            {
                var t = player.GetType();

                static bool? TryGetBool(object obj, Type type, string name)
                {
                    const BindingFlags FLAGS2 = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var f = type.GetField(name, FLAGS2);
                    if (f != null && f.FieldType == typeof(bool))
                        return (bool)f.GetValue(obj);

                    var p = type.GetProperty(name, FLAGS2);
                    if (p != null && p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                        return (bool)p.GetValue(obj, null);

                    return null;
                }

                // Common names across versions/mod loaders (best-effort).
                string[] candidates =
                {
                    "isInHangarShipRoom",
                    "IsInHangarShipRoom",
                    "isInShipRoom",
                    "IsInShipRoom",
                    "inShipRoom",
                    "InShipRoom",
                };

                foreach (var c in candidates)
                {
                    bool? v = TryGetBool(player, t, c);
                    if (v.HasValue)
                    {
                        if (!v.Value)
                        {
                            error = "You must be inside the ship to use this command.";
                            return false;
                        }
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and fall back to positional check.
            }

            // Fallback: approximate check using ship-local bounds.
            GameObject ship = GameObject.Find("Environment/HangarShip");
            if (ship == null)
            {
                error = "Ship not found";
                return false;
            }

            Vector3 local = ship.transform.InverseTransformPoint(player.transform.position);
            // Conservative bounds around the hangar ship interior.
            bool inside =
                local.x >= -10f && local.x <= 10f &&
                local.z >= -20f && local.z <= 10f &&
                local.y >= -6f && local.y <= 10f;

            if (!inside)
            {
                error = "You must be inside the ship to use this command.";
                return false;
            }

            return true;
        }

        private void Awake()
        {
            sortOriginX = Plugin.config.Bind<float>("Sorter", "sortOriginX", -2.8f,
                "X coordinate of the origin position for sorting items (relative to ship)");
            sortOriginY = Plugin.config.Bind<float>("Sorter", "sortOriginY", 0.1f,
                "Y coordinate of the origin position for sorting items (relative to ship)");
            sortOriginZ = Plugin.config.Bind<float>("Sorter", "sortOriginZ", -4.8f,
                "Z coordinate of the origin position for sorting items (relative to ship)");
            itemSpacing = Plugin.config.Bind<float>("Sorter", "itemSpacing", 0.8f,
                "Spacing between items horizontally");
            rowSpacing = Plugin.config.Bind<float>("Sorter", "rowSpacing", 0.8f,
                "Spacing between rows vertically");
            itemsPerRow = Plugin.config.Bind<int>("Sorter", "itemsPerRow", 9,
                "Number of items per row");
            // NOTE: Global skip list used by full sort (/sort) and the general scan.
            // Tokens are stored as canonical item keys (underscores), and some legacy/internal names
            // are normalized (e.g. double_barrel -> shotgun, shotgun_shell -> ammo).
            skippedItems = Plugin.config.Bind<string>("Sorter", "skippedItems", DefaultSkippedItems,
                "Global skip list (comma-separated, substring match). Applies to all grabbable items.");

            // Legacy config migration / normalization:
            // - Old versions had a typo: "rader_booster" (should be "radar_booster")
            // - Normalize tokens so spaces/hyphens work consistently in config
            skippedItems.Value = NormalizeSkipListConfig(skippedItems.Value);

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

        internal static bool IsInteractionLocked => _interactionLocked;
        internal static bool IsInteractionBypassActive => _interactionBypassCount > 0;

        internal static IDisposable BeginInteractionBypass()
        {
            _interactionBypassCount++;
            return new DisposeAction(() =>
            {
                _interactionBypassCount = Math.Max(0, _interactionBypassCount - 1);
            });
        }

        private sealed class DisposeAction : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;
            public DisposeAction(Action onDispose) => _onDispose = onDispose;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose?.Invoke();
            }
        }

        private static void CapturePlayerWeightIfNeeded()
        {
            if (_savedWeight != null) return;
            var player = Player.Local;
            if (player == null) return;

            var t = player.GetType();
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var saved = new List<(string name, bool isProperty, float value)>();

            bool IsCarryWeightName(string n) =>
                n != null &&
                n.IndexOf("carry", StringComparison.OrdinalIgnoreCase) >= 0 &&
                n.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0;

            try
            {
                foreach (var f in t.GetFields(FLAGS))
                {
                    if (f.FieldType != typeof(float)) continue;
                    if (!IsCarryWeightName(f.Name)) continue;
                    saved.Add((f.Name, isProperty: false, (float)f.GetValue(player)));
                }
            }
            catch { }

            try
            {
                foreach (var p in t.GetProperties(FLAGS))
                {
                    if (p.PropertyType != typeof(float)) continue;
                    if (p.GetIndexParameters().Length != 0) continue;
                    if (!p.CanRead || !p.CanWrite) continue;
                    if (!IsCarryWeightName(p.Name)) continue;
                    saved.Add((p.Name, isProperty: true, (float)p.GetValue(player, null)));
                }
            }
            catch { }

            if (saved.Count > 0)
                _savedWeight = saved;
        }

        private static void RestorePlayerWeightIfNeeded()
        {
            if (_savedWeight == null) return;
            var player = Player.Local;
            if (player == null)
            {
                _savedWeight = null;
                return;
            }

            var t = player.GetType();
            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var s in _savedWeight)
            {
                try
                {
                    if (!s.isProperty)
                    {
                        var f = t.GetField(s.name, FLAGS);
                        if (f != null && f.FieldType == typeof(float))
                            f.SetValue(player, s.value);
                    }
                    else
                    {
                        var p = t.GetProperty(s.name, FLAGS);
                        if (p != null && p.PropertyType == typeof(float) && p.GetIndexParameters().Length == 0 && p.CanWrite)
                            p.SetValue(player, s.value, null);
                    }
                }
                catch { }
            }
            _savedWeight = null;
        }

        private static void BeginOperationLock()
        {
            if (_interactionLocked) return;
            CapturePlayerWeightIfNeeded();
            _interactionLocked = true;
        }

        private static void EndOperationLock()
        {
            _interactionLocked = false;
            _interactionBypassCount = 0;
            RestorePlayerWeightIfNeeded();
        }

        private IEnumerator DropHeldItemIfAny(GameObject ship)
        {
            var player = Player.Local;
            if (player == null) yield break;
            var held = player.currentlyHeldObjectServer as GrabbableObject;
            if (held == null) yield break;

            var shipNetObj = ship != null ? ship.GetComponent<NetworkObject>() : null;
            // Drop near the player, but snapped to ship floor to avoid "floating in air" drops.
            Vector3 dropLocal = ship.transform.InverseTransformPoint(player.transform.position + player.transform.forward * 0.75f);
            if (TryGetGroundYLocalAt(shipLocalXZ: dropLocal, out float groundYLocal, out _))
            {
                float vOff = 0f;
                try
                {
                    if (held.itemProperties != null)
                        vOff = held.itemProperties.verticalOffset - 0.05f;
                }
                catch { }
                dropLocal.y = groundYLocal + vOff;
            }

            using (BeginInteractionBypass())
            {
                held.floorYRot = -1;
                player.DiscardHeldObject(true, shipNetObj, dropLocal, false);
            }

            // Wait briefly for the held slot to clear before continuing.
            const int maxFrames = 30;
            int frames = 0;
            while (frames < maxFrames && player.currentlyHeldObjectServer != null)
            {
                frames++;
                yield return null;
            }
        }

        private void Update()
        {
            if (inProgress && ((ButtonControl)Keyboard.current.escapeKey).wasPressedThisFrame)
            {
                inProgress = false;
                Log.Chat("Sorting cancelled", "FF0000");
            }

            // If sorting ended (complete or cancelled), restore weight + re-enable interactions.
            if (_interactionLocked && !inProgress)
            {
                EndOperationLock();
            }
        }

        private void SortCommandHandler(string[] args)
        {
            if (args.Length > 0 && args[0] == "help")
            {
                Log.Chat("Usage: /sort [help]", "FFFF00");
                return;
            }

            if (inProgress)
            {
                Log.NotifyPlayer("Sorter Error", "Operation in progress", isWarning: true);
                return;
            }

            CategorizeItems();
            Log.ConfirmSound();
            StartCoroutine(SortItems(force: false, ignoreSkippedItems: false));
        }

        public IEnumerator SortItems(bool force, bool ignoreSkippedItems = false, bool includeSavedPositionTypesEvenIfSkipped = false)
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

            // User request: /sort should not force-drop held item.
            BeginOperationLock();

            Vector3 originLocal = SortOrigin; // ship-local origin

            // Load saved custom positions ONCE (used for layout + optional skip override with -b)
            HashSet<string>? savedTypes = null;
            var savedPositions = SortPositions.ListAll(out var posListError);
            if (posListError != null)
            {
                Log.Warning(posListError);
            }
            else
            {
                savedTypes = new HashSet<string>(savedPositions.Select(p => p.itemKey));
            }

            // Group items by name
            Dictionary<string, List<GrabbableObject>> groupedItems = new Dictionary<string, List<GrabbableObject>>();

            foreach (GrabbableObject item in scrap)
            {
                string itemName = item.Name();
                bool ignoreSkipTokensForThisItem =
                    ignoreSkippedItems ||
                    (includeSavedPositionTypesEvenIfSkipped && savedTypes != null && savedTypes.Contains(itemName));

                if (ShouldSkipFullSort(item, ignoreSkipTokens: ignoreSkipTokensForThisItem))
                    continue;

                if (!groupedItems.ContainsKey(itemName))
                {
                    groupedItems[itemName] = new List<GrabbableObject>();
                }
                groupedItems[itemName].Add(item);
            }

            // Sort type order BEFORE layout:
            // - Two-handed (bulky) item types first (no config; always enabled)
            // - Then by item key/name for deterministic ordering
            // Note: we include reserved/custom-position types in the ordering list so they are still processed,
            // but CreateLayout will skip them without consuming normal slots.
            var orderedGroups = groupedItems
                .OrderByDescending(kvp => kvp.Value != null && kvp.Value.Any(IsTwoHandedItem))
                .ThenBy(kvp => kvp.Key)
                .ToList();
            List<string> orderedTypeNames = orderedGroups.Select(kvp => kvp.Key).ToList();

            // Create layout for non-custom-position item types only.
            // If a type has a saved position override, it should NOT consume a slot in the normal layout,
            // otherwise the layout will "skip" a spot (empty hole) where that type would have been.
            HashSet<string>? reservedTypes = savedTypes;

            // Vector3: x = offsetX, y = typeLayerY, z = offsetZ
            Dictionary<string, Vector3> layout = CreateLayout(orderedTypeNames, reservedTypes);

            // Process groups in the same deterministic order as layout (two-handed first)
            foreach (var group in orderedGroups)
            {
                string itemName = group.Key;
                List<GrabbableObject> items = group.Value;

                // Resolve per-type layout OR saved per-type position override.
                Vector3 typePos = layout.ContainsKey(itemName) ? layout[itemName] : Vector3.zero;
                bool hasCustomPos = SortPositions.TryGet(itemName, out Vector3 customShipLocal, out string? posError);
                if (posError != null)
                {
                    Log.Warning(posError);
                }
                bool ignoreSkipTokensForThisType = includeSavedPositionTypesEvenIfSkipped && hasCustomPos;

                // Determine the "lowest" (ground) Y once for this pile, then build layers upward from it.
                // This avoids per-item raycasts hitting other items and causing upward drift.
                const int LAYER_MASK = 268437761; // Copied from GrabbableObject.GetItemFloorPosition via LethalShipSort
                Vector3 pileCenterLocal = hasCustomPos
                    ? new Vector3(customShipLocal.x, 0f, customShipLocal.z)
                    : originLocal + new Vector3(typePos.x, 0f, typePos.z);
                // If a custom position is set, we treat it as an absolute target (no layout-based Y paging).
                float customYOffset = hasCustomPos ? customShipLocal.y : 0f;
                float typeLayerYOffset = hasCustomPos ? 0f : typePos.y;
                // Apply the configured sort origin Y as an additional offset ABOVE the detected ground,
                // but ONLY for non-custom-position types. Custom positions are treated as absolute.
                float originYOffset = hasCustomPos ? 0f : originLocal.y;
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
                        (groundYLocal + originYOffset + (item.itemProperties.verticalOffset - 0.1f) + (typeLayerYOffset + customYOffset) + pileY)
                        - (ShouldLowerYOffset(item) ? 0.2f : 0f),
                        pileCenterLocal.z + pileZ
                    );

                    Vector3 worldPos = ship.transform.TransformPoint(targetLocal);

                    if (!force && Vector3.Distance(worldPos, item.transform.position) < 0.25f)
                    {
                        continue;
                    }

                    yield return GrabbableRetry(item);

                    if (!ShouldSkipFullSort(item, ignoreSkipTokens: (ignoreSkippedItems || ignoreSkipTokensForThisType)))
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

            }

            Log.Chat("Sorting complete!", "00FF00");
            inProgress = false;
        }

        // Prefer compile-time access when possible, but use reflection to be resilient across game versions
        // / publicizer output changes. If we can't find the flag, we default to false.
        private static bool IsTwoHandedItem(GrabbableObject item)
        {
            try
            {
                if (item == null) return false;
                var props = item.itemProperties;
                if (props == null) return false;

                var t = props.GetType();
                const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                // Most common in Lethal Company:
                // - field: twoHanded (bool)
                // Less common/legacy/modded:
                // - field/property: isTwoHanded, twoHandedItem
                var f =
                    t.GetField("twoHanded", FLAGS) ??
                    t.GetField("isTwoHanded", FLAGS) ??
                    t.GetField("twoHandedItem", FLAGS) ??
                    t.GetField("<twoHanded>k__BackingField", FLAGS) ??
                    t.GetField("<isTwoHanded>k__BackingField", FLAGS) ??
                    t.GetField("<twoHandedItem>k__BackingField", FLAGS);
                if (f != null && f.FieldType == typeof(bool))
                    return (bool)f.GetValue(props);

                var p =
                    t.GetProperty("twoHanded", FLAGS) ??
                    t.GetProperty("isTwoHanded", FLAGS) ??
                    t.GetProperty("twoHandedItem", FLAGS);
                if (p != null && p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                    return (bool)p.GetValue(props, null);

                // Fallback: look for any bool field/property whose name contains "twohand" (case-insensitive).
                foreach (var field in t.GetFields(FLAGS))
                {
                    if (field.FieldType != typeof(bool)) continue;
                    if (field.Name.IndexOf("twohand", StringComparison.OrdinalIgnoreCase) >= 0)
                        return (bool)field.GetValue(props);
                }

                foreach (var prop in t.GetProperties(FLAGS))
                {
                    if (prop.PropertyType != typeof(bool)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    if (prop.Name.IndexOf("twohand", StringComparison.OrdinalIgnoreCase) >= 0)
                        return (bool)prop.GetValue(props, null);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TryStartGatherByQuery(string query, bool force, out string? error)
        {
            error = null;

            if (inProgress)
            {
                error = "Operation in progress";
                return false;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Missing item name";
                return false;
            }

            // Explicit /sort <item> should work even if the item type is in the user's skip lists.
            CategorizeItems(includeSkippedItems: true);
            if (scrap == null || scrap.Count == 0)
            {
                error = "No items found in ship";
                return false;
            }

            // Build groups once for name resolution
            Dictionary<string, List<GrabbableObject>> grouped = new Dictionary<string, List<GrabbableObject>>();
            foreach (var item in scrap)
            {
                if (ShouldSkipExplicitQuery(item)) continue;
                string key = item.Name();
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<GrabbableObject>();
                    grouped[key] = list;
                }
                list.Add(item);
            }

            if (!TryResolveItemKeyFromGrouped(grouped, query, out var resolved, out error))
                return false;

            if (!TryGetPlayerShipLocalTarget(out Vector3 targetLocal, out error))
                return false;

            // Convert player's ship-local Y to a ground-relative Y offset (prevents double-adding ground).
            if (!TryGetGroundYLocalAt(shipLocalXZ: targetLocal, out float groundYLocal, out string? groundError))
            {
                error = groundError;
                return false;
            }

            Vector3 targetWithOffset = new Vector3(targetLocal.x, targetLocal.y - groundYLocal, targetLocal.z);
            StartCoroutine(MoveItemsOfTypeToPosition(
                resolved,
                targetWithOffset,
                force,
                announce: true,
                ignoreSkipLists: true,
                applyTwoHandedSortYOffset: true
            ));
            return true;
        }

        // /pile [itemName] behavior:
        // - If itemName is provided: same as /sort <itemName> (fuzzy match against ship items)
        // - If itemName is omitted: use HELD item's type, and still work even if the held item is the only match
        public bool TryStartPileByQueryOrHeld(string? queryOrNull, bool force, out string? error)
        {
            error = null;

            if (inProgress)
            {
                error = "Operation in progress";
                return false;
            }

            var held = Player.Local != null ? Player.Local.currentlyHeldObjectServer as GrabbableObject : null;

            string query = !string.IsNullOrWhiteSpace(queryOrNull)
                ? queryOrNull!
                : (held != null ? held.Name() : "");
            bool useHeldType = string.IsNullOrWhiteSpace(queryOrNull);

            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Missing item name (hold the item or provide a name).";
                return false;
            }

            // Explicit gather should work even if the item type is in the user's skip lists.
            CategorizeItems(includeSkippedItems: true);

            // Build groups once for name resolution (ship items + held item)
            Dictionary<string, List<GrabbableObject>> grouped = new Dictionary<string, List<GrabbableObject>>();
            if (scrap != null)
            {
                foreach (var item in scrap)
                {
                    if (ShouldSkipExplicitQuery(item)) continue;
                    string key = item.Name();
                    if (!grouped.TryGetValue(key, out var list))
                    {
                        list = new List<GrabbableObject>();
                        grouped[key] = list;
                    }
                    list.Add(item);
                }
            }

            // Ensure held item type is resolvable even if it isn't currently counted as "in ship"
            if (held != null && !ShouldSkipExplicitQuery(held))
            {
                string heldKey = held.Name();
                if (!grouped.TryGetValue(heldKey, out var list))
                {
                    list = new List<GrabbableObject>();
                    grouped[heldKey] = list;
                }
                if (!list.Contains(held)) list.Add(held);
            }

            if (grouped.Count == 0)
            {
                error = "No items found in ship";
                return false;
            }

            if (!TryResolveItemKeyFromGrouped(grouped, query, out var resolved, out error))
                return false;

            if (!TryGetPlayerShipLocalTarget(out Vector3 targetLocal, out error))
                return false;

            // Convert player's ship-local Y to a ground-relative Y offset (prevents double-adding ground).
            if (!TryGetGroundYLocalAt(shipLocalXZ: targetLocal, out float groundYLocal, out string? groundError))
            {
                error = groundError;
                return false;
            }

            Vector3 targetWithOffset = new Vector3(targetLocal.x, targetLocal.y - groundYLocal, targetLocal.z);
            StartCoroutine(MoveItemsOfTypeToPosition(resolved, targetWithOffset, force, announce: true, ignoreSkipLists: true, dropHeldFirst: useHeldType));
            return true;
        }

        public bool TrySetAndMoveTypeToPlayer(string? queryOrNull, bool force, out string resolvedItemKey, out string? error)
        {
            resolvedItemKey = "";
            error = null;

            if (inProgress)
            {
                error = "Operation in progress";
                return false;
            }

            // Determine item key:
            // - If query is provided, resolve it against item types currently present on the ship (fuzzy/partial match).
            // - If omitted, use currently held item type.
            var held = Player.Local != null ? Player.Local.currentlyHeldObjectServer as GrabbableObject : null;
            if (!string.IsNullOrWhiteSpace(queryOrNull))
            {
                // Explicit /sort set ... should work even if the type is in skip lists.
                CategorizeItems(includeSkippedItems: true);

                if ((scrap == null || scrap.Count == 0) && held == null)
                {
                    error = "No items found in ship to match that name (hold the item or omit the name).";
                    return false;
                }

                // Build groups once for name resolution (same as /sort <item>)
                Dictionary<string, List<GrabbableObject>> grouped = new Dictionary<string, List<GrabbableObject>>();
                if (scrap != null)
                {
                    foreach (var item in scrap)
                    {
                        if (ShouldSkipExplicitQuery(item)) continue;
                        string key = item.Name();
                        if (!grouped.TryGetValue(key, out var list))
                        {
                            list = new List<GrabbableObject>();
                            grouped[key] = list;
                        }
                        list.Add(item);
                    }
                }

                // Also allow matching the held item type even if it's not currently counted in ship items.
                if (held != null && !ShouldSkipExplicitQuery(held))
                {
                    string heldKey = held.Name();
                    if (!grouped.TryGetValue(heldKey, out var list))
                    {
                        list = new List<GrabbableObject>();
                        grouped[heldKey] = list;
                    }
                    if (!list.Contains(held)) list.Add(held);
                }

                if (grouped.Count == 0)
                {
                    error = "No valid items found to match that name.";
                    return false;
                }

                if (!TryResolveItemKeyFromGrouped(grouped, queryOrNull, out resolvedItemKey, out error))
                    return false;
            }
            else
            {
                if (held == null)
                {
                    error = "No item name provided and no held item.";
                    return false;
                }
                resolvedItemKey = held.Name();
            }
            bool useHeldType = string.IsNullOrWhiteSpace(queryOrNull);

            if (string.IsNullOrWhiteSpace(resolvedItemKey))
            {
                error = "Invalid item name";
                return false;
            }

            if (!TryGetPlayerShipLocalTarget(out Vector3 targetLocal, out error))
                return false;

            // Save as ground-relative Y offset, not absolute ship-local Y (fixes "set goes to weird place").
            if (!TryGetGroundYLocalAt(shipLocalXZ: targetLocal, out float groundYLocal, out string? groundError))
            {
                error = groundError;
                return false;
            }

            Vector3 savedPos = new Vector3(targetLocal.x, targetLocal.y - groundYLocal, targetLocal.z);
            if (!SortPositions.Set(resolvedItemKey, savedPos, out error))
                return false;

            Log.ConfirmSound();
            // Also move to the exact saved position (ground-relative offset)
            StartCoroutine(MoveItemsOfTypeToPosition(resolvedItemKey, savedPos, force, announce: true, ignoreSkipLists: true, dropHeldFirst: useHeldType));
            return true;
        }

        private bool TryResolveItemKeyFromGrouped(Dictionary<string, List<GrabbableObject>> grouped, string query, out string resolvedKey, out string? error)
        {
            resolvedKey = "";
            error = null;

            string q = ApplyDefaultInputAliases(Extensions.NormalizeName(query));
            if (string.IsNullOrWhiteSpace(q))
            {
                error = "Invalid item name";
                return false;
            }

            if (grouped.ContainsKey(q))
            {
                resolvedKey = q;
                return true;
            }

            // Fuzzy match: allow partial matches.
            var keys = grouped.Keys.ToList();
            // Also match "loose" names that ignore underscores so users can type e.g. "weedkiller" for "weed_killer"
            // or "jet pack" for "jetpack" depending on the game's internal naming.
            string qLoose = q.Replace("_", "");
            var matches = keys
                .Where(k =>
                {
                    if (k.Contains(q) || q.Contains(k)) return true;
                    string kLoose = k.Replace("_", "");
                    return kLoose.Contains(qLoose) || qLoose.Contains(kLoose);
                })
                .Distinct()
                .ToList();

            if (matches.Count == 1)
            {
                resolvedKey = matches[0];
                return true;
            }
            if (matches.Count == 0)
            {
                error = $"No item match for '{query}'.";
                return false;
            }

            // Too many matches: tell user candidates
            string candidates = string.Join(", ", matches.Take(8));
            if (matches.Count > 8) candidates += ", ...";
            error = $"Ambiguous item '{query}'. Matches: {candidates}";
            return false;
        }

        private bool TryGetPlayerShipLocalTarget(out Vector3 shipLocalTarget, out string? error)
        {
            shipLocalTarget = default;
            error = null;

            if (Player.Local == null)
            {
                error = "Local player not ready yet";
                return false;
            }

            GameObject ship = GameObject.Find("Environment/HangarShip");
            if (ship == null)
            {
                error = "Ship not found";
                return false;
            }

            // Slightly in front of the player to avoid piling inside the player collider.
            Vector3 world = Player.Local.transform.position + Player.Local.transform.forward * 0.75f;
            shipLocalTarget = ship.transform.InverseTransformPoint(world);
            return true;
        }

        private bool TryGetGroundYLocalAt(Vector3 shipLocalXZ, out float groundYLocal, out string? error)
        {
            groundYLocal = 0f;
            error = null;

            GameObject ship = GameObject.Find("Environment/HangarShip");
            if (ship == null)
            {
                error = "Ship not found";
                return false;
            }

            const int LAYER_MASK = 268437761;
            Vector3 pileCenterLocal = new Vector3(shipLocalXZ.x, 0f, shipLocalXZ.z);
            Vector3 rayStartCenter = ship.transform.TransformPoint(pileCenterLocal + Vector3.up * 2f);
            if (Physics.Raycast(rayStartCenter, Vector3.down, out RaycastHit hitCenter, 80f, LAYER_MASK, QueryTriggerInteraction.Ignore))
            {
                groundYLocal = ship.transform.InverseTransformPoint(hitCenter.point).y;
                return true;
            }

            // Fallback: assume y=0 in ship space if raycast fails
            groundYLocal = 0f;
            return true;
        }

        private IEnumerator MoveItemsOfTypeToPosition(
            string itemKey,
            Vector3 targetCenterShipLocal,
            bool force,
            bool announce,
            bool ignoreSkipLists = false,
            bool applyTwoHandedSortYOffset = false,
            bool dropHeldFirst = false
        )
        {
            inProgress = true;
            if (announce)
            {
                Log.Chat($"Moving '{itemKey}' to your position... Press [Escape] to cancel", "FFFF00");
            }

            if (Player.Local == null)
            {
                Log.NotifyPlayer("Sorter Error", "Local player not ready yet", isWarning: true);
                inProgress = false;
                yield break;
            }

            GameObject ship = GameObject.Find("Environment/HangarShip");
            if (ship == null)
            {
                Log.NotifyPlayer("Sorter Error", "Ship not found", isWarning: true);
                inProgress = false;
                yield break;
            }

            // Only drop when the command relies on the held item (e.g. /pile with no name).
            if (dropHeldFirst)
            {
                var heldBeforeDrop = Player.Local.currentlyHeldObjectServer as GrabbableObject;
                yield return DropHeldItemIfAny(ship);

                // IMPORTANT: our cached `scrap` list is usually built BEFORE dropping (in the command handler),
                // so the just-dropped item may not be present and then won't be moved.
                // Refresh the scan so the dropped item becomes eligible.
                CategorizeItems(includeSkippedItems: true);

                // Extra safety: if the dropped item is still not captured by CategorizeItems (timing/flags),
                // include it explicitly if it matches the target type and is in the ship.
                if (heldBeforeDrop != null && heldBeforeDrop.Name() == itemKey && heldBeforeDrop.isInShipRoom)
                {
                    if (scrap == null) scrap = new List<GrabbableObject>();
                    if (!scrap.Contains(heldBeforeDrop))
                        scrap.Add(heldBeforeDrop);
                }
            }
            BeginOperationLock();

            // Group items by name (same as full sort) so filtering uses stable keys.
            Dictionary<string, List<GrabbableObject>> groupedItems = new Dictionary<string, List<GrabbableObject>>();
            foreach (GrabbableObject item in scrap)
            {
                if (ignoreSkipLists ? ShouldSkipExplicitQuery(item) : ShouldSkip(item)) continue;
                string name = item.Name();
                if (!groupedItems.TryGetValue(name, out var list))
                {
                    list = new List<GrabbableObject>();
                    groupedItems[name] = list;
                }
                list.Add(item);
            }

            if (!groupedItems.TryGetValue(itemKey, out var items) || items.Count == 0)
            {
                // Still allow moving the held item if it matches (sort set without name request)
                items = new List<GrabbableObject>();
            }

            // Pile center is EXACTLY the requested ship-local position (x/z); y is treated as an extra offset.
            Vector3 pileCenterLocal = new Vector3(targetCenterShipLocal.x, 0f, targetCenterShipLocal.z);
            float extraYOffset = targetCenterShipLocal.y;

            // Determine pile ground once (same as full sort)
            const int LAYER_MASK = 268437761;
            float groundYLocal = pileCenterLocal.y;
            {
                Vector3 rayStartCenter = ship.transform.TransformPoint(pileCenterLocal + Vector3.up * 2f);
                if (Physics.Raycast(rayStartCenter, Vector3.down, out RaycastHit hitCenter, 80f, LAYER_MASK, QueryTriggerInteraction.Ignore))
                {
                    groundYLocal = ship.transform.InverseTransformPoint(hitCenter.point).y;
                }
            }

            // If the player is holding an item of this type, include it and move it too.
            GrabbableObject? held = Player.Local != null ? Player.Local.currentlyHeldObjectServer as GrabbableObject : null;
            List<GrabbableObject> itemsToMove = new List<GrabbableObject>(items);
            if (held != null && held.Name() == itemKey && !itemsToMove.Contains(held))
            {
                itemsToMove.Insert(0, held);
            }

            int moved = 0;
            for (int stackIndex = 0; stackIndex < itemsToMove.Count; stackIndex++)
            {
                GrabbableObject item = itemsToMove[stackIndex];
                if (ShouldBreak(item))
                {
                    Log.NotifyPlayer("Sorter Stopping", "Operation cancelled or ship is in motion", isWarning: true);
                    inProgress = false;
                    yield break;
                }

                float pileX = 0f;
                float pileZ = 0f;
                float pileY;

                if (stackSameTypeTogether.Value)
                {
                    pileY = stackIndex * Mathf.Max(0f, sameTypeStackStepY.Value);
                }
                else
                {
                    int cols = Mathf.Max(1, itemsPerRow.Value);
                    int rows = cols;
                    int perLayer = cols * rows;
                    int layer = stackIndex / perLayer;
                    int inLayer = stackIndex % perLayer;
                    int r = inLayer / cols;
                    int c = inLayer % cols;

                    const float pileSpacing = 0.10f;
                    const float pileLayerHeight = 0.07f;

                    pileX = (c - (cols - 1) / 2f) * pileSpacing;
                    pileZ = (r - (rows - 1) / 2f) * pileSpacing;
                    pileY = layer * pileLayerHeight;
                }

                Vector3 targetLocal = new Vector3(
                    pileCenterLocal.x + pileX,
                    (groundYLocal + (item.itemProperties.verticalOffset - 0.05f) + extraYOffset + pileY),
                    pileCenterLocal.z + pileZ
                );

                Vector3 worldPos = ship.transform.TransformPoint(targetLocal);
                if (!force && Vector3.Distance(worldPos, item.transform.position) < 0.25f)
                {
                    continue;
                }

                // Special-case held item: it's filtered by ShouldSkip (isHeld==true), but user wants it moved too.
                if (held != null && item == held)
                {
                    item.floorYRot = -1;
                    MoveUtils.MoveItemOnShipLocal(item, targetLocal, item.floorYRot);
                    moved++;
                    yield return null;
                    continue;
                }

                yield return GrabbableRetry(item);

                if (!(ignoreSkipLists ? ShouldSkipExplicitQuery(item) : ShouldSkip(item)))
                {
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

                    moved++;
                }
            }

            Log.Chat($"Moved '{itemKey}' ({moved} items)", "00FF00");
            inProgress = false;
        }

        private Dictionary<string, Vector3> CreateLayout(List<string> itemNames, HashSet<string>? reservedTypes = null)
        {
            Dictionary<string, Vector3> layout = new Dictionary<string, Vector3>();

            // IMPORTANT: itemNames are expected to be pre-sorted by the caller.
            // (We need to preserve "two-handed first" ordering.)

            // User request: do NOT advance Z for type layout (prevents pushing into doors/walls).
            // Types spread only on X (left/right). If there are too many types, we go UP on Y and restart from first X.
            int cols = Mathf.Max(1, itemsPerRow.Value);

            // If we have reserved types (saved custom positions), skip them without advancing layout slots.
            // This prevents "holes" in the normal grid when some types are placed elsewhere.
            int reservedCount = 0;
            for (int i = 0; i < itemNames.Count; i++)
            {
                string itemName = itemNames[i];

                if (reservedTypes != null && reservedTypes.Contains(itemName))
                {
                    reservedCount++;
                    continue;
                }

                int layoutIndex = i - reservedCount;
                int layer = layoutIndex / cols;
                int col = layoutIndex % cols;

                float centerOffset = (cols - 1) * 0.5f;
                float xOffset = (col - centerOffset) * itemSpacing.Value;
                float zOffset = 0f;
                float yOffset = layer * rowSpacing.Value + 0.05f;

                layout[itemName] = new Vector3(xOffset, yOffset, zOffset);
            }

            return layout;
        }

        public void CategorizeItems(bool includeSkippedItems = false)
        {
            scrap = new List<GrabbableObject>();

            GrabbableObject[] allItems = UnityEngine.Object.FindObjectsOfType<GrabbableObject>();

            foreach (GrabbableObject item in allItems)
            {
                // Include ALL grabbable items that are inside the ship room (not only scrap).
                // Store-bought items like shovel/weedkiller are not scrap and were previously excluded.
                bool skip = includeSkippedItems ? ShouldSkipExplicitQuery(item) : ShouldSkip(item);
                if (!skip && item.isInShipRoom)
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
            // User request: allow sorting even while ship is taking off / landing.
            // Only stop if the user cancels or the player is being beamed.
            return !inProgress ||
                   (Player.Local != null && (Player.Local.beamOutParticle.isPlaying || Player.Local.beamUpParticle.isPlaying));
        }

        // For explicit item queries (/sort <item>), we should NOT apply the user's skip lists.
        // Otherwise, any type listed in skippedItems becomes "unsortable" even when requested directly.
        private bool ShouldSkipExplicitQuery(GrabbableObject item)
        {
            if (item == null) return true;

            // Same baseline filtering as ShouldSkip, but without skip-list tokens.
            if (!item.grabbable || item.deactivated || item.isHeld || item.isPocketed)
                return true;

            if (item.Name() == "body")
                return true;

            if (!item.isInShipRoom)
                return true;

            return false;
        }

        private static string NormalizeSkipListConfig(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return list ?? "";

            var seen = new HashSet<string>();
            var tokens = new List<string>();

            foreach (string raw in list.Split(','))
            {
                string t = raw?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(t)) continue;

                t = NormalizeSkipToken(t);
                if (t == "rader_booster") t = "radar_booster";

                if (string.IsNullOrWhiteSpace(t)) continue;
                if (seen.Add(t))
                    tokens.Add(t);
            }

            return string.Join(", ", tokens);
        }

        private static List<string> ParseSkipListTokens(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return new List<string>();
            return list.Split(',')
                .Select(t => NormalizeSkipToken(t))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t == "rader_booster" ? "radar_booster" : t)
                .Distinct()
                .ToList();
        }

        // Skip list tokens are stored as normalized item keys (underscores), but we guard against
        // the historical bug where a leading space became a leading underscore (e.g. "_kitchen_knife").
        private static string NormalizeSkipToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            // Normalize to item-key style (spaces/hyphens -> underscores)
            string token = Extensions.NormalizeName(s);
            // Canonicalize legacy/internal names so config is stored consistently
            // and matches user-facing keys (shotgun/ammo).
            token = ApplyDefaultInputAliases(token);
            // Fix bug: leading/trailing underscores caused by leading/trailing spaces in user input
            token = token.Trim('_');
            // Legacy typo fix
            if (token == "rader_booster") token = "radar_booster";
            return token;
        }

        private static void TrySaveConfig()
        {
            try
            {
                Plugin.config?.Save();
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Failed to save config: {e.Message}");
            }
        }

        public bool TrySkipAdd(string rawItemName, out string? error, out string? message)
        {
            error = null;
            message = null;

            string token = NormalizeSkipToken(rawItemName);
            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Usage: /sort skip add <itemName>";
                return false;
            }

            var tokens = ParseSkipListTokens(skippedItems.Value);
            if (tokens.Contains(token))
            {
                message = $"Already in skippedItems: {token}";
                return true;
            }

            tokens.Add(token);
            skippedItems.Value = NormalizeSkipListConfig(string.Join(", ", tokens));
            TrySaveConfig();
            message = $"Added to skippedItems: {token}";
            return true;
        }

        public bool TrySkipRemove(string rawItemName, out string? error, out string? message)
        {
            error = null;
            message = null;

            string token = NormalizeSkipToken(rawItemName);
            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Usage: /sort skip remove <itemName>";
                return false;
            }

            var tokens = ParseSkipListTokens(skippedItems.Value);
            int before = tokens.Count;
            tokens = tokens.Where(t => t != token).ToList();
            if (tokens.Count == before)
            {
                message = $"Not in skippedItems: {token}";
                return true;
            }

            skippedItems.Value = NormalizeSkipListConfig(string.Join(", ", tokens));
            TrySaveConfig();
            message = $"Removed from skippedItems: {token}";
            return true;
        }

        public List<string> GetSkippedTokens()
        {
            return ParseSkipListTokens(skippedItems.Value)
                .OrderBy(t => t)
                .ToList();
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
            // Apply skip list to ALL items (global).
            string list = skippedItems.Value;

            if (!string.IsNullOrWhiteSpace(list))
            {
                string[] skipped = list.Split(',');
                foreach (string skippedItem in skipped)
                {
                    string token = NormalizeSkipToken(skippedItem);
                    if (string.IsNullOrWhiteSpace(token)) continue;

                    // Match against canonicalized item key as well, to support legacy/internal names.
                    string itemKey = ApplyDefaultInputAliases(itemName);
                    if (itemKey.Contains(token))
                        return true;
                }
            }

            return false;
        }

        // Full sort (/sort with no item name) uses skippedItems as a GLOBAL skip list,
        // regardless of scrap vs non-scrap, per user preference.
        private bool ShouldSkipFullSort(GrabbableObject item, bool ignoreSkipTokens = false)
        {
            if (item == null) return true;

            if (!item.grabbable || item.deactivated || item.isHeld || item.isPocketed)
                return true;

            if (item.Name() == "body")
                return true;

            if (!item.isInShipRoom)
                return true;

            if (ignoreSkipTokens)
                return false;

            string itemName = item.Name();

            // Global skip list (substring match). Note: only one list now.
            string list = skippedItems.Value;

            if (!string.IsNullOrWhiteSpace(list))
            {
                foreach (string skippedItem in list.Split(','))
                {
                    string token = NormalizeSkipToken(skippedItem);
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    if (itemName.Contains(token))
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
        public override string Description =>
            "Sorts items on the ship.\n" +
            "Usage:\n" +
            "  /sort                 -> sort everything\n" +
            "  /sort -a              -> sort everything, IGNORE skippedItems\n" +
            "  /sort -b              -> full sort, but DO NOT skip item types that have a saved /sort set position\n" +
            "                       (Note: -a and -b cannot be combined)\n" +
            "  /sort skip list       -> show skippedItems tokens\n" +
            "  /sort skip add <name> -> add token to skippedItems (name can include spaces)\n" +
            "  /sort skip remove <name> -> remove token from skippedItems\n" +
            "  /sort <itemName>      -> pull that item type to YOUR position (e.g. /sort cash_register)\n" +
            "  /sort <number>        -> shortcut from JSON (pull to you) (e.g. /sort 1)\n" +
            "  /sort bind <name|id>  -> bind your HELD item to an alias name OR shortcut id (then /sort <name> or /sort <id> works)\n" +
            "  /sort set [itemName]  -> set this type's future sort position to YOUR position (name optional if holding)\n" +
            "  /sort reset [itemName]-> delete saved sort position for this type (name optional if holding)\n" +
            "  /sort bindings        -> list all binds (numbers + names)\n" +
            "  /sort positions       -> list saved sort positions";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            error = null;

            QuickSort.Log.Info($"SortCommand.Invoke called with args: [{string.Join(", ", args)}]");

            if (args.Length > 0 && args[0] == "help")
            {
                ChatCommandAPI.ChatCommandAPI.Print(Description);
                return true;
            }

            // Works on non-host too (vanilla ServerRpc calls from local player).
            // Note: force flags were removed.
            if (args.Contains("-r") || args.Contains("-redo"))
            {
                error = "Flag '-r/-redo' was removed.";
                return false;
            }

            // Reject combined flags explicitly (users may try "/sort -ab")
            if (args.Contains("-ab") || args.Contains("-ba"))
            {
                error = "Flags '-a' and '-b' cannot be combined. Use only one.";
                return false;
            }

            // Flags:
            // -a / -all: full sort but ignore skippedItems (sort everything)
            // -b / -bound: full sort but include item types with saved /sort set positions even if skipped
            bool ignoreSkippedItems =
                args.Contains("-a") || args.Contains("-all") ||
                (kwargs != null && (kwargs.ContainsKey("a") || kwargs.ContainsKey("all")));
            bool includeSavedPositionTypesEvenIfSkipped =
                args.Contains("-b") || args.Contains("-bound") ||
                (kwargs != null && (kwargs.ContainsKey("b") || kwargs.ContainsKey("bound")));

            if (ignoreSkippedItems && includeSavedPositionTypesEvenIfSkipped)
            {
                error = "Flags '-a' and '-b' cannot be combined. Use only one.";
                return false;
            }

            string[] filteredArgs = args
                .Where(a => a != "-a" && a != "-all" && a != "-b" && a != "-bound" && a != "-ab" && a != "-ba")
                .ToArray();

            // Some subcommands are "config/info only" and should work even when not in orbit/landed,
            // and even if the player isn't currently inside the ship.
            // Movement/sort operations still require CanSortNow + inside-ship.
            string sub0 = (filteredArgs != null && filteredArgs.Length > 0) ? filteredArgs[0] : "";
            bool isNonMovementCommand =
                sub0 == "skip" ||
                sub0 == "bindings" || sub0 == "binds" || sub0 == "shortcuts" || sub0 == "aliases" ||
                sub0 == "positions" ||
                sub0 == "bind" ||
                sub0 == "reset";

            if (!isNonMovementCommand)
            {
                // User request: allow usage during takeoff/landing. Do NOT gate on ship state flags.
                if (!Sorter.EnsureLocalPlayerInShip(out var shipErr))
                {
                    error = shipErr ?? "You must be inside the ship to use this command.";
                    QuickSort.Log.Warning($"SortCommand failed: {error}");
                    return false;
                }

                if (Sorter.inProgress)
                {
                    error = "Operation in progress";
                    QuickSort.Log.Warning($"SortCommand failed: {error}");
                    return false;
                }
            }

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

            // Subcommands / shortcuts / item name
            if (filteredArgs.Length > 0)
            {
                if (filteredArgs[0] == "skip")
                {
                    // /sort skip list
                    // /sort skip add <itemName...>
                    // /sort skip remove <itemName...>
                    if (filteredArgs.Length < 2)
                    {
                        error = "Usage: /sort skip <list|add|remove> ...";
                        return false;
                    }

                    string sub = filteredArgs[1];
                    if (sub == "list" || sub == "ls")
                    {
                        var tokens = sorter.GetSkippedTokens();
                        if (tokens.Count == 0)
                        {
                            ChatCommandAPI.ChatCommandAPI.Print("skippedItems is empty.");
                            return true;
                        }

                        string text = string.Join(", ", tokens.Take(20));
                        if (tokens.Count > 20) text += ", ...";
                        ChatCommandAPI.ChatCommandAPI.Print($"skippedItems ({tokens.Count}): {text}");
                        return true;
                    }

                    // Allow using shortcut id or alias in addition to raw item name.
                    string ResolveSkipTarget(string raw)
                    {
                        raw = (raw ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(raw)) return raw;

                        if (int.TryParse(raw, out int shortcutId) && shortcutId > 0)
                        {
                            if (SortShortcuts.TryResolve(shortcutId, out var itemKey, out _))
                                return itemKey;
                            return raw;
                        }

                        if (SortShortcuts.TryResolveAlias(raw, out var aliasedKey, out _))
                            return aliasedKey;

                        return raw;
                    }

                    if (sub == "add")
                    {
                        string name = string.Join(" ", filteredArgs.Skip(2)).Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = (Player.Local?.currentlyHeldObjectServer as GrabbableObject)?.Name() ?? "";
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                error = "Usage: /sort skip add <itemName> (or hold an item)";
                                return false;
                            }
                        }
                        name = ResolveSkipTarget(name);
                        if (!sorter.TrySkipAdd(name, out error, out var msg))
                            return false;
                        if (!string.IsNullOrWhiteSpace(msg))
                            ChatCommandAPI.ChatCommandAPI.Print(msg);
                        return true;
                    }

                    if (sub == "remove" || sub == "rm" || sub == "del")
                    {
                        string name = string.Join(" ", filteredArgs.Skip(2)).Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = (Player.Local?.currentlyHeldObjectServer as GrabbableObject)?.Name() ?? "";
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                error = "Usage: /sort skip remove <itemName> (or hold an item)";
                                return false;
                            }
                        }
                        name = ResolveSkipTarget(name);
                        if (!sorter.TrySkipRemove(name, out error, out var msg))
                            return false;
                        if (!string.IsNullOrWhiteSpace(msg))
                            ChatCommandAPI.ChatCommandAPI.Print(msg);
                        return true;
                    }

                    error = "Usage: /sort skip <list|add|remove> ...";
                    return false;
                }

                if (filteredArgs[0] == "bind")
                {
                    // /sort bind <aliasName|id>
                    // /sort bind reset <aliasName|id>
                    if (filteredArgs.Length < 2)
                    {
                        error = "Usage: /sort bind <name> (hold the item you want to bind)";
                        return false;
                    }

                    // reset/remove bindings
                    if (filteredArgs.Length >= 3 && (filteredArgs[1] == "reset" || filteredArgs[1] == "rm" || filteredArgs[1] == "remove" || filteredArgs[1] == "del"))
                    {
                        string target = string.Join(" ", filteredArgs.Skip(2)).Trim();
                        if (string.IsNullOrWhiteSpace(target))
                        {
                            error = "Usage: /sort bind reset <name|id>";
                            return false;
                        }

                        if (int.TryParse(target, out int removeId) && removeId > 0)
                        {
                            if (!SortShortcuts.RemoveShortcut(removeId, out bool removed, out var rmErr))
                            {
                                error = rmErr ?? "Failed to remove shortcut.";
                                return false;
                            }
                            ChatCommandAPI.ChatCommandAPI.Print(removed ? $"Unbound {removeId}" : $"No binding for {removeId}");
                            return true;
                        }
                        else
                        {
                            if (!SortShortcuts.RemoveAlias(target, out bool removed, out var rmErr))
                            {
                                error = rmErr ?? "Failed to remove alias.";
                                return false;
                            }
                            string key = Extensions.NormalizeName(target);
                            ChatCommandAPI.ChatCommandAPI.Print(removed ? $"Unbound {key}" : $"No binding for {key}");
                            return true;
                        }
                    }

                    var held = Player.Local != null ? Player.Local.currentlyHeldObjectServer as GrabbableObject : null;
                    if (held == null)
                    {
                        error = "You must hold an item to bind.";
                        return false;
                    }

                    string nameOrIdRaw = string.Join(" ", filteredArgs.Skip(1));
                    string itemKey = held.Name();

                    // If user binds a number (e.g. "1"), treat it as a shortcut id bind.
                    if (int.TryParse(nameOrIdRaw.Trim(), out int bindShortcutId) && bindShortcutId > 0)
                    {
                        if (!SortShortcuts.SetShortcut(bindShortcutId, itemKey, out var setErr))
                        {
                            error = setErr ?? "Failed to bind shortcut.";
                            return false;
                        }
                        ChatCommandAPI.ChatCommandAPI.Print($"Bound {bindShortcutId} => {itemKey}");
                        return true;
                    }

                    if (!SortShortcuts.BindAlias(nameOrIdRaw, itemKey, out var bindErr))
                    {
                        error = bindErr ?? "Failed to bind alias.";
                        return false;
                    }

                    ChatCommandAPI.ChatCommandAPI.Print($"Bound {Extensions.NormalizeName(nameOrIdRaw)} => {itemKey}");
                    return true;
                }

                if (filteredArgs[0] == "reset")
                {
                    // /sort reset [itemName]
                    // If itemName is omitted, use currently held item.
                    string? resetKey = filteredArgs.Length > 1
                        ? Extensions.NormalizeName(string.Join(" ", filteredArgs.Skip(1)))
                        : (Player.Local?.currentlyHeldObjectServer as GrabbableObject)?.Name();

                    if (string.IsNullOrWhiteSpace(resetKey))
                    {
                        error = "Missing item name (hold the item or provide a name).";
                        return false;
                    }

                    if (!SortPositions.Remove(resetKey, out bool removed, out var removeErr))
                    {
                        error = removeErr ?? "Failed to remove saved position.";
                        return false;
                    }
                    if (removeErr != null)
                    {
                        error = removeErr;
                        return false;
                    }

                    if (removed)
                        ChatCommandAPI.ChatCommandAPI.Print($"Removed saved sort position for '{resetKey}'.");
                    else
                        ChatCommandAPI.ChatCommandAPI.Print($"No saved sort position for '{resetKey}'.");

                    return true;
                }

                if (filteredArgs[0] == "set")
                {
                    string? setQuery = filteredArgs.Length > 1 ? string.Join(" ", filteredArgs.Skip(1)) : null;
                    if (!sorter.TrySetAndMoveTypeToPlayer(setQuery, force: false, out var resolvedKey, out error))
                        return false;

                    if (!string.IsNullOrWhiteSpace(setQuery))
                    {
                        string qNorm = Extensions.NormalizeName(setQuery);
                        if (!string.IsNullOrWhiteSpace(qNorm) && qNorm != resolvedKey)
                        {
                            ChatCommandAPI.ChatCommandAPI.Print($"Resolved '{setQuery}' => '{resolvedKey}'.");
                        }
                    }

                    string label = string.IsNullOrWhiteSpace(resolvedKey) ? "held_item" : resolvedKey;
                    if (SortPositions.TryGet(resolvedKey, out var saved, out var readErr))
                    {
                        ChatCommandAPI.ChatCommandAPI.Print(
                            $"Saved sort position for '{label}' => (x={saved.x:F2}, y={saved.y:F2}, z={saved.z:F2}).");
                    }
                    else
                    {
                        if (readErr != null) QuickSort.Log.Warning(readErr);
                        ChatCommandAPI.ChatCommandAPI.Print($"Saved sort position for '{label}'.");
                    }
                    return true;
                }

                if (filteredArgs[0] == "positions")
                {
                    var list = SortPositions.ListAll(out var listError);
                    if (listError != null)
                    {
                        error = listError;
                        return false;
                    }

                    if (list.Count == 0)
                    {
                        ChatCommandAPI.ChatCommandAPI.Print("No saved sort positions.");
                        return true;
                    }

                    string text = string.Join(", ", list.Take(8).Select(p => $"{p.itemKey}=(x={p.shipLocalPos.x:F1},y={p.shipLocalPos.y:F1},z={p.shipLocalPos.z:F1})"));
                    if (list.Count > 8) text += ", ...";
                    ChatCommandAPI.ChatCommandAPI.Print(text);
                    return true;
                }

                // "shortcut" and "bind" are the same concept now: list everything together.
                if (filteredArgs[0] == "bindings" || filteredArgs[0] == "binds" || filteredArgs[0] == "shortcuts" || filteredArgs[0] == "aliases")
                {
                    var shortcuts = SortShortcuts.ListShortcuts(out var shortcutErr);
                    if (shortcutErr != null)
                    {
                        error = shortcutErr;
                        return false;
                    }

                    var aliases = SortShortcuts.ListAliases(out var aliasErr);
                    if (aliasErr != null)
                    {
                        error = aliasErr;
                        return false;
                    }

                    if (shortcuts.Count == 0 && aliases.Count == 0)
                    {
                        ChatCommandAPI.ChatCommandAPI.Print("No bindings found.");
                        return true;
                    }

                    if (shortcuts.Count > 0)
                    {
                        string text = string.Join(", ", shortcuts.Select(s => $"{s.id}={s.itemKey}"));
                        ChatCommandAPI.ChatCommandAPI.Print(text);
                    }
                    if (aliases.Count > 0)
                    {
                        string text = string.Join(", ", aliases.Take(12).Select(a => $"{a.alias}={a.itemKey}"));
                        if (aliases.Count > 12) text += ", ...";
                        ChatCommandAPI.ChatCommandAPI.Print(text);
                    }

                    return true;
                }

                // /sort 1 -> resolve via JSON
                if (filteredArgs.Length == 1 && int.TryParse(filteredArgs[0], out int shortcutId))
                {
                    if (!SortShortcuts.TryResolve(shortcutId, out var itemKey, out var shortcutError))
                    {
                        error = shortcutError ?? "Unknown shortcut error";
                        return false;
                    }

                    Log.ConfirmSound();
                    if (!sorter.TryStartGatherByQuery(itemKey, force: false, out error))
                        return false;

                    QuickSort.Log.Info("SortCommand executed successfully (shortcut move)");
                    return true;
                }

                // /sort alias -> resolve via JSON alias binding
                if (filteredArgs.Length == 1)
                {
                    if (SortShortcuts.TryResolveAlias(filteredArgs[0], out var aliasedKey, out _))
                    {
                        Log.ConfirmSound();
                        if (!sorter.TryStartGatherByQuery(aliasedKey, force: false, out error))
                            return false;

                        QuickSort.Log.Info("SortCommand executed successfully (alias move)");
                        return true;
                    }
                }

                // /sort item name (allow spaces)
                string itemQuery = string.Join(" ", filteredArgs);
                Log.ConfirmSound();
                if (!sorter.TryStartGatherByQuery(itemQuery, force: false, out error))
                    return false;

                QuickSort.Log.Info("SortCommand executed successfully (item move)");
                return true;
            }

            // Default: full sort
            // If -a is set, we ignore skippedItems entirely.
            // If -b is set, we must include skipped types in the scan so SortItems can selectively keep only
            // the ones that have saved positions.
            sorter.CategorizeItems(includeSkippedItems: (ignoreSkippedItems || includeSavedPositionTypesEvenIfSkipped));
            Log.ConfirmSound();
            sorter.StartCoroutine(sorter.SortItems(force: false, ignoreSkippedItems: ignoreSkippedItems, includeSavedPositionTypesEvenIfSkipped: includeSavedPositionTypesEvenIfSkipped));
            QuickSort.Log.Info("SortCommand executed successfully (full sort)");
            return true;
        }
    }

    // Short commands:
    // - /sb ... == /sort bind ...
    // - /ss ... == /sort set ...
    // - /sr ... == /sort reset ...
    // - /sp      == /sort positions
    // - /sbl     == /sort bindings
    // - /sk ...  == /sort skip ...
    public class SortBindCommand : Command
    {
        public override string Name => "sb";
        public override string[] Commands => new[] { "sb", Name };
        public override string Description =>
            "Shortcut for /sort bind\n" +
            "Usage:\n" +
            "  /sb <name|id>  -> bind your HELD item to an alias name OR shortcut id";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            // Reuse SortCommand by injecting the subcommand token.
            var forwarded = new[] { "bind" }.Concat(args ?? Array.Empty<string>()).ToArray();
            return new SortCommand().Invoke(forwarded, kwargs, out error);
        }
    }

    public class SortSetCommand : Command
    {
        public override string Name => "ss";
        public override string[] Commands => new[] { "ss", Name };
        public override string Description =>
            "Shortcut for /sort set\n" +
            "Usage:\n" +
            "  /ss [itemName]  -> set saved sort position for this type (name optional if holding; partial match supported)";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            var forwarded = new[] { "set" }.Concat(args ?? Array.Empty<string>()).ToArray();
            return new SortCommand().Invoke(forwarded, kwargs, out error);
        }
    }

    public class SortResetCommand : Command
    {
        public override string Name => "sr";
        public override string[] Commands => new[] { "sr", Name };
        public override string Description =>
            "Shortcut for /sort reset\n" +
            "Usage:\n" +
            "  /sr [itemName]  -> delete saved sort position for this type (name optional if holding)";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            var forwarded = new[] { "reset" }.Concat(args ?? Array.Empty<string>()).ToArray();
            return new SortCommand().Invoke(forwarded, kwargs, out error);
        }
    }

    public class SortPositionsCommand : Command
    {
        public override string Name => "sp";
        public override string[] Commands => new[] { "sp", Name };
        public override string Description =>
            "Shortcut for /sort positions\n" +
            "Usage:\n" +
            "  /sp  -> list saved sort positions";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            // Ignore args; keep behavior deterministic
            var forwarded = new[] { "positions" };
            return new SortCommand().Invoke(forwarded, kwargs, out error);
        }
    }

    public class SortBindingsListCommand : Command
    {
        public override string Name => "sbl";
        public override string[] Commands => new[] { "sbl", Name };
        public override string Description =>
            "Shortcut for /sort bindings\n" +
            "Usage:\n" +
            "  /sbl  -> list bindings (shortcuts + aliases)";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            var forwarded = new[] { "bindings" };
            return new SortCommand().Invoke(forwarded, kwargs, out error);
        }
    }

    public class SortSkipCommand : Command
    {
        public override string Name => "sk";
        public override string[] Commands => new[] { "sk", Name };
        public override string Description =>
            "Shortcut for /sort skip\n" +
            "Usage:\n" +
            "  /sk list            -> show skippedItems tokens\n" +
            "  /sk add [itemName]  -> add token (or use held item if omitted)\n" +
            "  /sk remove [itemName] -> remove token (or use held item if omitted)";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            var forwarded = new[] { "skip" }.Concat(args ?? Array.Empty<string>()).ToArray();
            return new SortCommand().Invoke(forwarded, kwargs, out error);
        }
    }

    public class PileCommand : Command
    {
        public override string Name => "pile";
        public override string[] Commands => new[] { "pile", Name };
        public override string Description =>
            "Piles a specific item type onto your position (like /sort <item>).\n" +
            "Usage:\n" +
            "  /pile <itemName>  -> pull that item type to YOUR position (partial match supported)\n" +
            "  /pile             -> uses your HELD item type and also moves the held item";

        public override bool Invoke(string[] args, Dictionary<string, string> kwargs, out string? error)
        {
            error = null;

            if (args.Length > 0 && args[0] == "help")
            {
                ChatCommandAPI.ChatCommandAPI.Print(Description);
                return true;
            }

            if (!Sorter.EnsureLocalPlayerInShip(out var shipErr))
            {
                error = shipErr ?? "You must be inside the ship to use this command.";
                return false;
            }

            // Get Sorter instance from Plugin (Unity-safe null checks)
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
                    }

                    sorter = Plugin.sorterObject.GetComponent<Sorter>();
                    if (sorter == null)
                    {
                        sorter = Plugin.sorterObject.AddComponent<Sorter>();
                    }
                }
                catch (Exception e)
                {
                    error = "Sorter not initialized yet";
                    QuickSort.Log.Error("PileCommand failed: " + e.Message);
                    return false;
                }
            }

            string? query = (args != null && args.Length > 0) ? string.Join(" ", args) : null;
            Log.ConfirmSound();
            return sorter.TryStartPileByQueryOrHeld(query, force: false, out error);
        }
    }
}

