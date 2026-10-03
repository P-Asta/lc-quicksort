using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace QuickSort
{
    // Named messages let a client opt in without imposing QuickSort on everyone in
    // the lobby. Vanilla/other hosts simply ignore the request.
    internal static class ProfileNetwork
    {
        private const string RequestMessage = "pasta.quicksort.profile.v1.request";
        private const string SnapshotMessage = "pasta.quicksort.profile.v1.snapshot";
        private const float RetrySeconds = 3f;
        private const float HostScanSeconds = 2f;
        private const int MaxSerializedChars = 65536;

        private static NetworkManager? manager;
        private static float nextRequestAt;
        private static bool receivedSnapshot;
        private static float nextHostScanAt;
        private static string? lastHostSnapshot;

        internal static bool IsRemoteClientSession
        {
            get
            {
                var current = NetworkManager.Singleton;
                return current != null && current.IsConnectedClient && !current.IsServer;
            }
        }

        internal static void OnLocalPlayerCreated()
        {
            Tick();
        }

        internal static void Tick()
        {
            var current = NetworkManager.Singleton;
            if (manager != null && (manager != current || !manager.IsListening ||
                (!manager.IsServer && !manager.IsConnectedClient)))
                EndSession();

            if (manager == null && current != null && current.IsListening &&
                (current.IsServer || current.IsConnectedClient) &&
                StartOfRound.Instance != null && StartOfRound.Instance.localPlayerController != null)
                BeginSession(current);

            if (manager != null && manager.IsConnectedClient && !manager.IsServer &&
                !receivedSnapshot && Time.unscaledTime >= nextRequestAt)
            {
                SendRequest();
                nextRequestAt = Time.unscaledTime + RetrySeconds;
            }
            if (manager != null && manager.IsServer &&
                Time.unscaledTime >= nextHostScanAt)
            {
                nextHostScanAt = Time.unscaledTime + HostScanSeconds;
                BroadcastHostSnapshotIfChanged();
            }
            SortProfiles.ApplyPendingHost();
        }

        private static void BeginSession(NetworkManager current)
        {
            var messaging = current.CustomMessagingManager;
            if (messaging == null) return;
            try
            {
                messaging.RegisterNamedMessageHandler(RequestMessage, OnRequest);
                messaging.RegisterNamedMessageHandler(SnapshotMessage, OnSnapshot);
                current.OnClientDisconnectCallback += OnClientDisconnect;
                manager = current;
                receivedSnapshot = false;
                nextRequestAt = 0f;
                nextHostScanAt = 0f;
                lastHostSnapshot = null;
                QuickSort.Log.Info("Profile sync channel ready.");
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Could not initialize profile sync: {e.Message}");
                manager = null;
                current.OnClientDisconnectCallback -= OnClientDisconnect;
                try
                {
                    messaging.UnregisterNamedMessageHandler(RequestMessage);
                    messaging.UnregisterNamedMessageHandler(SnapshotMessage);
                }
                catch (Exception cleanupError)
                {
                    QuickSort.Log.Warning($"Could not reset profile sync channel: {cleanupError.Message}");
                }
            }
        }

        internal static void EndSession()
        {
            var previous = manager;
            manager = null;
            receivedSnapshot = false;
            nextRequestAt = 0f;
            nextHostScanAt = 0f;
            lastHostSnapshot = null;
            // All coroutines on this component are ship/cruiser sort operations. Stop
            // them before restoring settings so an old sort cannot resume in a new lobby.
            if (Plugin.sorterObject != null)
            {
                var sorter = Plugin.sorterObject.GetComponent<Sorter>();
                if (sorter != null) sorter.StopAllCoroutines();
            }
            Sorter.inProgress = false;
            if (previous != null)
            {
                try
                {
                    previous.OnClientDisconnectCallback -= OnClientDisconnect;
                    var messaging = previous.CustomMessagingManager;
                    if (messaging != null)
                    {
                        messaging.UnregisterNamedMessageHandler(RequestMessage);
                        messaging.UnregisterNamedMessageHandler(SnapshotMessage);
                    }
                }
                catch (Exception e)
                {
                    QuickSort.Log.Warning($"Could not close profile sync channel: {e.Message}");
                }
            }
            SortProfiles.EndHostSession();
        }

        private static void OnClientDisconnect(ulong clientId)
        {
            if (manager != null && !manager.IsServer &&
                (clientId == NetworkManager.ServerClientId || clientId == manager.LocalClientId))
                EndSession();
        }

        private static void SendRequest()
        {
            if (manager == null || !manager.IsConnectedClient || manager.IsServer) return;
            try
            {
                using var writer = new FastBufferWriter(1, Allocator.Temp);
                manager.CustomMessagingManager.SendNamedMessage(RequestMessage,
                    NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Could not request host profile: {e.Message}");
            }
        }

        private static void OnRequest(ulong senderClientId, FastBufferReader reader)
        {
            if (manager == null || manager != NetworkManager.Singleton || !manager.IsServer ||
                senderClientId == manager.LocalClientId) return;
            SendSnapshot(senderClientId);
        }

        private static void OnSnapshot(ulong senderClientId, FastBufferReader reader)
        {
            if (manager == null || manager != NetworkManager.Singleton || manager.IsServer ||
                !manager.IsConnectedClient || senderClientId != NetworkManager.ServerClientId ||
                reader.Length > MaxSerializedChars * 2 + 8) return;
            try
            {
                reader.ReadValueSafe(out string json);
                if (json.Length > MaxSerializedChars) return;
                if (SortProfiles.ReceiveHostSnapshot(json))
                    receivedSnapshot = true;
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Ignored invalid host profile message: {e.Message}");
            }
        }

        private static void SendSnapshot(ulong clientId)
        {
            if (manager == null || !manager.IsServer) return;
            if (!SortProfiles.TryCaptureNetworkSnapshot(out var json, out var error))
            {
                QuickSort.Log.Warning(error ?? "Could not capture host profile.");
                return;
            }
            SendSnapshot(clientId, json);
        }

        private static void SendSnapshot(ulong clientId, string json)
        {
            if (manager == null || !manager.IsServer) return;
            try
            {
                using var writer = new FastBufferWriter(json.Length * 2 + 8, Allocator.Temp);
                writer.WriteValueSafe(json);
                manager.CustomMessagingManager.SendNamedMessage(SnapshotMessage, clientId,
                    writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            catch (Exception e)
            {
                QuickSort.Log.Warning($"Could not send host profile: {e.Message}");
            }
        }

        internal static void BroadcastHostSnapshotIfHosting()
        {
            if (manager == null || !manager.IsServer) return;
            if (!SortProfiles.TryCaptureNetworkSnapshot(out var json, out var error))
            {
                QuickSort.Log.Warning(error ?? "Could not capture host profile.");
                return;
            }
            lastHostSnapshot = json;
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                if (clientId != manager.LocalClientId)
                    SendSnapshot(clientId, json);
            }
        }

        private static void BroadcastHostSnapshotIfChanged()
        {
            if (manager == null || !manager.IsServer || manager.ConnectedClientsIds.Count < 2)
                return;
            if (!SortProfiles.TryCaptureNetworkSnapshot(out var json, out _)) return;
            if (string.Equals(json, lastHostSnapshot, StringComparison.Ordinal)) return;
            lastHostSnapshot = json;
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                if (clientId != manager.LocalClientId)
                    SendSnapshot(clientId, json);
            }
        }
    }

    internal sealed class ProfileNetworkBehaviour : MonoBehaviour
    {
        private void Update() => ProfileNetwork.Tick();
        private void OnApplicationQuit() => ProfileNetwork.EndSession();
    }
}
