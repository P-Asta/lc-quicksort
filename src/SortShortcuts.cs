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
    internal static class SortShortcuts
    {
        [Serializable]
        private class ShortcutFile
        {
            public List<Shortcut> shortcuts = new List<Shortcut>();
            public Dictionary<string, string> aliases = new Dictionary<string, string>();
        }

        [Serializable]
        private class Shortcut
        {
            public int id;
            public string item = "";
        }

        // "bind" feature fits better as a "bindings" file.
        // We still support migration from the old filename for backwards compatibility.
        private static string OldShortcutPath =>
            Path.Combine(Paths.ConfigPath, "pasta.quicksort.sort.shortcuts.json");

        public static string ShortcutPath =>
            Path.Combine(Paths.ConfigPath, "pasta.quicksort.sort.bindings.json");

        public static void EnsureFileExists()
        {
            try
            {
                var dir = Path.GetDirectoryName(ShortcutPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                // Migrate old file name -> new file name (one-time).
                if (!File.Exists(ShortcutPath) && File.Exists(OldShortcutPath))
                {
                    try
                    {
                        File.Copy(OldShortcutPath, ShortcutPath, overwrite: false);
                        QuickSort.Log.Info($"Migrated shortcuts file '{OldShortcutPath}' => '{ShortcutPath}'");
                    }
                    catch (Exception e)
                    {
                        QuickSort.Log.Warning($"Failed to migrate shortcuts file: {e.GetType().Name}: {e.Message}");
                    }
                }

                if (File.Exists(ShortcutPath)) return;

                var sample = new ShortcutFile
                {
                    shortcuts = new List<Shortcut>
                    {
                        new Shortcut { id = 1, item = "weed_killer" },
                        new Shortcut { id = 2, item = "shovel" },
                    }
                };

                string json = JsonConvert.SerializeObject(sample, Formatting.Indented);
                if (!TryWriteAllTextAtomic(ShortcutPath, json, out var writeError))
                    throw new IOException(writeError ?? "Failed to write shortcuts file (unknown error).");
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Failed to create shortcuts file: {e.GetType().Name}: {e.Message}");
                QuickSort.Log.Warning($"Stack trace: {e.StackTrace}");
            }
        }

        public static bool TryResolve(int id, out string itemKey, out string? error)
        {
            itemKey = "";
            error = null;

            EnsureFileExists();

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json);
                if (data?.shortcuts == null)
                {
                    error = "Shortcuts file is empty or invalid.";
                    return false;
                }
                data.aliases ??= new Dictionary<string, string>();

                var match = data.shortcuts.FirstOrDefault(s => s != null && s.id == id);
                if (match == null || string.IsNullOrWhiteSpace(match.item))
                {
                    error = $"No shortcut found for {id}.";
                    return false;
                }

                itemKey = Extensions.NormalizeName(match.item);
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to load shortcuts JSON: {e.Message}";
                return false;
            }
        }

        public static bool SetShortcut(int id, string itemKey, out string? error)
        {
            error = null;
            EnsureFileExists();

            if (id <= 0)
            {
                error = "Shortcut id must be >= 1.";
                return false;
            }

            itemKey = Extensions.NormalizeName(itemKey);
            if (string.IsNullOrWhiteSpace(itemKey))
            {
                error = "Invalid item key.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json) ?? new ShortcutFile();
                data.shortcuts ??= new List<Shortcut>();
                data.aliases ??= new Dictionary<string, string>();

                var match = data.shortcuts.FirstOrDefault(s => s != null && s.id == id);
                if (match == null)
                {
                    match = new Shortcut { id = id, item = itemKey };
                    data.shortcuts.Add(match);
                }
                match.id = id;
                match.item = itemKey;

                string outJson = JsonConvert.SerializeObject(data, Formatting.Indented);
                if (!TryWriteAllTextAtomic(ShortcutPath, outJson, out error))
                    return false;

                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save shortcuts JSON: {e.Message}";
                return false;
            }
        }

        public static bool TryResolveAlias(string alias, out string itemKey, out string? error)
        {
            itemKey = "";
            error = null;
            EnsureFileExists();

            alias = Extensions.NormalizeName(alias);
            if (string.IsNullOrWhiteSpace(alias))
            {
                error = "Invalid alias.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json);
                if (data == null)
                {
                    error = "Shortcuts file is empty or invalid.";
                    return false;
                }

                data.aliases ??= new Dictionary<string, string>();
                if (!data.aliases.TryGetValue(alias, out var raw) || string.IsNullOrWhiteSpace(raw))
                {
                    error = $"No alias found for '{alias}'.";
                    return false;
                }

                itemKey = Extensions.NormalizeName(raw);
                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to load shortcuts JSON: {e.Message}";
                return false;
            }
        }

        public static bool BindAlias(string alias, string itemKey, out string? error)
        {
            error = null;
            EnsureFileExists();

            alias = Extensions.NormalizeName(alias);
            itemKey = Extensions.NormalizeName(itemKey);
            if (string.IsNullOrWhiteSpace(alias))
            {
                error = "Invalid alias.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(itemKey))
            {
                error = "Invalid item key.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json) ?? new ShortcutFile();
                data.shortcuts ??= new List<Shortcut>();
                data.aliases ??= new Dictionary<string, string>();

                data.aliases[alias] = itemKey;

                string outJson = JsonConvert.SerializeObject(data, Formatting.Indented);
                if (!TryWriteAllTextAtomic(ShortcutPath, outJson, out error))
                    return false;

                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save shortcuts JSON: {e.Message}";
                return false;
            }
        }

        public static bool RemoveShortcut(int id, out bool removed, out string? error)
        {
            removed = false;
            error = null;
            EnsureFileExists();

            if (id <= 0)
            {
                error = "Shortcut id must be >= 1.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json) ?? new ShortcutFile();
                data.shortcuts ??= new List<Shortcut>();
                data.aliases ??= new Dictionary<string, string>();

                int before = data.shortcuts.Count;
                data.shortcuts = data.shortcuts
                    .Where(s => s != null && s.id != id)
                    .ToList();
                removed = data.shortcuts.Count != before;

                string outJson = JsonConvert.SerializeObject(data, Formatting.Indented);
                if (!TryWriteAllTextAtomic(ShortcutPath, outJson, out error))
                    return false;

                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save shortcuts JSON: {e.Message}";
                return false;
            }
        }

        public static bool RemoveAlias(string alias, out bool removed, out string? error)
        {
            removed = false;
            error = null;
            EnsureFileExists();

            alias = Extensions.NormalizeName(alias);
            if (string.IsNullOrWhiteSpace(alias))
            {
                error = "Invalid alias.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json) ?? new ShortcutFile();
                data.shortcuts ??= new List<Shortcut>();
                data.aliases ??= new Dictionary<string, string>();

                removed = data.aliases.Remove(alias);

                string outJson = JsonConvert.SerializeObject(data, Formatting.Indented);
                if (!TryWriteAllTextAtomic(ShortcutPath, outJson, out error))
                    return false;

                return true;
            }
            catch (Exception e)
            {
                error = $"Failed to save shortcuts JSON: {e.Message}";
                return false;
            }
        }

        public static List<(string alias, string itemKey)> ListAliases(out string? error)
        {
            error = null;
            EnsureFileExists();
            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json);
                if (data?.aliases == null) return new List<(string, string)>();

                return data.aliases
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => (Extensions.NormalizeName(kv.Key), Extensions.NormalizeName(kv.Value)))
                    .OrderBy(x => x.Item1)
                    .ToList();
            }
            catch (Exception e)
            {
                error = $"Failed to load shortcuts JSON: {e.Message}";
                return new List<(string, string)>();
            }
        }

        public static List<(int id, string itemKey)> ListShortcuts(out string? error)
        {
            error = null;
            EnsureFileExists();

            try
            {
                string json = File.ReadAllText(ShortcutPath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<ShortcutFile>(json);
                if (data?.shortcuts == null) return new List<(int, string)>();

                return data.shortcuts
                    .Where(s => s != null && s.id != 0 && !string.IsNullOrWhiteSpace(s.item))
                    .OrderBy(s => s.id)
                    .Select(s => (s.id, Extensions.NormalizeName(s.item)))
                    .ToList();
            }
            catch (Exception e)
            {
                error = $"Failed to load shortcuts JSON: {e.Message}";
                return new List<(int, string)>();
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
                    }
                    catch
                    {
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
                error = $"{e.GetType().Name}: {e.Message} (Path='{path}')";
                return false;
            }
        }
    }
}


