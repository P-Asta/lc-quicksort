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
    internal static class CruiserPositions
    {
        [Serializable]
        private sealed class PositionsFile
        {
            public List<PositionEntry> positions = new List<PositionEntry>();
        }

        [Serializable]
        private sealed class PositionEntry
        {
            public string item = "";
            public float x;
            public float y;
            public float z;
            public int maxCount = 10;
        }

        public static string PositionsPath => Path.Combine(Paths.ConfigPath, "pasta.quicksort.cruiser.positions.json");

        public static void EnsureFileExists()
        {
            try
            {
                if (File.Exists(PositionsPath)) return;
                Save(new PositionsFile(), out _);
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Failed to create cruiser positions file: {e.Message}");
            }
        }

        private static PositionsFile Load(out string? error)
        {
            error = null;
            EnsureFileExists();
            try
            {
                var data = JsonConvert.DeserializeObject<PositionsFile>(File.ReadAllText(PositionsPath, Encoding.UTF8));
                if (data == null || data.positions == null)
                    throw new InvalidDataException("Missing positions list.");
                return data;
            }
            catch (Exception e)
            {
                error = $"Failed to load cruiser positions: {e.Message}";
                return new PositionsFile();
            }
        }

        private static bool Save(PositionsFile data, out string? error)
        {
            error = null;
            string path = PositionsPath;
            string tmp = path + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, JsonConvert.SerializeObject(data, Formatting.Indented), Encoding.UTF8);
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true); }
                    catch
                    {
                        File.Copy(tmp, path, overwrite: true);
                        File.Delete(tmp);
                    }
                }
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save cruiser positions: {e.Message}";
                return false;
            }
        }

        private static bool Valid(Vector3 point, int count) =>
            count > 0 && count <= 10000 &&
            !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) &&
            !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);

        public static bool Set(string itemKey, Vector3 cruiserLocalPos, int maxCount, out string? error)
        {
            itemKey = Extensions.NormalizeName(itemKey);
            if (string.IsNullOrWhiteSpace(itemKey) || !Valid(cruiserLocalPos, maxCount))
            {
                error = "Invalid cruiser item, position, or maximum (1-10000).";
                return false;
            }
            var data = Load(out error);
            if (error != null) return false;
            data.positions.RemoveAll(p => p != null && Extensions.NormalizeName(p.item) == itemKey);
            data.positions.Add(new PositionEntry
            {
                item = itemKey, x = cruiserLocalPos.x, y = cruiserLocalPos.y, z = cruiserLocalPos.z, maxCount = maxCount
            });
            return Save(data, out error);
        }

        public static bool Remove(string itemKey, out bool removed, out string? error)
        {
            itemKey = Extensions.NormalizeName(itemKey);
            var data = Load(out error);
            removed = false;
            if (error != null) return false;
            removed = data.positions.RemoveAll(p => p != null && Extensions.NormalizeName(p.item) == itemKey) > 0;
            return !removed || Save(data, out error);
        }

        public static List<(string itemKey, Vector3 cruiserLocalPos, int maxCount)> ListAll(out string? error)
        {
            var data = Load(out error);
            if (error != null) return new List<(string, Vector3, int)>();
            var result = new List<(string itemKey, Vector3 cruiserLocalPos, int maxCount)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in data.positions)
            {
                if (entry == null) continue;
                string key = Extensions.NormalizeName(entry.item);
                Vector3 point = new Vector3(entry.x, entry.y, entry.z);
                if (string.IsNullOrWhiteSpace(key) || !Valid(point, entry.maxCount) || !seen.Add(key))
                {
                    error = $"Invalid or duplicate cruiser position for '{entry.item}'.";
                    return new List<(string, Vector3, int)>();
                }
                result.Add((key, point, entry.maxCount));
            }
            return result.OrderBy(p => p.itemKey).ToList();
        }

        public static bool ReplaceAll(IEnumerable<(string itemKey, Vector3 cruiserLocalPos, int maxCount)> positions, out string? error)
        {
            error = null;
            if (positions == null)
            {
                error = "Cruiser positions are missing.";
                return false;
            }
            var entries = new List<PositionEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var position in positions)
            {
                string key = Extensions.NormalizeName(position.itemKey);
                Vector3 point = position.cruiserLocalPos;
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key) || !Valid(point, position.maxCount))
                {
                    error = $"Invalid or duplicate cruiser position for '{position.itemKey}'.";
                    return false;
                }
                entries.Add(new PositionEntry
                {
                    item = key, x = point.x, y = point.y, z = point.z, maxCount = position.maxCount
                });
            }
            Load(out error);
            if (error != null) return false;
            return Save(new PositionsFile { positions = entries }, out error);
        }
    }
}
