using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace QuickSort
{
    // Keep cruiser cargo fixed relative to the vehicle while leaving its PhysicsProp
    // collider enabled, so players can still pick it up normally.
    internal sealed class CruiserShelfAnchor : MonoBehaviour
    {
        private GrabbableObject? item;
        private Rigidbody? body;
        private Transform? cargoParent;
        private Vector3 localPosition;
        private bool originalKinematic;
        private bool initialized;

        internal static void Attach(GrabbableObject item, Transform cargoParent)
        {
            if (item == null || cargoParent == null || item.transform.parent != cargoParent || item.isHeld)
                return;

            var anchor = item.GetComponent<CruiserShelfAnchor>();
            if (anchor == null)
                anchor = item.gameObject.AddComponent<CruiserShelfAnchor>();
            anchor.Configure(item, cargoParent);
        }

        private void Configure(GrabbableObject placedItem, Transform parent)
        {
            if (!initialized)
            {
                item = placedItem;
                body = placedItem.GetComponent<Rigidbody>();
                originalKinematic = body != null && body.isKinematic;
                initialized = true;
            }
            cargoParent = parent;
            localPosition = placedItem.transform.localPosition;
            PinItem();
        }

        private void FixedUpdate()
        {
            if (!StillOnCruiser())
            {
                Release();
                Destroy(this);
                return;
            }
            PinBody();
        }

        private void LateUpdate()
        {
            if (!StillOnCruiser())
            {
                Release();
                Destroy(this);
                return;
            }
            PinItem();
        }

        private bool StillOnCruiser() =>
            initialized && item != null && cargoParent != null && item.transform.parent == cargoParent &&
            !item.isHeld && !item.isPocketed && !item.deactivated;

        private void PinItem()
        {
            if (item == null) return;
            item.transform.localPosition = localPosition;
            item.targetFloorPosition = localPosition;
            item.startFallingPosition = localPosition;
            item.fallTime = 1.1f;
            item.reachedFloorTarget = true;
            item.hasHitGround = true;
            PinBody();
        }

        private void PinBody()
        {
            if (body == null) return;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private void Release()
        {
            if (!initialized) return;
            initialized = false;
            if (body != null)
                body.isKinematic = originalKinematic;
        }

        internal void ReleaseForPickup()
        {
            Release();
            Destroy(this);
        }

        private void OnDisable() => Release();

        private void OnDestroy() => Release();
    }

    // PlaceGrabbableObject runs on the holder when they drop an item and on every
    // other client through PlaceObjectClientRpc. QuickSort uses direct placement
    // (matchRotationOfParent=true) and one of its exact shelf slots.
    internal static class CruiserShelfAnchorPatch
    {
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.PlaceGrabbableObject))]
        [HarmonyPostfix]
        private static void AfterPlacement(Transform parentObject, bool matchRotationOfParent,
            GrabbableObject placeObject)
        {
            if (!matchRotationOfParent || parentObject == null || placeObject == null) return;
            var cruiser = parentObject.GetComponentInParent<VehicleController>();
            if (cruiser == null) return;
            Vector3 cruiserLocalPosition = cruiser.transform.InverseTransformPoint(placeObject.transform.position);
            if (!CruiserSorter.IsShelfSlot(cruiserLocalPosition)) return;
            var region = cruiser.GetComponentInChildren<PlayerPhysicsRegion>();
            Transform cargoParent = region != null && region.allowDroppingItems && region.physicsTransform != null
                ? region.physicsTransform : cruiser.transform;
            CruiserShelfAnchor.Attach(placeObject, cargoParent);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "GrabObjectClientRpc")]
        [HarmonyPrefix]
        private static void BeforePickup(bool grabValidated, NetworkObjectReference grabbedObject)
        {
            if (!grabValidated || !grabbedObject.TryGet(out var networkObject)) return;
            var anchor = networkObject.GetComponentInChildren<CruiserShelfAnchor>();
            if (anchor != null)
                anchor.ReleaseForPickup();
        }
    }
}
