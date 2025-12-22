using System.Collections.Generic;
using UnityEngine;
using GameNetcodeStuff;

namespace QuickSort
{
    public static class Extensions
    {
        public static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return s.ToLower().Replace(" ", "_").Replace("-", "_").Trim();
        }

        public static string Name(this GrabbableObject item)
        {
            return NormalizeName(item.itemProperties.itemName);
        }

        public static string Name(this Item item)
        {
            return NormalizeName(item.itemName);
        }
    }
}

