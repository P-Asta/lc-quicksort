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
        private Transform? cruiserTransform;
        private Vector3 localPosition;
        private Quaternion shelfRotation;
        private bool originalKinematic;
        private bool initialized;

        internal static void Attach(GrabbableObject item, Transform cargoParent, Transform cruiserTransform)
        {
            if (item == null || cargoParent == null || cruiserTransform == null ||
                item.transform.parent != cargoParent || item.isHeld)
                return;

            var anchor = item.GetComponent<CruiserShelfAnchor>();
            if (anchor == null)
                anchor = item.gameObject.AddComponent<CruiserShelfAnchor>();
            anchor.Configure(item, cargoParent, cruiserTransform);
        }

        internal static void Detach(GrabbableObject item)
        {
            if (item == null) return;
            var anchor = item.GetComponent<CruiserShelfAnchor>();
            if (anchor != null)
                anchor.ReleaseForPickup();
        }

        private void Configure(GrabbableObject placedItem, Transform parent, Transform cruiser)
        {
            if (!initialized)
            {
                item = placedItem;
                body = placedItem.GetComponent<Rigidbody>();
                originalKinematic = body != null && body.isKinematic;
                initialized = true;
            }
            cargoParent = parent;
            cruiserTransform = cruiser;
            localPosition = placedItem.transform.localPosition;
            // Direct vehicle placement resets local rotation to zero and skips the
            // normal fall, which is when the game applies an item's resting tilt.
            // Apply that tilt relative to the vehicle, since its cargo physics
            // parent can have a different rotation from the visible shelf.
            Vector3 resting = placedItem.itemProperties.restingRotation;
            shelfRotation = Quaternion.Euler(resting.x, 0f, resting.z);
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
            initialized && item != null && cargoParent != null && cruiserTransform != null &&
            item.transform.parent == cargoParent &&
            !item.isHeld && !item.isPocketed && !item.deactivated;

        private void PinItem()
        {
            if (item == null || cruiserTransform == null) return;
            item.transform.localPosition = localPosition;
            item.transform.rotation = cruiserTransform.rotation * shelfRotation;
            item.targetFloorPosition = localPosition;
            item.startFallingPosition = localPosition;
            item.fallTime = 1.1f;
            item.reachedFloorTarget = true;
            item.hasHitGround = true;
            PinBody();
        }

        private void PinBody()
        {
            if (body == null || cargoParent == null || cruiserTransform == null) return;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            body.position = cargoParent.TransformPoint(localPosition);
            body.rotation = cruiserTransform.rotation * shelfRotation;
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
    // (matchRotationOfParent=true) at named shelf zones or saved CSS coordinates.
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
            if (!CruiserSorter.IsShelfSlot(placeObject, cruiserLocalPosition)) return;
            var region = cruiser.GetComponentInChildren<PlayerPhysicsRegion>();
            Transform cargoParent = region != null && region.allowDroppingItems && region.physicsTransform != null
                ? region.physicsTransform : cruiser.transform;
            CruiserShelfAnchor.Attach(placeObject, cargoParent, cruiser.transform);
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
