using System.Collections;
using UnityEngine;
using GameNetcodeStuff;
using Unity.Netcode;

namespace QuickSort
{
    public static class Player
    {
        public static GrabbableObject overrideObject;
        public static PlayerControllerB Local => StartOfRound.Instance?.localPlayerController;

        public static bool CanGrabObject(GrabbableObject item)
        {
            if (item == null || !item.grabbable || item.deactivated || item.isHeld || item.isPocketed)
            {
                return false;
            }

            if (Local == null || Local.isPlayerDead || Local.isTypingChat || Local.inTerminalMenu || 
                Local.throwingObject || Local.IsInspectingItem || Local.isGrabbingObjectAnimation || 
                Local.inAnimationWithEnemy != null || Local.inSpecialInteractAnimation || 
                Local.jetpackControls || Local.disablingJetpackControls || Local.activatingItem || 
                Local.waitingToDropItem || Local.FirstEmptyItemSlot() == -1)
            {
                return false;
            }

            return true;
        }

        public static bool OverrideGrabbingObject()
        {
            if (overrideObject == null)
            {
                return false;
            }

            Local.currentlyGrabbingObject = overrideObject;
            overrideObject = null;
            return true;
        }

        public static IEnumerator StartGrabbingObject(GrabbableObject grabbableObject)
        {
            if (CanGrabObject(grabbableObject))
            {
                overrideObject = grabbableObject;
                Local.BeginGrabObject();
                yield return Local.grabObjectCoroutine;
            }
        }

        public static IEnumerator StartMovingObject(GrabbableObject item, Vector3 position, NetworkObject? parent = null)
        {
            yield return StartGrabbingObject(item);

            // Guard: never call DiscardHeldObject unless we are actually holding the item.
            // Otherwise LC may throw inside SetSpecialGrabAnimationBool(currentItem=null).
            if (Local == null)
                yield break;

            const int maxFrames = 30;
            int frames = 0;
            while (frames < maxFrames && (Local.currentlyHeldObjectServer == null || Local.currentlyHeldObjectServer != item))
            {
                frames++;
                yield return null;
            }

            if (Local.currentlyHeldObjectServer == null || Local.currentlyHeldObjectServer != item)
            {
                Log.Warning($"Failed to grab {item?.itemProperties?.itemName ?? "item"}; skipping move to avoid crash.");
                yield break;
            }
            
            try
            {
                item.floorYRot = -1;
                Local.DiscardHeldObject(true, parent, position, false);
            }
            catch (System.Exception e)
            {
                Log.Exception(e);
            }
        }
    }
}

