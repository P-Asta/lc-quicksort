using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CommandChat = ChatCommandAPI.Utils.Chat;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QuickSort
{
    internal static class CruiserSorter
    {
        private const int MaxShelfZoneSlots = 20;
        internal const float SavedHeightOffset = 0.3f;

        private static int ZoneCapacity(string zone) => zone == "D2" ? 1 : MaxShelfZoneSlots;

        // Vehicle-local zones from veber01/LC-CruiserLoader (MIT):
        // https://github.com/veber01/LC-CruiserLoader/blob/main/Patches/CruiserZones.cs
        private static readonly Dictionary<string, Vector3> Zones = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = new Vector3(-1.15f, -0.4f, -2.6f),
            ["A2"] = new Vector3(-1.15f, 0.46f, -2.6f),
            ["A3"] = new Vector3(-1.15f, 1.22f, -2.6f),
            ["B1"] = new Vector3(-1.15f, -0.4f, -1.7f),
            ["B2"] = new Vector3(-1.15f, 0.46f, -1.7f),
            ["B3"] = new Vector3(-1.15f, 1.22f, -1.7f),
            ["C1"] = new Vector3(-1.15f, -0.4f, -0.8f),
            ["C2"] = new Vector3(-1.15f, 0.46f, -0.8f),
            ["C3"] = new Vector3(-1.15f, 1.22f, -0.8f),
            ["D1"] = new Vector3(-0.91f, 0f, 0.3f),
            ["D2"] = new Vector3(0f, -0.5f, -0.55f),
            ["D3"] = new Vector3(0.82f, 0f, 0.3f),
            ["E1"] = new Vector3(1.15f, -0.4f, -2.6f),
            ["E2"] = new Vector3(1.15f, 0.46f, -2.6f),
            ["E3"] = new Vector3(1.15f, 1.22f, -2.6f),
            ["F1"] = new Vector3(1.15f, -0.4f, -1.7f),
            ["F2"] = new Vector3(1.15f, 0.46f, -1.7f),
            ["F3"] = new Vector3(1.15f, 1.22f, -1.7f),
            ["G1"] = new Vector3(1.15f, -0.4f, -0.8f),
            ["G2"] = new Vector3(1.15f, 0.46f, -0.8f),
            ["G3"] = new Vector3(1.15f, 1.22f, -0.8f)
        };

        internal static bool TryGetZone(string? name, out string zoneName, out Vector3 position)
        {
            zoneName = (name ?? "").Trim().ToUpperInvariant();
            return Zones.TryGetValue(zoneName, out position);
        }

        internal static string? ZoneAt(Vector3 position)
        {
            foreach (var zone in Zones)
                if (Mathf.Abs(zone.Value.x - position.x) < 0.001f &&
                    Mathf.Abs(zone.Value.z - position.z) < 0.001f &&
                    (Mathf.Abs(zone.Value.y - position.y) < 0.001f ||
                     Mathf.Abs(zone.Value.y + SavedHeightOffset - position.y) < 0.001f))
                    return zone.Key;
            return null;
        }

        private static VehicleController? FindNearestCruiser()
        {
            var player = Player.Local;
            if (player == null) return null;
            return UnityEngine.Object.FindObjectsOfType<VehicleController>()
                .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude)
                .FirstOrDefault();
        }

        private static VehicleController? FindCruiserNearPlayer()
        {
            var player = Player.Local;
            if (player == null) return null;

            // CruiserLoader's item zones span roughly x = -1.15..1.15 and z = -2.6..0.3.
            // Use a slightly larger envelope for a player standing on the cargo bed.
            foreach (var cruiser in UnityEngine.Object.FindObjectsOfType<VehicleController>()
                .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude))
            {
                Vector3 local = cruiser.transform.InverseTransformPoint(player.transform.position);
                if (local.x >= -2.2f && local.x <= 2.2f &&
                    local.y >= -2.0f && local.y <= 3.0f &&
                    local.z >= -4.0f && local.z <= 1.6f)
                    return cruiser;
            }
            return null;
        }

        internal static bool TryGetPlayerPosition(out VehicleController? cruiser, out Vector3 localPosition, out string? error)
        {
            cruiser = FindCruiserNearPlayer();
            localPosition = default;
            error = null;
            if (cruiser == null)
            {
                error = "Stand on the cruiser to use this command.";
                return false;
            }
            var player = Player.Local;
            if (player == null)
            {
                error = "Local player not ready yet.";
                return false;
            }
            localPosition = cruiser.transform.InverseTransformPoint(player.transform.position);
            return true;
        }

        internal static bool TryResolveItemKey(string? query, out string key, out string? error)
        {
            error = null;
            key = "";
            if (string.IsNullOrWhiteSpace(query))
            {
                var held = Player.Local?.currentlyHeldObjectServer as GrabbableObject;
                if (held == null)
                {
                    error = "Hold an item or provide its name.";
                    return false;
                }
                key = held.Name();
                return true;
            }

            string wanted = Extensions.NormalizeName(query);
            if (string.IsNullOrWhiteSpace(wanted))
            {
                error = "Missing item name.";
                return false;
            }

            // Resolve abbreviated and Korean inputs against types that are available in this game.
            var names = KnownItemKeys();

            if (names.Contains(wanted))
            {
                key = wanted;
                return true;
            }
            var matches = names.Where(n => n.Contains(wanted) || n.Replace("_", "").Contains(wanted.Replace("_", "")))
                .OrderBy(n => n).ToList();
            if (matches.Count == 1)
            {
                key = matches[0];
                return true;
            }
            if (matches.Count > 1)
            {
                error = "Ambiguous item name: " + string.Join(", ", matches.Take(8));
                return false;
            }

            // A not-yet-spawned modded item can still have a position saved for later.
            key = wanted;
            return true;
        }

        private static HashSet<string> KnownItemKeys()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var allItems = StartOfRound.Instance?.allItemsList?.itemsList;
            if (allItems != null)
                foreach (var item in allItems)
                    if (item != null) names.Add(item.Name());
            foreach (var item in UnityEngine.Object.FindObjectsOfType<GrabbableObject>())
                if (item != null && item.itemProperties != null) names.Add(item.Name());
            return names;
        }

        internal static bool IsExactAvailableItem(string query) =>
            KnownItemKeys().Contains(Extensions.NormalizeName(query));

        internal static bool TryStart(out string? error)
        {
            if (!CheckCruiserReady(out var cruiser, out error) || cruiser == null)
                return false;
            var rules = CruiserPositions.ListAll(out error);
            if (error != null) return false;
            if (rules.Count == 0)
            {
                error = "No cruiser positions saved. Use /css [item] [max] while on the cruiser.";
                return false;
            }
            return StartSort(cruiser, rules, dropMatchingHeld: false, out error);
        }

        internal static bool TryStartRule(VehicleController cruiser, string itemKey, Vector3 localPosition,
            int maximum, out string? error)
        {
            if (!CheckCruiserReady(cruiser, out error)) return false;
            var rules = new List<(string itemKey, Vector3 cruiserLocalPos, int maxCount)>
            {
                (itemKey, localPosition, maximum)
            };
            return StartSort(cruiser, rules, dropMatchingHeld: true, out error);
        }

        private static bool StartSort(VehicleController cruiser,
            List<(string itemKey, Vector3 cruiserLocalPos, int maxCount)> rules,
            bool dropMatchingHeld, out string? error)
        {
            if (Sorter.inProgress)
            {
                error = "Operation in progress.";
                return false;
            }
            if (!TryGetSortHost(out var host, out error) || host == null) return false;
            Sorter.inProgress = true;
            try
            {
                if (host.StartCoroutine(SortCruiser(cruiser, rules, dropMatchingHeld)) == null)
                {
                    Sorter.inProgress = false;
                    error = "Unable to start cruiser sort.";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Sorter.inProgress = false;
                error = "Unable to start cruiser sort: " + e.Message;
                QuickSort.Log.Error(error);
                return false;
            }
        }

        private static bool TryGetSortHost(out Sorter? host, out string? error)
        {
            host = null;
            error = null;
            try
            {
                if (Plugin.sorterObject == null)
                {
                    Plugin.sorterObject = new GameObject("PastaSorter");
                    UnityEngine.Object.DontDestroyOnLoad(Plugin.sorterObject);
                }
                host = Plugin.sorterObject.GetComponent<Sorter>();
                if (host == null)
                    host = Plugin.sorterObject.AddComponent<Sorter>();
                if (host == null || !host.isActiveAndEnabled)
                {
                    error = "Cruiser sorter is not active.";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = "Unable to initialize cruiser sorter: " + e.Message;
                QuickSort.Log.Error(error);
                return false;
            }
        }

        internal static bool CheckCruiserReady(out VehicleController? cruiser, out string? error)
        {
            cruiser = FindNearestCruiser();
            return CheckCruiserReady(cruiser, out error);
        }

        internal static bool CheckCruiserReady(VehicleController? cruiser, out string? error)
        {
            error = null;
            if (Sorter.inProgress)
            {
                error = "Operation in progress.";
                return false;
            }
            if (cruiser == null)
            {
                error = "Cruiser not found.";
                return false;
            }
            var cruiserNet = cruiser.GetComponent<NetworkObject>();
            if (cruiserNet == null || !cruiserNet.IsSpawned)
            {
                error = "Cruiser network object not ready yet.";
                return false;
            }
            return true;
        }

        private static bool Eligible(GrabbableObject item) =>
            item != null && item.itemProperties != null && item.NetworkObject != null &&
            item.NetworkObject.IsSpawned &&
            item.grabbable && !item.deactivated && !item.isHeld && !item.isPocketed;

        private static Transform PlacementParent(VehicleController cruiser)
        {
            // PlaceGrabbableObject redirects vehicle cargo to this physics transform.
            // Its RPC offset is local to that transform, not necessarily the vehicle root.
            var region = cruiser.GetComponentInChildren<PlayerPhysicsRegion>();
            return region != null && region.allowDroppingItems && region.physicsTransform != null
                ? region.physicsTransform : cruiser.transform;
        }

        private static int cargoParentCacheFrame = -1;
        private static Transform[] cargoParentCache = Array.Empty<Transform>();

        internal static bool IsCruiserCargo(GrabbableObject item)
        {
            if (item == null || item.transform == null) return false;
            if (item.GetComponentInParent<VehicleController>() != null) return true;

            // The vehicle's physics region can use a separate transform as the cargo
            // parent. Cache those transforms for this frame so a ship sort does not
            // search the scene once per item.
            if (cargoParentCacheFrame != Time.frameCount)
            {
                cargoParentCacheFrame = Time.frameCount;
                cargoParentCache = UnityEngine.Object.FindObjectsOfType<VehicleController>()
                    .Where(cruiser => cruiser != null)
                    .Select(PlacementParent)
                    .Distinct()
                    .ToArray();
            }
            foreach (var parent in cargoParentCache)
                if (parent != null && item.transform.IsChildOf(parent))
                    return true;
            return false;
        }

        private static Vector3 PlacementOffset(VehicleController cruiser, Transform parent,
            Vector3 cruiserLocalPosition) =>
            parent.InverseTransformPoint(cruiser.transform.TransformPoint(cruiserLocalPosition));

        private static List<GrabbableObject> CruiserCargoItems(VehicleController cruiser)
        {
            var parent = PlacementParent(cruiser);
            return cruiser.GetComponentsInChildren<GrabbableObject>()
                .Concat(parent.GetComponentsInChildren<GrabbableObject>())
                .Distinct().ToList();
        }

        private static Dictionary<string, int> ReservedZoneSlots(VehicleController cruiser,
            List<(string itemKey, Vector3 cruiserLocalPos, int maxCount)> movingRules)
        {
            var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
            var movingKeys = new HashSet<string>(movingRules.Select(r => r.itemKey), StringComparer.Ordinal);
            var savedRules = CruiserPositions.ListAll(out var error);
            if (error != null)
            {
                QuickSort.Log.Warning(error);
                return reserved;
            }
            var cargo = CruiserCargoItems(cruiser).Where(Eligible).ToList();
            foreach (var rule in savedRules)
            {
                if (movingKeys.Contains(rule.itemKey)) continue;
                string? zone = ZoneAt(rule.cruiserLocalPos);
                if (zone == null) continue;
                int count = cargo.Count(item => item.Name() == rule.itemKey);
                if (count > 0)
                    reserved[zone] = reserved.TryGetValue(zone, out int current) ? current + count : count;
            }
            return reserved;
        }

        private static bool IsShipCargo(GrabbableObject item, GameObject? ship)
        {
            if (ship == null || item.transform == null) return false;
            if (item.transform.IsChildOf(ship.transform)) return true;
            return item.transform.parent != null && item.transform.parent.name == "StorageCloset";
        }

        private static Vector3 Placement(Vector3 origin, int index)
        {
            // Cruiser piles use one X/Z point regardless of the ship's sort layout.
            // The configured step only controls vertical spacing between objects.
            // Rules saved before the +0.3m change used the shelf zone's base Y.
            // Raise those at placement time without changing the saved profile or
            // adding the offset twice to newly saved zone rules.
            string? zone = ZoneAt(origin);
            if (zone != null && Mathf.Abs(origin.y - Zones[zone].y) < 0.001f)
                origin.y += SavedHeightOffset;
            float step = Plugin.sorterObject?.GetComponent<Sorter>()?.sameTypeStackStepY?.Value ?? 0f;
            if (float.IsNaN(step) || float.IsInfinity(step)) step = 0f;
            return origin + new Vector3(0f, index * Mathf.Max(0f, step), 0f);
        }

        internal static bool IsShelfSlot(GrabbableObject item, Vector3 cruiserLocalPosition)
        {
            // The placement RPC has no QuickSort rule ID. Match the active CSS rule,
            // item type and one of its possible stack heights before pinning on peers.
            // A synced host profile supplies these same rules and stack settings.
            if (item == null || item.itemProperties == null) return false;
            string itemKey = item.Name();
            var rules = CruiserPositions.ListAll(out var error);
            if (error != null)
            {
                QuickSort.Log.Warning(error);
                return false;
            }
            float step = Plugin.sorterObject?.GetComponent<Sorter>()?.sameTypeStackStepY?.Value ?? 0f;
            if (float.IsNaN(step) || float.IsInfinity(step)) step = 0f;
            step = Mathf.Max(0f, step);
            const float coordinateTolerance = 0.03f;
            foreach (var rule in rules)
            {
                if (rule.itemKey != itemKey ||
                    Mathf.Abs(rule.cruiserLocalPos.x - cruiserLocalPosition.x) >= coordinateTolerance ||
                    Mathf.Abs(rule.cruiserLocalPos.z - cruiserLocalPosition.z) >= coordinateTolerance)
                    continue;

                string? zone = ZoneAt(rule.cruiserLocalPos);
                float baseY = rule.cruiserLocalPos.y;
                if (zone != null && Mathf.Abs(baseY - Zones[zone].y) < 0.001f)
                    baseY += SavedHeightOffset;
                int slots = zone == null ? rule.maxCount : ZoneCapacity(zone);
                if (slots <= 0) continue;
                float deltaY = cruiserLocalPosition.y - baseY;
                if (step <= 0f)
                {
                    if (Mathf.Abs(deltaY) < coordinateTolerance) return true;
                    continue;
                }
                float slot = deltaY / step;
                // For a named zone, other CSS item types can occupy earlier shared
                // slots, so use the zone capacity rather than this type's maximum.
                if (slot < -coordinateTolerance / step ||
                    slot > slots - 1 + coordinateTolerance / step)
                    continue;
                int nearestSlot = Mathf.RoundToInt(slot);
                if (nearestSlot >= 0 && nearestSlot < slots &&
                    Mathf.Abs(deltaY - nearestSlot * step) < coordinateTolerance)
                    return true;
            }
            return false;
        }

        private static bool PlaceOnCruiser(GrabbableObject item, VehicleController cruiser,
            Vector3 localPosition)
        {
            var player = Player.Local;
            var cruiserNet = cruiser.GetComponent<NetworkObject>();
            if (player == null || cruiserNet == null || !cruiserNet.IsSpawned || !Eligible(item))
                return false;

            Transform placementParent = PlacementParent(cruiser);
            Vector3 placementLocal = PlacementOffset(cruiser, placementParent, localPosition);

            Transform oldParent = item.transform.parent;
            Vector3 oldWorldPosition = item.transform.position;
            Quaternion oldWorldRotation = item.transform.rotation;
            float oldFallTime = item.fallTime;
            bool oldReachedFloorTarget = item.reachedFloorTarget;
            bool oldHasHitGround = item.hasHitGround;
            Vector3 oldTargetFloorPosition = item.targetFloorPosition;
            Vector3 oldStartFallingPosition = item.startFallingPosition;
            try
            {
                // The RPC parent is the vehicle NetworkObject. The game redirects placement
                // to its physics transform, so the offset must be local to that transform.
                item.transform.SetParent(placementParent, worldPositionStays: true);
                item.transform.localPosition = placementLocal;
                Vector3 resting = item.itemProperties.restingRotation;
                item.transform.rotation = cruiser.transform.rotation *
                    Quaternion.Euler(resting.x, 0f, resting.z);
                item.fallTime = 1.1f;
                item.reachedFloorTarget = true;
                item.hasHitGround = true;
                item.targetFloorPosition = placementLocal;
                item.startFallingPosition = placementLocal;
                // Direct placement avoids the vanilla fall animation sweeping the item
                // through other cargo and moving it away from its saved X/Z point.
                player.PlaceObjectServerRpc(item.NetworkObject, cruiserNet, placementLocal, true);
            }
            catch (Exception e)
            {
                // If the RPC path fails, put the item back instead of leaving it visually missing.
                item.transform.SetParent(oldParent, worldPositionStays: true);
                item.transform.position = oldWorldPosition;
                item.transform.rotation = oldWorldRotation;
                item.fallTime = oldFallTime;
                item.reachedFloorTarget = oldReachedFloorTarget;
                item.hasHitGround = oldHasHitGround;
                item.targetFloorPosition = oldTargetFloorPosition;
                item.startFallingPosition = oldStartFallingPosition;
                QuickSort.Log.Warning($"Failed to place '{item.Name()}' on cruiser: {e.Message}");
                return false;
            }

            // The RPC has already been sent. A local physics sync failure must not undo the
            // network placement and leave client and host disagreeing about this item.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                try
                {
                    var body = item.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.position = placementParent.TransformPoint(placementLocal);
                        body.velocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                    Physics.SyncTransforms();
                }
                catch (Exception e)
                {
                    QuickSort.Log.Warning($"Cruiser physics sync failed for '{item.Name()}': {e.Message}");
                }
            }
            CruiserShelfAnchor.Attach(item, placementParent, cruiser.transform);
            return true;
        }

        private static IEnumerator SortCruiser(VehicleController cruiser,
            List<(string itemKey, Vector3 cruiserLocalPos, int maxCount)> rules, bool dropMatchingHeld)
        {
            Sorter.inProgress = true;
            int placed = 0;
            bool shelfCapacityReached = false;
            try
            {
                // A shelf zone has one sequence of slots shared by all configured types.
                // For /css, preserve slots occupied by other saved types already on board.
                var zoneSlots = ReservedZoneSlots(cruiser, rules);
                GrabbableObject? droppedHeld = null;
                int heldSlot = -1;
                if (dropMatchingHeld && rules.Count == 1 && cruiser != null)
                {
                    var rule = rules[0];
                    var player = Player.Local;
                    var held = player?.currentlyHeldObjectServer as GrabbableObject;
                    int onCruiser = CruiserCargoItems(cruiser)
                        .Count(item => Eligible(item) && item.Name() == rule.itemKey);
                    string? heldZone = ZoneAt(rule.cruiserLocalPos);
                    int zoneStart = heldZone != null && zoneSlots.TryGetValue(heldZone, out int count)
                        ? count : 0;
                    if (heldZone != null && held != null && held.Name() == rule.itemKey &&
                        onCruiser < rule.maxCount && zoneStart + onCruiser >= ZoneCapacity(heldZone))
                        shelfCapacityReached = true;
                    if (player != null && held != null && held.Name() == rule.itemKey &&
                        onCruiser < rule.maxCount &&
                        (heldZone == null || zoneStart + onCruiser < ZoneCapacity(heldZone)))
                    {
                        var cruiserNet = cruiser.GetComponent<NetworkObject>();
                        if (cruiserNet != null && cruiserNet.IsSpawned && held.NetworkObject != null &&
                            held.NetworkObject.IsSpawned)
                        {
                            heldSlot = zoneStart + onCruiser;
                            bool dropStarted = false;
                            try
                            {
                                using (Sorter.BeginInteractionBypass())
                                {
                                    held.floorYRot = -1;
                                    Vector3 target = Placement(rule.cruiserLocalPos, heldSlot);
                                    Transform parent = PlacementParent(cruiser);
                                    player.DiscardHeldObject(true, cruiserNet,
                                        PlacementOffset(cruiser, parent, target), true);
                                }
                                dropStarted = true;
                            }
                            catch (Exception e)
                            {
                                QuickSort.Log.Warning($"Failed to drop '{rule.itemKey}' on cruiser: {e.Message}");
                            }
                            if (dropStarted)
                            {
                                // Give the drop RPC a frame to clear the held slot and set its parent.
                                yield return null;
                                int frames = 0;
                                while (frames++ < 30 && player != null &&
                                    player.currentlyHeldObjectServer == held)
                                    yield return null;
                                if (player != null && player.currentlyHeldObjectServer != held)
                                {
                                    // Reserve its slot while the drop RPC completes so later cargo
                                    // cannot push the cruiser over the configured maximum.
                                    droppedHeld = held;
                                    int confirmFrames = 0;
                                    while (held != null && cruiser != null &&
                                        (held.isHeld ||
                                            (!held.transform.IsChildOf(PlacementParent(cruiser)) && !Eligible(held))) &&
                                        confirmFrames++ < 30)
                                        yield return null;
                                    if (held != null && cruiser != null &&
                                        !held.isHeld && held.transform.IsChildOf(PlacementParent(cruiser)))
                                    {
                                        Transform cargoParent = PlacementParent(cruiser);
                                        Vector3 targetLocal = PlacementOffset(cruiser, cargoParent,
                                            Placement(rule.cruiserLocalPos, heldSlot));
                                        held.transform.localPosition = targetLocal;
                                        held.targetFloorPosition = targetLocal;
                                        held.startFallingPosition = targetLocal;
                                        held.fallTime = 1.1f;
                                        held.reachedFloorTarget = true;
                                        held.hasHitGround = true;
                                        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
                                        {
                                            try
                                            {
                                                var body = held.GetComponent<Rigidbody>();
                                                if (body != null)
                                                {
                                                    body.position = cargoParent.TransformPoint(targetLocal);
                                                    body.velocity = Vector3.zero;
                                                    body.angularVelocity = Vector3.zero;
                                                }
                                                Physics.SyncTransforms();
                                            }
                                            catch (Exception e)
                                            {
                                                QuickSort.Log.Warning($"Cruiser physics sync failed for held '{rule.itemKey}': {e.Message}");
                                            }
                                        }
                                        CruiserShelfAnchor.Attach(held, cargoParent, cruiser.transform);
                                        placed++;
                                    }
                                    else if (held != null && cruiser != null && Eligible(held) &&
                                        PlaceOnCruiser(held, cruiser,
                                            Placement(rule.cruiserLocalPos, heldSlot)))
                                        placed++;
                                    else
                                        QuickSort.Log.Warning($"Could not confirm cruiser placement for held '{rule.itemKey}'.");
                                }
                                else
                                {
                                    QuickSort.Log.Warning($"Held '{rule.itemKey}' was not released onto the cruiser.");
                                }
                            }
                        }
                    }
                }

                if (!Sorter.inProgress || cruiser == null || Player.Local == null)
                    yield break;
                GameObject? ship = GameObject.Find("Environment/HangarShip");
                var cruiserItems = CruiserCargoItems(cruiser)
                    .Where(item => item != droppedHeld && Eligible(item)).ToList();
                var shipItems = UnityEngine.Object.FindObjectsOfType<GrabbableObject>()
                    .Where(item => item != droppedHeld && Eligible(item) &&
                        !IsCruiserCargo(item) &&
                        IsShipCargo(item, ship))
                    .ToList();

                foreach (var rule in rules)
                {
                    if (!Sorter.inProgress || cruiser == null || Player.Local == null ||
                        (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                        yield break;

                    int reserved = droppedHeld != null && droppedHeld.Name() == rule.itemKey ? 1 : 0;
                    string? zone = ZoneAt(rule.cruiserLocalPos);
                    int startSlot = zone != null && zoneSlots.TryGetValue(zone, out int next) ? next : 0;
                    int allowed = rule.maxCount - reserved;
                    if (zone != null)
                    {
                        int room = Math.Max(0, ZoneCapacity(zone) - startSlot - reserved);
                        if (allowed > room)
                        {
                            allowed = room;
                            shelfCapacityReached = true;
                        }
                    }
                    var already = cruiserItems.Where(item => item.Name() == rule.itemKey)
                        .Take(allowed).ToList();
                    int needed = allowed - already.Count;
                    var incoming = shipItems.Where(item => item.Name() == rule.itemKey).Take(needed).ToList();
                    var selected = already.Concat(incoming).ToList();
                    for (int index = 0; index < selected.Count; index++)
                    {
                        if (!Sorter.inProgress || cruiser == null || Player.Local == null ||
                            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                            yield break;
                        var item = selected[index];
                        int slot = startSlot + index;
                        if (reserved > 0 && slot >= heldSlot) slot++;
                        if (Eligible(item) && PlaceOnCruiser(item, cruiser,
                            Placement(rule.cruiserLocalPos, slot)))
                            placed++;
                        yield return null;
                    }
                    if (zone != null) zoneSlots[zone] = startSlot + selected.Count + reserved;
                }
            }
            finally
            {
                Sorter.inProgress = false;
                CommandChat.Print($"Cruiser sort finished: placed {placed} item(s)." +
                    (shelfCapacityReached ?
                        $" Shelf zones are limited to {MaxShelfZoneSlots} items each (D2: 1)." : ""));
            }
        }
    }

    public sealed class CruiserSetCommand : QuickSortCommand
    {
        public override string Name => "css";
        public override string Description =>
            "Save a cruiser position, then immediately place that item type up to its maximum.\n" +
            "Usage: /css [item] [max=10], /css <zone> [item] [max=10], " +
            "/css zones, /css list, /css reset [item]";

        protected override bool InvokeParsed(string[] args, out string? error)
        {
            error = null;
            if (args.Length > 0 && string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
            {
                CommandChat.Print(Description);
                return true;
            }
            if (args.Length > 0 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
            {
                var rules = CruiserPositions.ListAll(out error);
                if (error != null) return false;
                CommandChat.Print(rules.Count == 0 ? "No cruiser positions saved." :
                    string.Join(", ", rules.Select(r =>
                    {
                        string location = CruiserSorter.ZoneAt(r.cruiserLocalPos) ??
                            $"({r.cruiserLocalPos.x:F1},{r.cruiserLocalPos.y:F1},{r.cruiserLocalPos.z:F1})";
                        return $"{r.itemKey}={r.maxCount}@{location}";
                    })));
                return true;
            }
            if (args.Length > 0 && string.Equals(args[0], "zones", StringComparison.OrdinalIgnoreCase))
            {
                CommandChat.Print("Cruiser zones: A1-A3, B1-B3, C1-C3, D1-D3, E1-E3, F1-F3, G1-G3. " +
                    "Side shelves: 1=bottom, 2=middle, 3=top. Use /css A2 [item] [max].");
                return true;
            }
            if (args.Length > 0 && string.Equals(args[0], "reset", StringComparison.OrdinalIgnoreCase))
            {
                string? raw = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;
                if (!CruiserSorter.TryResolveItemKey(raw, out string key, out error)) return false;
                if (!CruiserPositions.Remove(key, out bool removed, out error)) return false;
                CommandChat.Print(removed ? $"Removed cruiser position for {key}." : $"No cruiser position for {key}.");
                return true;
            }

            string? zoneName = null;
            Vector3 zonePosition = default;
            string[] placementArgs = args;
            if (args.Length > 0 && (string.Equals(args[0], "shelf", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[0], "zone", StringComparison.OrdinalIgnoreCase)))
            {
                if (args.Length < 2 || !CruiserSorter.TryGetZone(args[1], out zoneName, out zonePosition))
                {
                    error = "Usage: /css shelf <A1-G3 zone> [item name] [max=10]. Use /css zones to list zones.";
                    return false;
                }
                placementArgs = args.Skip(2).ToArray();
            }
            else if (args.Length > 0 &&
                CruiserSorter.TryGetZone(args[0], out string firstZone, out Vector3 firstPosition))
            {
                zoneName = firstZone;
                zonePosition = firstPosition;
                placementArgs = args.Skip(1).ToArray();
            }
            else if (args.Length > 0 &&
                !CruiserSorter.IsExactAvailableItem(string.Join(" ", args)) &&
                CruiserSorter.TryGetZone(args[args.Length - 1], out string trailingZone, out Vector3 trailingPosition))
            {
                zoneName = trailingZone;
                zonePosition = trailingPosition;
                placementArgs = args.Take(args.Length - 1).ToArray();
            }

            if (!CruiserSorter.TryGetPlayerPosition(out var cruiser, out Vector3 localPosition, out error) ||
                cruiser == null)
                return false;
            if (zoneName != null) localPosition = zonePosition;
            // Save a little above the player-selected point or CruiserLoader shelf
            // coordinate, then preserve this exact base point on later /cs runs.
            localPosition += Vector3.up * CruiserSorter.SavedHeightOffset;
            int maximum = 10;
            string[] nameArgs = placementArgs;
            // A real item name may end with a number (e.g. "Wet Note 1"). Prefer an
            // exact available item name; users can append another number for its max.
            if (placementArgs.Length > 0 &&
                !CruiserSorter.IsExactAvailableItem(string.Join(" ", placementArgs)) &&
                int.TryParse(placementArgs[placementArgs.Length - 1], out int parsed))
            {
                maximum = parsed;
                nameArgs = placementArgs.Take(placementArgs.Length - 1).ToArray();
            }
            if (maximum <= 0 || maximum > 10000)
            {
                error = "Maximum must be between 1 and 10000.";
                return false;
            }
            string? query = nameArgs.Length > 0 ? string.Join(" ", nameArgs) : null;
            if (!CruiserSorter.TryResolveItemKey(query, out string itemKey, out error)) return false;
            if (!CruiserSorter.CheckCruiserReady(cruiser, out error)) return false;
            if (!CruiserPositions.Set(itemKey, localPosition, maximum, out error)) return false;
            if (!CruiserSorter.TryStartRule(cruiser, itemKey, localPosition, maximum, out error))
            {
                QuickSort.Log.Warning($"Cruiser position for '{itemKey}' was saved, but placement could not start: {error}");
                return false;
            }
            CommandChat.Print($"Cruiser position saved: {itemKey}, max {maximum}" +
                (zoneName == null ? "." : $" at {zoneName}.") + " Placing items now.");
            return true;
        }
    }

    public sealed class CruiserSortCommand : QuickSortCommand
    {
        public override string Name => "csort";
        public override string[] Commands => new[] { "csort", "cs" };
        public override string Description => "Sort cruiser cargo up to each saved maximum. Usage: /csort or /cs.";

        protected override bool InvokeParsed(string[] args, out string? error)
        {
            // This command has no options. Some chat command versions still forward
            // a token for a bare invocation, so argument count must not block sorting.
            return CruiserSorter.TryStart(out error);
        }
    }
}
