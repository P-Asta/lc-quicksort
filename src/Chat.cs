using System;
using System.Linq;
using HarmonyLib;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using TMPro;

namespace QuickSort
{
    public static class Chat
    {
        [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
        [HarmonyPrefix]
        [HarmonyWrapSafe]
        public static bool OnChatSubmit(HUDManager __instance, InputAction.CallbackContext context)
        {
            if (!context.performed || !Player.Local.isTypingChat)
            {
                return true;
            }

            string text = __instance.chatTextField.text;

            if (text == "/help")
            {
                string text2 = "Commands: ";
                text2 += string.Join(", ", ChatCommand.Commands);
                Log.Chat(text2);
                CloseChat(__instance);
                return false;
            }

            if (text.StartsWith("/"))
            {
                text = text.Substring(1).Trim();

                foreach (ChatCommand command in ChatCommand.Commands)
                {
                    if (text.StartsWith(command.keyword))
                    {
                        CloseChat(__instance);
                        try
                        {
                            command.action(command.GetArgs(text));
                        }
                        catch (Exception e)
                        {
                            Log.Exception(e);
                        }
                        return false;
                    }
                }
            }

            return true;
        }

        public static void CloseChat(HUDManager instance)
        {
            instance.localPlayer.isTypingChat = false;
            instance.chatTextField.text = "";
            EventSystem.current.SetSelectedGameObject(null);
            instance.typingIndicator.enabled = false;
        }
    }
}

