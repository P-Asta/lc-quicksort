using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;

namespace QuickSort
{
    internal static class SortPositions
    {
        [Serializable]
        private class PositionsFile
        {
            public List<PositionEntry> positions = new List<PositionEntry>();
        }

        [Serializable]
        private class PositionEntry
        {
            public string item = "";
            public float x;
            public float y;
            public float z;
        }

        public static string PositionsPath =>
            Path.Combine(Paths.ConfigPath, "pasta.quicksort.sort.positions.json");

        public static void EnsureFileExists()
        {
            try
            {
                var dir = Path.GetDirectoryName(PositionsPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(PositionsPath)) return;

                // Default sample positions (user-editable).
                // Only written the first time (when the file does not exist).
                var sample = new PositionsFile
                {
                    positions = new List<PositionEntry>
                    {
                        new PositionEntry
                        {
                            item = "soccer_ball",
                            x = 9.085655f,
                            y = -1.24895f,
                            z = -8.23046f
                        },
                        new PositionEntry
                        {
                            item = "whoopie_cushion",
                            x = 9.062115f,
                            y = 0.110211849f,
                            z = -6.09901428f
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(sample, Formatting.Indented);
                if (!TryWriteAllTextAtomic(PositionsPath, json, out var writeError))
                    throw new IOException(writeError ?? "Failed to write positions file (unknown error).");
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Failed to create positions file: {e.GetType().Name}: {e.Message}");
                QuickSort.Log.Warning($"Stack trace: {e.StackTrace}");
            }
        }

        private static PositionsFile Load(out string? error)
        {
            error = null;
            EnsureFileExists();

            try
            {
                string json = File.ReadAllText(PositionsPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<PositionsFile>(json) ?? new PositionsFile();
                data.positions ??= new List<PositionEntry>();
                return data;
            }
            catch (Exception e)
            {
                error = $"Failed to load positions JSON: {e.Message}";
                return new PositionsFile();
            }
        }

        private static bool Save(PositionsFile data, out string? error)
        {
            error = null;
            try
            {
                var dir = Path.GetDirectoryName(PositionsPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                if (!TryWriteAllTextAtomic(PositionsPath, json, out error))
                    return false;

                // Post-write verification (helps catch path redirection / external cleanup).
                if (!File.Exists(PositionsPath))
                {
                    error = $"Positions JSON not found after write. Path='{PositionsPath}', CWD='{Directory.GetCurrentDirectory()}', ConfigPath='{Paths.ConfigPath}', BepInExRoot='{Paths.BepInExRootPath}'.";
                    return false;
                }

                var info = new FileInfo(PositionsPath);
                if (info.Length <= 0)
                {
                    error = $"Positions JSON is empty after write. Path='{PositionsPath}', LastWriteUtc='{info.LastWriteTimeUtc:o}'.";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save positions JSON: {e.Message}";
                return false;
            }
        }

        private static bool TryWriteAllTextAtomic(string path, string content, out string? error)
        {
            error = null;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var tmp = path + ".tmp";
                File.WriteAllText(tmp, content, Encoding.UTF8);

                if (File.Exists(path))
                {
                    var bak = path + ".bak";
                    try
                    {
                        File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                        // Keep .bak around for debugging/recovery.
                    }
                    catch
                    {
                        // Fallback if Replace isn't supported in this environment.
                        File.Copy(tmp, path, overwrite: true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }

                return true;
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message} (Path='{path}', CWD='{Directory.GetCurrentDirectory()}', ConfigPath='{Paths.ConfigPath}')";
                return false;
            }
        }

        public static bool TryGet(string itemKey, out Vector3 shipLocalPos, out string? error)
        {
            shipLocalPos = default;
            itemKey = Extensions.NormalizeName(itemKey);

            var data = Load(out error);
            if (error != null) return false;

            var match = data.positions?.FirstOrDefault(p => p != null && Extensions.NormalizeName(p.item) == itemKey);
            if (match == null) return false;

            shipLocalPos = new Vector3(match.x, match.y, match.z);
            return true;
        }

        public static bool Set(string itemKey, Vector3 shipLocalPos, out string? error)
        {
            itemKey = Extensions.NormalizeName(itemKey);
            var data = Load(out error);
            if (error != null) return false;

            data.positions ??= new List<PositionEntry>();
            var match = data.positions.FirstOrDefault(p => p != null && Extensions.NormalizeName(p.item) == itemKey);
            if (match == null)
            {
                match = new PositionEntry { item = itemKey };
                data.positions.Add(match);
            }

            match.item = itemKey;
            match.x = shipLocalPos.x;
            match.y = shipLocalPos.y;
            match.z = shipLocalPos.z;

            bool ok = Save(data, out error);
            if (!ok)
            {
                if (error != null) QuickSort.Log.Warning(error);
                return false;
            }

            QuickSort.Log.Info($"Saved sort position '{itemKey}' => (x={shipLocalPos.x:F3}, y={shipLocalPos.y:F3}, z={shipLocalPos.z:F3}) to {PositionsPath}");
            return true;
        }

        public static bool Remove(string itemKey, out bool removed, out string? error)
        {
            removed = false;
            itemKey = Extensions.NormalizeName(itemKey);
            var data = Load(out error);
            if (error != null) return false;

            if (data.positions == null || data.positions.Count == 0)
            {
                removed = false;
                return true;
            }

            int before = data.positions.Count;
            data.positions = data.positions
                .Where(p => p != null && Extensions.NormalizeName(p.item) != itemKey)
                .ToList();
            removed = data.positions.Count != before;

            bool ok = Save(data, out error);
            if (!ok)
            {
                if (error != null) QuickSort.Log.Warning(error);
                return false;
            }

            if (removed)
                QuickSort.Log.Info($"Removed sort position '{itemKey}' from {PositionsPath}");
            return true;
        }

        public static List<(string itemKey, Vector3 shipLocalPos)> ListAll(out string? error)
        {
            var data = Load(out error);
            if (error != null) return new List<(string, Vector3)>();

            if (data.positions == null) return new List<(string, Vector3)>();

            return data.positions
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.item))
                .Select(p => (Extensions.NormalizeName(p.item), new Vector3(p.x, p.y, p.z)))
                .OrderBy(p => p.Item1)
                .ToList();
        }
    }
}


