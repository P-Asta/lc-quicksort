using System.Collections.Generic;
using System.Reflection.Emit;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;

namespace QuickSort
{
    // Based on ScrapMagic's working approach: inject an override hook into BeginGrabObject
    // so our coroutine can force-grab a specific GrabbableObject.
    [HarmonyPatch(typeof(PlayerControllerB), "BeginGrabObject")]
    internal static class GrabPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {
            var instrList = new List<CodeInstruction>(instructions);
            var matcher = new CodeMatcher(instrList, ilGenerator);

            // This is the same pattern ScrapMagic uses:
            // - early in the method, call Player.OverrideGrabbingObject()
            // - if it returns true, branch past the normal selection logic
            //
            // Note: this is a best-effort patch; if LC changes BeginGrabObject too much,
            // the matcher will throw and Harmony will log it (better than silent misbehavior).
            // Find which local variable BeginGrabObject uses to store the targeted NetworkObject.
            // ScrapMagic assumed local0, but LC updates can change local ordering.
            CodeInstruction? stlocForTargetNetworkObject = null;
            var getNetworkObject = AccessTools.Method(typeof(NetworkBehaviour), "get_NetworkObject");
            for (int i = 0; i < instrList.Count - 1; i++)
            {
                var ins = instrList[i];
                if (ins.opcode == OpCodes.Callvirt && ins.operand is System.Reflection.MethodInfo mi && mi == getNetworkObject)
                {
                    var next = instrList[i + 1];
                    if (IsStloc(next.opcode))
                    {
                        stlocForTargetNetworkObject = next.Clone();
                        break;
                    }
                }
            }

            // Fallback to stloc.0 if we couldn't detect it.
            stlocForTargetNetworkObject ??= new CodeInstruction(OpCodes.Stloc_0);

            Label label;

            return matcher
                .MatchForward(
                    useEnd: true,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(GrabbableObject), "InteractItem"))
                )
                .MatchBack(useEnd: true, new CodeMatch(OpCodes.Ldarg_0))
                .Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerControllerB), "currentlyGrabbingObject")),
                    new CodeInstruction(OpCodes.Callvirt, getNetworkObject),
                    stlocForTargetNetworkObject
                )
                .ThrowIfInvalid("QuickSort: BeginGrabObject pattern not found (InteractItem)")
                .CreateLabel(out label)
                .Start()
                .Insert(
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Player), nameof(Player.OverrideGrabbingObject))),
                    new CodeInstruction(OpCodes.Brtrue, label)
                )
                .InstructionEnumeration();
        }

        private static bool IsStloc(OpCode op) =>
            op == OpCodes.Stloc || op == OpCodes.Stloc_S || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1
            || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3;
    }
}


