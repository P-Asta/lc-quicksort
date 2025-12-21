using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine;

namespace QuickSort
{
    public class Log
    {
        private static ManualLogSource _log;

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        public static void NotifyPlayer(string header, string body = "", bool isWarning = false)
        {
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.DisplayTip(header, body, isWarning, false, "LC_Tip1");
            }
            Debug(header);
            Debug(body);
        }

        public static void Chat(string body, string color = "FFFFFF")
        {
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.AddChatMessage("<color=#" + color + ">[Pasta] " + body + "</color>", "", -1, false);
            }
            Debug(body);
        }

        public static void ConfirmSound()
        {
            if (HUDManager.Instance != null && GameNetworkManager.Instance != null)
            {
                HUDManager.Instance.UIAudio.PlayOneShot(GameNetworkManager.Instance.buttonTuneSFX);
            }
        }

        public static void Exception(System.Exception e)
        {
            string message = e.Message;
            string stackTrace = e.StackTrace;
            _log.LogError((object)message);
            _log.LogError((object)stackTrace);
        }

        public static void Error(params object[] objects)
        {
            _log.LogError((object)string.Join(" ", objects));
        }

        public static void Warning(params object[] objects)
        {
            _log.LogWarning((object)string.Join(" ", objects));
        }

        public static void Info(params object[] objects)
        {
            _log.LogInfo((object)string.Join(" ", objects));
        }

        public static void Debug(params object[] objects)
        {
            _log.LogDebug((object)string.Join(" ", objects));
        }
    }
}

