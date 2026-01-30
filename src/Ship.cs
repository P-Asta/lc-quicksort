using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using GameNetcodeStuff;

namespace QuickSort
{
    public static class Ship
    {
        public static Action OnShipOrbit;
        public static Action OnShipTouchdown;
        public static Action OnShipAscent;
        public static Action OnShipDescent;

        // NOTE:
        // Older implementations often patched methods like StartOfRound.SwitchMapMonitorPurpose to infer "orbit",
        // but LC updates changed semantics and that signal became unreliable.
        //
        // To keep this mod working across versions, we use reflection-based heuristics against StartOfRound.
        // Some builds/publicizers expose these flags as fields, others as properties, and names can vary.
        private static readonly Dictionary<string, FieldInfo?> _startOfRoundBoolFields = new();
        private static readonly Dictionary<string, PropertyInfo?> _startOfRoundBoolProps = new();
        private static readonly Dictionary<string, MemberInfo?> _startOfRoundBoolFuzzy = new();

        private static bool? GetStartOfRoundBoolExact(string name)
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return null;
            var t = sor.GetType();

            if (!_startOfRoundBoolFields.TryGetValue(name, out var fi))
            {
                fi = AccessTools.Field(t, name);
                _startOfRoundBoolFields[name] = fi;
            }
            if (fi != null)
            {
                try
                {
                    if (fi.GetValue(sor) is bool b) return b;
                }
                catch { }
            }

            if (!_startOfRoundBoolProps.TryGetValue(name, out var pi))
            {
                pi = AccessTools.Property(t, name);
                _startOfRoundBoolProps[name] = pi;
            }
            if (pi != null && pi.PropertyType == typeof(bool) && pi.GetIndexParameters().Length == 0)
            {
                try
                {
                    if (pi.GetValue(sor, null) is bool b) return b;
                }
                catch { }
            }

            return null;
        }

        private static bool? GetStartOfRoundBoolFuzzy(string containsOrAltName)
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return null;
            var t = sor.GetType();

            if (!_startOfRoundBoolFuzzy.TryGetValue(containsOrAltName, out var member))
            {
                const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                string needle = containsOrAltName;

                // First pass: exact (case-insensitive) name match among bool fields/properties
                var exactField = t.GetFields(FLAGS)
                    .FirstOrDefault(f => f.FieldType == typeof(bool) && string.Equals(f.Name, needle, StringComparison.OrdinalIgnoreCase));
                if (exactField != null)
                {
                    member = exactField;
                }
                else
                {
                    var exactProp = t.GetProperties(FLAGS)
                        .FirstOrDefault(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0 && string.Equals(p.Name, needle, StringComparison.OrdinalIgnoreCase));
                    member = exactProp;
                }

                // Second pass: contains match (case-insensitive)
                if (member == null)
                {
                    var containsField = t.GetFields(FLAGS)
                        .FirstOrDefault(f => f.FieldType == typeof(bool) && f.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (containsField != null)
                    {
                        member = containsField;
                    }
                    else
                    {
                        var containsProp = t.GetProperties(FLAGS)
                            .FirstOrDefault(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0 && p.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                        member = containsProp;
                    }
                }

                _startOfRoundBoolFuzzy[containsOrAltName] = member;
            }

            try
            {
                if (member is FieldInfo f && f.FieldType == typeof(bool))
                    return (bool)f.GetValue(sor);
                if (member is PropertyInfo p && p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                    return (bool)p.GetValue(sor, null);
            }
            catch { }

            return null;
        }

        private static bool? GetStartOfRoundBool(params string[] candidates)
        {
            // Try exact names first, then fuzzy (contains) lookups.
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var v = GetStartOfRoundBoolExact(c);
                if (v.HasValue) return v;
            }

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var v = GetStartOfRoundBoolFuzzy(c);
                if (v.HasValue) return v;
            }

            return null;
        }

        /// <summary>
        /// Best-effort check for when the ship is "not moving" (safe to sort).
        /// </summary>
        public static bool Stationary
        {
            get
            {
                // True if ship has landed (moons/company)
                if (GetStartOfRoundBool("shipHasLanded", "ShipHasLanded", "hasLanded", "HasLanded") == true) return true;

                // In many LC versions, "inShipPhase" is true when in orbit/company UI phase.
                // If the ship isn't currently leaving, treat as stationary.
                if (GetStartOfRoundBool("inShipPhase", "InShipPhase", "shipPhase", "ShipPhase") == true &&
                    GetStartOfRoundBool("shipIsLeaving", "ShipIsLeaving", "isShipLeaving", "IsShipLeaving", "shipLeaving") != true)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Unified "safe to run sorter commands" check.
        /// This intentionally allows:
        /// - landed states
        /// - orbit / ship-phase states, INCLUDING takeoff/landing transitions
        /// </summary>
        public static bool CanSortNow
        {
            get
            {
                // Landed is always allowed.
                if (GetStartOfRoundBool("shipHasLanded", "ShipHasLanded", "hasLanded", "HasLanded") == true)
                    return true;

                // During orbit/ship-phase (including leaving/landing animations), allow sorting too.
                if (GetStartOfRoundBool("inShipPhase", "InShipPhase", "shipPhase", "ShipPhase") == true)
                    return true;

                // Fallback to previous heuristic.
                return InOrbit || Stationary;
            }
        }

        /// <summary>
        /// Best-effort "in orbit" check (used only for messaging / debugging).
        /// </summary>
        public static bool InOrbit =>
            GetStartOfRoundBool("inShipPhase", "InShipPhase", "shipPhase", "ShipPhase") == true &&
            GetStartOfRoundBool("shipHasLanded", "ShipHasLanded", "hasLanded", "HasLanded") != true;

        [HarmonyPatch(typeof(StartOfRound), "ShipLeave")]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void OnShipLeave()
        {
            OnShipAscent?.Invoke();
            OnShipDescent?.Invoke();
        }

        [HarmonyPatch(typeof(StartOfRound), "OnShipLandedMiscEvents")]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void OnShipLanded()
        {
            OnShipTouchdown?.Invoke();
            OnShipOrbit?.Invoke();
        }
    }
}
