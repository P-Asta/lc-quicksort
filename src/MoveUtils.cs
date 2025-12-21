using System;
using GameNetcodeStuff;
using UnityEngine;

namespace QuickSort
{
    internal static class MoveUtils
    {
        /// <summary>
        /// LethalShipSort-style move: force the server to update item position by using PlayerControllerB
        /// methods that already replicate state (prevents snap-back).
        /// </summary>
        public static bool MoveItemOnShip(GrabbableObject item, Vector3 worldPos, int floorYRot = -1)
        {
            if (item == null) return false;

            var player = GameNetworkManager.Instance?.localPlayerController;
            if (player == null) return false;

            // LethalShipSort uses Environment/HangarShip as the coordinate space for ThrowObjectServerRpc.
            var ship = GameObject.Find("Environment/HangarShip");
            if (ship == null) return false;

            // Convert to ship-local position (matches LethalShipSort Utils.MoveItemRelativeTo path)
            Vector3 shipLocalPos = ship.transform.InverseTransformPoint(worldPos);

            // Update client-side immediately for responsiveness
            item.transform.position = worldPos;

            // Force Netcode sync via existing RPCs
            player.SetObjectAsNoLongerHeld(true, true, shipLocalPos, item, floorYRot);
            player.ThrowObjectServerRpc(item.NetworkObject, true, true, shipLocalPos, floorYRot);

            return true;
        }

        /// <summary>
        /// Same as MoveItemOnShip, but expects ship-local coordinates (relative to Environment/HangarShip).
        /// </summary>
        public static bool MoveItemOnShipLocal(GrabbableObject item, Vector3 shipLocalPos, int floorYRot = -1)
        {
            if (item == null) return false;

            var player = GameNetworkManager.Instance?.localPlayerController;
            if (player == null) return false;

            var ship = GameObject.Find("Environment/HangarShip");
            if (ship == null) return false;

            // Update client-side immediately for responsiveness
            item.transform.position = ship.transform.TransformPoint(shipLocalPos);

            player.SetObjectAsNoLongerHeld(true, true, shipLocalPos, item, floorYRot);
            player.ThrowObjectServerRpc(item.NetworkObject, true, true, shipLocalPos, floorYRot);
            return true;
        }

        /// <summary>
        /// Backwards-compat wrapper.
        /// </summary>
        public static bool TryTeleportToWorld(GameObject go, Vector3 worldPos, Quaternion worldRot)
        {
            var item = go != null ? go.GetComponent<GrabbableObject>() : null;
            if (item != null)
                return MoveItemOnShip(item, worldPos, item.floorYRot);

            if (go == null) return false;
            go.transform.SetPositionAndRotation(worldPos, worldRot);
            return true;
        }
    }
}


