using System;
using System.Collections.Generic;
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
        private static readonly Dictionary<string, FieldInfo?> _startOfRoundBoolFields = new();

        private static bool? GetStartOfRoundBool(string fieldName)
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return null;

            if (!_startOfRoundBoolFields.TryGetValue(fieldName, out var fi))
            {
                fi = AccessTools.Field(sor.GetType(), fieldName);
                _startOfRoundBoolFields[fieldName] = fi;
            }

            if (fi == null) return null;
            try
            {
                if (fi.GetValue(sor) is bool b) return b;
            }
            catch
            {
                // ignore reflection failures; treat as unknown
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
                if (GetStartOfRoundBool("shipHasLanded") == true) return true;

                // In many LC versions, "inShipPhase" is true when in orbit/company UI phase.
                // If the ship isn't currently leaving, treat as stationary.
                if (GetStartOfRoundBool("inShipPhase") == true && GetStartOfRoundBool("shipIsLeaving") != true)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Best-effort "in orbit" check (used only for messaging / debugging).
        /// </summary>
        public static bool InOrbit =>
            GetStartOfRoundBool("inShipPhase") == true && GetStartOfRoundBool("shipHasLanded") != true;

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
