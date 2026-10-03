using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using CommandChat = ChatCommandAPI.Utils.Chat;

namespace QuickSort
{
    /// <summary>
    /// Named snapshots of the settings used by ship and cruiser sorting. A snapshot is
    /// deliberately explicit: edits to positions or the skip list are saved into a
    /// profile only when the player runs /profile save again.
    /// </summary>
    internal static class SortProfiles
    {
        private sealed class ProfilesFile
        {
            public string activeProfile = "";
            public Dictionary<string, ProfileData> profiles = new Dictionary<string, ProfileData>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ProfileData
        {
            [JsonProperty(Required = Required.Always)] public LayoutSettings layout = null!;
            [JsonProperty(Required = Required.Always)] public List<ShipPosition> shipPositions = null!;
            [JsonProperty(Required = Required.Always)] public List<CruiserPosition> cruiserPositions = null!;
        }

        private sealed class HostRecoveryFile
        {
            [JsonProperty(Required = Required.Always)] public ProfileData personalSettings = null!;
            [JsonProperty(Required = Required.Always)] public string activeProfile = "";
        }

        private sealed class LayoutSettings
        {
            [JsonProperty(Required = Required.Always)] public string skippedItems = "";
            [JsonProperty(Required = Required.Always)] public float sortOriginX;
            [JsonProperty(Required = Required.Always)] public float sortOriginY;
            [JsonProperty(Required = Required.Always)] public float sortOriginZ;
            [JsonProperty(Required = Required.Always)] public float itemSpacing;
            [JsonProperty(Required = Required.Always)] public float rowSpacing;
            [JsonProperty(Required = Required.Always)] public int itemsPerRow;
            [JsonProperty(Required = Required.Always)] public float sortAreaWidth;
            [JsonProperty(Required = Required.Always)] public float sortAreaDepth;
            [JsonProperty(Required = Required.Always)] public float wallPadding;
            [JsonProperty(Required = Required.Always)] public bool stackSameTypeTogether;
            [JsonProperty(Required = Required.Always)] public float sameTypeStackStepY;
        }

        private sealed class ShipPosition
        {
            [JsonProperty(Required = Required.Always)] public string item = "";
            [JsonProperty(Required = Required.Always)] public float x;
            [JsonProperty(Required = Required.Always)] public float y;
            [JsonProperty(Required = Required.Always)] public float z;
        }

        private sealed class CruiserPosition
        {
            [JsonProperty(Required = Required.Always)] public string item = "";
            [JsonProperty(Required = Required.Always)] public float x;
            [JsonProperty(Required = Required.Always)] public float y;
            [JsonProperty(Required = Required.Always)] public float z;
            [JsonProperty(Required = Required.Always)] public int maxCount;
        }

        private static readonly object Gate = new object();
        private const string DefaultName = "default";
        private const string HostName = "host";
        // The host snapshot is deliberately kept out of the profiles file. Applying it
        // changes the live config/position files, so keep a complete local snapshot to
        // restore when this temporary selection ends.
        private static ProfileData? hostProfile;
        private static ProfileData? beforeHostSelection;
        private static bool hostSelected;
        private static bool personalSelectionMadeInLobby;
        private static bool pendingHostApply;
        private static bool hostSelectedManually;
        private static bool pendingHostRestore;

        internal static string ProfilesPath => Path.Combine(Paths.ConfigPath, "pasta.quicksort.profiles.json");
        internal static string HostRecoveryPath => Path.Combine(Paths.ConfigPath, "pasta.quicksort.host-recovery.json");

        // The live config and position JSON files are the sorter's working state. Before
        // a temporary host profile writes them, durably save the personal state so a
        // crash (or an unexpected Plugin component destruction) can be recovered.
        private static bool WriteHostRecovery(ProfileData personal, out string? error)
        {
            error = null;
            if (!ValidateProfile(personal, out error)) return false;
            if (!TryLoad(out var profiles, out error)) return false;
            string path = HostRecoveryPath;
            string temp = path + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                var recovery = new HostRecoveryFile
                {
                    personalSettings = personal,
                    activeProfile = profiles.activeProfile
                };
                File.WriteAllText(temp, JsonConvert.SerializeObject(recovery, Formatting.Indented), Encoding.UTF8);
                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true); }
                    catch
                    {
                        File.Copy(temp, path, overwrite: true);
                        File.Delete(temp);
                    }
                }
                else File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                error = $"Could not save temporary host recovery data: {e.Message}";
                return false;
            }
        }

        private static void ClearHostRecovery()
        {
            try
            {
                if (File.Exists(HostRecoveryPath + ".bak")) File.Delete(HostRecoveryPath + ".bak");
                if (File.Exists(HostRecoveryPath + ".tmp")) File.Delete(HostRecoveryPath + ".tmp");
                if (File.Exists(HostRecoveryPath)) File.Delete(HostRecoveryPath);
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Could not remove host recovery file: {e.Message}");
            }
        }

        private static void FinishHostSelectionWithPersonal(ProfileData selected)
        {
            if (!hostSelected) return;
            // If deletion itself fails, a stale recovery file must describe the
            // newly selected personal state rather than the former one.
            if (!WriteHostRecovery(selected, out var error))
                QuickSort.Log.Warning(error ?? "Could not update host recovery data.");
            ClearHostRecovery();
        }

        internal static void RecoverInterruptedHostSession()
        {
            lock (Gate)
            {
                if (!File.Exists(HostRecoveryPath) && !File.Exists(HostRecoveryPath + ".bak")) return;
                try
                {
                    HostRecoveryFile? recovery = null;
                    foreach (string path in new[] { HostRecoveryPath, HostRecoveryPath + ".bak" })
                    {
                        if (!File.Exists(path)) continue;
                        try
                        {
                            var candidate = JsonConvert.DeserializeObject<HostRecoveryFile>(
                                File.ReadAllText(path, Encoding.UTF8));
                            if (candidate != null && ValidateProfile(candidate.personalSettings, out _))
                            {
                                recovery = candidate;
                                break;
                            }
                        }
                        catch { /* Try the previous atomic backup. */ }
                    }
                    if (recovery == null)
                    {
                        QuickSort.Log.Warning("Host recovery data is invalid.");
                        return;
                    }
                    ProfileData restore = recovery.personalSettings;
                    if (TryLoad(out var profiles, out _) &&
                        !string.Equals(profiles.activeProfile, recovery.activeProfile,
                            StringComparison.OrdinalIgnoreCase) &&
                        profiles.profiles.TryGetValue(profiles.activeProfile, out var changedProfile) &&
                        ValidateProfile(changedProfile, out _))
                        restore = changedProfile;
                    if (!ApplyCurrent(restore, out var error))
                    {
                        QuickSort.Log.Warning($"Could not recover personal settings: {error}");
                        return;
                    }
                    ClearHostRecovery();
                    QuickSort.Log.Info("Recovered personal settings after an interrupted host profile session.");
                }
                catch (Exception e)
                {
                    QuickSort.Log.Warning($"Could not recover personal settings: {e.Message}");
                }
            }
        }

        internal static void EnsureFileExists()
        {
            lock (Gate)
            {
                if (File.Exists(ProfilesPath)) return;

                if (!SaveFile(new ProfilesFile(), out var error))
                    QuickSort.Log.Warning(error ?? "Could not create profiles file.");
            }
        }

        internal static void EnsureDefaultProfile()
        {
            lock (Gate)
            {
                if (!TryLoad(out var file, out var error))
                {
                    QuickSort.Log.Warning(error ?? "Could not read profiles file.");
                    return;
                }
                bool changed = false;
                if (!file.profiles.ContainsKey(DefaultName))
                {
                    if (!CaptureCurrent(out var baseline, out error) ||
                        !ValidateProfile(baseline, out error))
                    {
                        QuickSort.Log.Warning(error ?? "Could not capture default profile.");
                        return;
                    }
                    file.profiles.Add(DefaultName, baseline);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(file.activeProfile) ||
                    !file.profiles.ContainsKey(file.activeProfile))
                {
                    file.activeProfile = DefaultName;
                    changed = true;
                }
                if (changed && !SaveFile(file, out error))
                    QuickSort.Log.Warning(error ?? "Could not save default profile.");
            }
        }

        private static bool TryValidateName(string? raw, out string name, out string? error)
        {
            name = (raw ?? "").Trim();
            error = null;
            if (name.Length == 0 || name.Length > 64 || name.Any(char.IsControl))
            {
                error = "Profile name must contain 1–64 characters and no control characters.";
                return false;
            }
            return true;
        }

        private static bool TryLoad(out ProfilesFile file, out string? error)
        {
            file = new ProfilesFile();
            error = null;

            if (!File.Exists(ProfilesPath))
            {
                if (!SaveFile(file, out error)) return false;
                return true;
            }

            try
            {
                string json = File.ReadAllText(ProfilesPath, Encoding.UTF8);
                file = JsonConvert.DeserializeObject<ProfilesFile>(json) ?? new ProfilesFile();
                var profiles = new Dictionary<string, ProfileData>(StringComparer.OrdinalIgnoreCase);
                if (file.profiles != null)
                {
                    foreach (var entry in file.profiles)
                    {
                        if (!TryValidateName(entry.Key, out var name, out _) || entry.Value == null)
                        {
                            error = "Profiles JSON contains an invalid profile entry.";
                            return false;
                        }
                        if (profiles.ContainsKey(name))
                        {
                            error = $"Profiles JSON contains duplicate profile name '{name}'.";
                            return false;
                        }
                        profiles.Add(name, entry.Value);
                    }
                }
                file.profiles = profiles;
                file.activeProfile ??= "";
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to read profiles JSON: {e.Message}";
                return false;
            }
        }

        private static bool SaveFile(ProfilesFile file, out string? error)
        {
            error = null;
            try
            {
                string path = ProfilesPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                string json = JsonConvert.SerializeObject(file, Formatting.Indented);
                string temp = path + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                    }
                    catch
                    {
                        File.Copy(temp, path, overwrite: true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save profiles JSON: {e.Message}";
                return false;
            }
        }

        private static bool TryGetSorter(out Sorter sorter, out string? error)
        {
            sorter = null!;
            error = null;
            if (Plugin.sorterObject != null)
                sorter = Plugin.sorterObject.GetComponent<Sorter>();
            if (sorter == null)
            {
                error = "Sorter is not initialized yet.";
                return false;
            }
            return true;
        }

        private static bool CaptureCurrent(out ProfileData data, out string? error)
        {
            data = new ProfileData();
            if (!TryGetSorter(out var sorter, out error)) return false;

            var shipPositions = SortPositions.ListAll(out error);
            if (error != null) return false;
            var cruiserPositions = CruiserPositions.ListAll(out error);
            if (error != null) return false;

            data.layout = new LayoutSettings
            {
                skippedItems = sorter.skippedItems.Value,
                sortOriginX = sorter.sortOriginX.Value,
                sortOriginY = sorter.sortOriginY.Value,
                sortOriginZ = sorter.sortOriginZ.Value,
                itemSpacing = sorter.itemSpacing.Value,
                rowSpacing = sorter.rowSpacing.Value,
                itemsPerRow = sorter.itemsPerRow.Value,
                sortAreaWidth = sorter.sortAreaWidth.Value,
                sortAreaDepth = sorter.sortAreaDepth.Value,
                wallPadding = sorter.wallPadding.Value,
                stackSameTypeTogether = sorter.stackSameTypeTogether.Value,
                sameTypeStackStepY = sorter.sameTypeStackStepY.Value
            };
            data.shipPositions = shipPositions.Select(p => new ShipPosition
            {
                item = p.itemKey, x = p.shipLocalPos.x, y = p.shipLocalPos.y, z = p.shipLocalPos.z
            }).ToList();
            data.cruiserPositions = cruiserPositions.Select(p => new CruiserPosition
            {
                item = p.itemKey, x = p.cruiserLocalPos.x, y = p.cruiserLocalPos.y, z = p.cruiserLocalPos.z,
                maxCount = p.maxCount
            }).ToList();
            return true;
        }

        private static bool ValidateProfile(ProfileData data, out string? error)
        {
            error = null;
            if (data.layout == null || data.shipPositions == null || data.cruiserPositions == null)
            {
                error = "Profile is missing settings or positions.";
                return false;
            }
            var shipKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var position in data.shipPositions)
            {
                string key = Extensions.NormalizeName(position?.item ?? "");
                if (position == null || string.IsNullOrWhiteSpace(key) || !shipKeys.Add(key) ||
                    !IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
                {
                    error = "Profile contains an invalid or duplicate ship position.";
                    return false;
                }
            }

            var cruiserKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var position in data.cruiserPositions)
            {
                string key = Extensions.NormalizeName(position?.item ?? "");
                if (position == null || string.IsNullOrWhiteSpace(key) || !cruiserKeys.Add(key) ||
                    !IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                    position.maxCount < 1 || position.maxCount > 10000)
                {
                    error = "Profile contains an invalid or duplicate cruiser position or maximum.";
                    return false;
                }
            }

            var layout = data.layout;
            if (!IsFinite(layout.sortOriginX) || !IsFinite(layout.sortOriginY) || !IsFinite(layout.sortOriginZ) ||
                !IsFinite(layout.itemSpacing) || !IsFinite(layout.rowSpacing) ||
                !IsFinite(layout.sortAreaWidth) || !IsFinite(layout.sortAreaDepth) ||
                !IsFinite(layout.wallPadding) || !IsFinite(layout.sameTypeStackStepY))
            {
                error = "Profile contains a non-finite layout setting.";
                return false;
            }
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ApplyCurrent(ProfileData data, out string? error)
        {
            if (!ValidateProfile(data, out error)) return false;
            if (!TryGetSorter(out var sorter, out error)) return false;

            var ship = data.shipPositions.Select(p =>
                (itemKey: p.item, shipLocalPos: new Vector3(p.x, p.y, p.z)));
            if (!SortPositions.ReplaceAll(ship, out error)) return false;

            var cruiser = data.cruiserPositions.Select(p =>
                (itemKey: p.item, cruiserLocalPos: new Vector3(p.x, p.y, p.z), maxCount: p.maxCount));
            if (!CruiserPositions.ReplaceAll(cruiser, out error)) return false;

            var layout = data.layout;
            try
            {
                sorter.skippedItems.Value = layout.skippedItems ?? "";
                sorter.sortOriginX.Value = layout.sortOriginX;
                sorter.sortOriginY.Value = layout.sortOriginY;
                sorter.sortOriginZ.Value = layout.sortOriginZ;
                sorter.itemSpacing.Value = layout.itemSpacing;
                sorter.rowSpacing.Value = layout.rowSpacing;
                sorter.itemsPerRow.Value = layout.itemsPerRow;
                sorter.sortAreaWidth.Value = layout.sortAreaWidth;
                sorter.sortAreaDepth.Value = layout.sortAreaDepth;
                sorter.wallPadding.Value = layout.wallPadding;
                sorter.stackSameTypeTogether.Value = layout.stackSameTypeTogether;
                sorter.sameTypeStackStepY.Value = layout.sameTypeStackStepY;
                Plugin.config.Save();
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to apply profile settings: {e.Message}";
                return false;
            }
        }

        internal static bool TryCaptureNetworkSnapshot(out string json, out string? error)
        {
            json = "";
            lock (Gate)
            {
                if (!CaptureCurrent(out var data, out error) || !ValidateProfile(data, out error))
                    return false;
                json = JsonConvert.SerializeObject(data);
                if (Encoding.UTF8.GetByteCount(json) > 65536)
                {
                    error = "Host profile is too large to sync (64 KiB limit).";
                    json = "";
                    return false;
                }
                return true;
            }
        }

        internal static bool ReceiveHostSnapshot(string json)
        {
            ProfileData? received;
            try
            {
                if (Encoding.UTF8.GetByteCount(json) > 65536) return false;
                received = JsonConvert.DeserializeObject<ProfileData>(json);
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Ignored invalid host profile: {e.Message}");
                return false;
            }
            string? validationError = null;
            if (received == null || !ValidateProfile(received, out validationError))
            {
                QuickSort.Log.Warning($"Ignored invalid host profile: {validationError}");
                return false;
            }

            lock (Gate)
            {
                hostProfile = received;
                pendingHostApply = !personalSelectionMadeInLobby &&
                    (hostSelectedManually || (Plugin.SyncHostProfile != null && Plugin.SyncHostProfile.Value));
            }
            ApplyPendingHost();
            return true;
        }

        internal static void OnSyncHostProfileChanged()
        {
            lock (Gate)
            {
                if (hostProfile == null) return;
                if (Plugin.SyncHostProfile != null && Plugin.SyncHostProfile.Value)
                {
                    pendingHostRestore = false;
                    if (!personalSelectionMadeInLobby && !hostSelected)
                        pendingHostApply = true;
                }
                else
                {
                    pendingHostApply = false;
                    if (hostSelected && !hostSelectedManually)
                        pendingHostRestore = true;
                }
            }
        }

        internal static void ApplyPendingHost()
        {
            lock (Gate)
            {
                if (pendingHostRestore && !Sorter.inProgress)
                {
                    pendingHostRestore = false;
                    if (hostSelected && beforeHostSelection != null)
                    {
                        if (ApplyCurrent(beforeHostSelection, out var restoreError))
                        {
                            hostSelected = false;
                            hostSelectedManually = false;
                            beforeHostSelection = null;
                            ClearHostRecovery();
                        }
                        else QuickSort.Log.Warning($"Could not restore personal profile: {restoreError}");
                    }
                }
                if (!pendingHostApply || hostProfile == null) return;
                if (personalSelectionMadeInLobby)
                {
                    pendingHostApply = false;
                    return;
                }
                if (Sorter.inProgress) return;
                pendingHostApply = false;
                if (!hostSelected)
                {
                    if (File.Exists(HostRecoveryPath))
                    {
                        RecoverInterruptedHostSession();
                        if (File.Exists(HostRecoveryPath))
                        {
                            QuickSort.Log.Warning("Previous host profile recovery is pending; host profile was not applied.");
                            return;
                        }
                    }
                    if (!CaptureCurrent(out var before, out var captureError))
                    {
                        QuickSort.Log.Warning(captureError ?? "Could not back up personal profile.");
                        return;
                    }
                    if (!WriteHostRecovery(before, out captureError))
                    {
                        QuickSort.Log.Warning(captureError ?? "Could not back up personal profile.");
                        return;
                    }
                    beforeHostSelection = before;
                }
                if (!ApplyCurrent(hostProfile, out var applyError))
                {
                    QuickSort.Log.Warning(applyError ?? "Could not apply host profile.");
                    bool restored = false;
                    if (!hostSelected && beforeHostSelection != null)
                    {
                        restored = ApplyCurrent(beforeHostSelection, out var restoreError);
                        if (!restored) QuickSort.Log.Warning($"Could not restore personal settings: {restoreError}");
                    }
                    if (!hostSelected)
                    {
                        beforeHostSelection = null;
                        // Leave the durable backup if rollback failed; startup can retry.
                        if (restored) ClearHostRecovery();
                    }
                    return;
                }
                hostSelected = true;
                QuickSort.Log.Info("Temporary host profile selected.");
            }
        }

        internal static void EndHostSession()
        {
            lock (Gate)
            {
                if (hostSelected && beforeHostSelection != null)
                {
                    if (ApplyCurrent(beforeHostSelection, out var error)) ClearHostRecovery();
                    else QuickSort.Log.Warning($"Could not restore personal profile on lobby exit: {error}");
                }
                hostProfile = null;
                beforeHostSelection = null;
                hostSelected = false;
                hostSelectedManually = false;
                personalSelectionMadeInLobby = false;
                pendingHostApply = false;
                pendingHostRestore = false;
            }
        }

        private static bool UseHost(out string? error)
        {
            error = null;
            lock (Gate)
            {
                if (hostProfile == null)
                {
                    error = "No temporary host profile is available in this lobby.";
                    return false;
                }
                if (!hostSelected)
                {
                    if (File.Exists(HostRecoveryPath))
                    {
                        RecoverInterruptedHostSession();
                        if (File.Exists(HostRecoveryPath))
                        {
                            error = "Previous host profile recovery is pending.";
                            return false;
                        }
                    }
                    if (!CaptureCurrent(out var before, out error)) return false;
                    if (!WriteHostRecovery(before, out error)) return false;
                    beforeHostSelection = before;
                }
                if (!ApplyCurrent(hostProfile, out error))
                {
                    bool restored = false;
                    if (!hostSelected && beforeHostSelection != null)
                    {
                        restored = ApplyCurrent(beforeHostSelection, out var restoreError);
                        if (!restored) QuickSort.Log.Warning($"Could not restore personal settings: {restoreError}");
                    }
                    if (!hostSelected)
                    {
                        beforeHostSelection = null;
                        if (restored) ClearHostRecovery();
                    }
                    return false;
                }
                hostSelected = true;
                hostSelectedManually = true;
                personalSelectionMadeInLobby = false;
                pendingHostApply = false;
                pendingHostRestore = false;
                return true;
            }
        }

        internal static bool Save(string rawName, out string? error)
        {
            if (!TryValidateName(string.IsNullOrWhiteSpace(rawName) ? DefaultName : rawName,
                out var name, out error)) return false;
            lock (Gate)
            {
                // A persistent profile named "host" may predate this feature; never
                // overwrite it with a temporary host selection by accident.
                if (ProfileNetwork.IsRemoteClientSession &&
                    name.Equals(HostName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The name 'host' is reserved for the temporary host profile in this lobby.";
                    return false;
                }
                if (!CaptureCurrent(out var data, out error)) return false;
                if (!ValidateProfile(data, out error)) return false;
                if (!TryLoad(out var file, out error)) return false;
                file.profiles[name] = data;
                file.activeProfile = name;
                if (!SaveFile(file, out error)) return false;
                FinishHostSelectionWithPersonal(data);
                hostSelected = false;
                hostSelectedManually = false;
                beforeHostSelection = null;
                pendingHostApply = false;
                pendingHostRestore = false;
                if (ProfileNetwork.IsRemoteClientSession) personalSelectionMadeInLobby = true;
            }
            ProfileNetwork.BroadcastHostSnapshotIfHosting();
            return true;
        }

        internal static bool Use(string rawName, out string? error)
        {
            if (!TryValidateName(string.IsNullOrWhiteSpace(rawName) ? DefaultName : rawName,
                out var name, out error)) return false;
            if (Sorter.inProgress)
            {
                error = "Wait for the current sort to finish before switching profiles.";
                return false;
            }
            if (name.Equals(HostName, StringComparison.OrdinalIgnoreCase) &&
                ProfileNetwork.IsRemoteClientSession)
                return UseHost(out error);
            lock (Gate)
            {
                if (!TryLoad(out var file, out error)) return false;
                if (!file.profiles.TryGetValue(name, out var target))
                {
                    error = $"Profile '{name}' was not found.";
                    return false;
                }
                if (!ValidateProfile(target, out error)) return false;
                if (!CaptureCurrent(out var before, out error)) return false;

                if (!ApplyCurrent(target, out error))
                {
                    string? originalError = error;
                    if (!ApplyCurrent(before, out var rollbackError))
                        QuickSort.Log.Warning($"Profile rollback failed: {rollbackError}");
                    error = originalError;
                    return false;
                }

                string previousActive = file.activeProfile;
                file.activeProfile = name;
                if (SaveFile(file, out error))
                {
                    FinishHostSelectionWithPersonal(target);
                    hostSelected = false;
                    hostSelectedManually = false;
                    beforeHostSelection = null;
                    pendingHostApply = false;
                    pendingHostRestore = false;
                    if (ProfileNetwork.IsRemoteClientSession) personalSelectionMadeInLobby = true;
                }
                else
                {
                    string? saveError = error;
                    if (!ApplyCurrent(before, out var restoreError))
                        QuickSort.Log.Warning($"Profile rollback failed: {restoreError}");
                    file.activeProfile = previousActive;
                    if (!SaveFile(file, out var markerError))
                        QuickSort.Log.Warning($"Could not restore active profile marker: {markerError}");
                    error = saveError;
                    return false;
                }
            }
            ProfileNetwork.BroadcastHostSnapshotIfHosting();
            return true;
        }

        internal static bool HasCruiserRules(string rawName, out string? error)
        {
            if (!TryValidateName(rawName, out var name, out error)) return false;
            lock (Gate)
            {
                if (name.Equals(HostName, StringComparison.OrdinalIgnoreCase) &&
                    ProfileNetwork.IsRemoteClientSession)
                {
                    if (hostProfile == null)
                    {
                        error = "No temporary host profile is available in this lobby.";
                        return false;
                    }
                    if (hostProfile.cruiserPositions.Count > 0) return true;
                    error = "Temporary host profile has no cruiser positions.";
                    return false;
                }
                if (!TryLoad(out var file, out error)) return false;
                if (!file.profiles.TryGetValue(name, out var target))
                {
                    error = $"Profile '{name}' was not found.";
                    return false;
                }
                if (!ValidateProfile(target, out error)) return false;
                if (target.cruiserPositions.Count == 0)
                {
                    error = $"Profile '{name}' has no cruiser positions.";
                    return false;
                }
                return true;
            }
        }

        internal static bool List(out List<string> names, out string active, out string? error)
        {
            names = new List<string>();
            active = "";
            lock (Gate)
            {
                if (!TryLoad(out var file, out error)) return false;
                active = hostSelected ? "host (temporary)" : file.activeProfile;
                names = file.profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                if (hostProfile != null)
                {
                    // A legacy personal profile can also be named "host". Show both
                    // entries distinctly without changing the persisted one.
                    names.Add("host (temporary)");
                }
                return true;
            }
        }

        internal static bool Delete(string rawName, out string? error)
        {
            if (!TryValidateName(rawName, out var name, out error)) return false;
            if (Sorter.inProgress)
            {
                error = "Wait for the current sort to finish before deleting profiles.";
                return false;
            }
            lock (Gate)
            {
                if (name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The default profile cannot be deleted.";
                    return false;
                }
                if (name.Equals(HostName, StringComparison.OrdinalIgnoreCase) &&
                    ProfileNetwork.IsRemoteClientSession)
                {
                    error = "The temporary host profile disappears when you leave the lobby.";
                    return false;
                }
                if (!TryLoad(out var file, out error)) return false;
                if (!file.profiles.ContainsKey(name))
                {
                    error = $"Profile '{name}' was not found.";
                    return false;
                }
                bool deletingActive = string.Equals(file.activeProfile, name, StringComparison.OrdinalIgnoreCase);
                ProfileData? previousLive = null;
                if (deletingActive && !hostSelected)
                {
                    if (!CaptureCurrent(out previousLive, out error)) return false;
                    if (!file.profiles.TryGetValue(DefaultName, out var baseline))
                    {
                        error = "Default profile is missing.";
                        return false;
                    }
                    if (!ApplyCurrent(baseline, out error))
                    {
                        string? applyError = error;
                        if (!ApplyCurrent(previousLive, out var restoreError))
                            QuickSort.Log.Warning($"Profile rollback failed: {restoreError}");
                        error = applyError;
                        return false;
                    }
                }
                file.profiles.Remove(name);
                if (deletingActive)
                    file.activeProfile = DefaultName;
                if (!SaveFile(file, out error))
                {
                    if (previousLive != null && !ApplyCurrent(previousLive, out var restoreError))
                        QuickSort.Log.Warning($"Profile rollback failed: {restoreError}");
                    return false;
                }
                if (deletingActive && hostSelected && file.profiles.TryGetValue(DefaultName, out var restoreDefault))
                    beforeHostSelection = restoreDefault;
                if (deletingActive && !hostSelected && ProfileNetwork.IsRemoteClientSession)
                {
                    personalSelectionMadeInLobby = true;
                    pendingHostApply = false;
                }
            }
            ProfileNetwork.BroadcastHostSnapshotIfHosting();
            return true;
        }
    }

    public sealed class ProfileCommand : QuickSortCommand
    {
        private const string ProfileDescription =
            "Save and switch named ship/cruiser sorting settings.\n" +
            "  /profile save [name]   -> save current settings (default if omitted)\n" +
            "  /profile use [name]    -> activate a profile (default if omitted; host is temporary)\n" +
            "  /profile sort [name]   -> activate profile (if given) and sort ship\n" +
            "  /profile csort [name]  -> activate profile (if given) and sort cruiser\n" +
            "  /profile list          -> list saved profiles\n" +
            "  /profile delete <name> -> delete a saved profile\n" +
            "  /ps, /pu, /pl, /pd    -> short forms for save, use, list, delete";

        public override string Name => "profile";
        public override string[] Commands => new[] { "profile" };
        public override string Description => ProfileDescription;

        protected override bool InvokeParsed(string[] args, out string? error)
        {
            return InvokeProfile(args, out error);
        }

        internal static bool InvokeProfile(string[] args, out string? error)
        {
            error = null;
            if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                CommandChat.Print(ProfileDescription);
                return true;
            }

            string action = args[0].ToLowerInvariant();
            string name = string.Join(" ", args.Skip(1));
            switch (action)
            {
                case "save":
                    if (!SortProfiles.Save(name, out error)) return false;
                    CommandChat.Print($"Saved profile '{(string.IsNullOrWhiteSpace(name) ? "default" : name.Trim())}'.");
                    return true;
                case "use":
                    if (!SortProfiles.Use(name, out error)) return false;
                    CommandChat.Print($"Activated profile '{(string.IsNullOrWhiteSpace(name) ? "default" : name.Trim())}'.");
                    return true;
                case "sort":
                    if (!Sorter.EnsureLocalPlayerInShip(out error)) return false;
                    if (!string.IsNullOrWhiteSpace(name) && !SortProfiles.Use(name, out error)) return false;
                    return SortCommand.InvokeSort(Array.Empty<string>(), out error);
                case "csort":
                    // Check the vehicle and the requested profile before changing any live
                    // settings, so a failed sort does not unexpectedly switch profiles.
                    if (!CruiserSorter.CheckCruiserReady(out _, out error)) return false;
                    if (!string.IsNullOrWhiteSpace(name) && !SortProfiles.HasCruiserRules(name, out error)) return false;
                    if (!string.IsNullOrWhiteSpace(name) && !SortProfiles.Use(name, out error)) return false;
                    return CruiserSorter.TryStart(out error);
                case "list":
                    if (args.Length != 1)
                    {
                        error = "Usage: /profile list";
                        return false;
                    }
                    if (!SortProfiles.List(out var names, out var active, out error)) return false;
                    CommandChat.Print(names.Count == 0
                        ? "No saved profiles."
                        : "Profiles: " + string.Join(", ", names.Select(n =>
                            string.Equals(n, active, StringComparison.OrdinalIgnoreCase) ? n + " (active)" : n)));
                    return true;
                case "delete":
                    if (!SortProfiles.Delete(name, out error)) return false;
                    CommandChat.Print($"Deleted profile '{name.Trim()}'.");
                    return true;
                default:
                    error = "Unknown profile action. Use /profile help.";
                    return false;
            }
        }
    }

    public abstract class ProfileShortcutCommand : QuickSortCommand
    {
        protected abstract string ActionName { get; }

        protected override bool InvokeParsed(string[] args, out string? error)
        {
            if (args.Length > 0 && args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
                return ProfileCommand.InvokeProfile(new[] { "help" }, out error);
            if (ActionName == "list")
                return ProfileCommand.InvokeProfile(new[] { "list" }, out error);
            return ProfileCommand.InvokeProfile(new[] { ActionName }.Concat(args).ToArray(), out error);
        }
    }

    public sealed class ProfileSaveShortcutCommand : ProfileShortcutCommand
    {
        public override string Name => "ps";
        public override string Description => "Shortcut for /profile save [name].";
        protected override string ActionName => "save";
    }

    public sealed class ProfileUseShortcutCommand : ProfileShortcutCommand
    {
        public override string Name => "pu";
        public override string Description => "Shortcut for /profile use [name].";
        protected override string ActionName => "use";
    }

    public sealed class ProfileListShortcutCommand : ProfileShortcutCommand
    {
        public override string Name => "pl";
        public override string Description => "Shortcut for /profile list.";
        protected override string ActionName => "list";
    }

    public sealed class ProfileDeleteShortcutCommand : ProfileShortcutCommand
    {
        public override string Name => "pd";
        public override string Description => "Shortcut for /profile delete <name>.";
        protected override string ActionName => "delete";
    }
}
