using System.Collections.Generic;
using UnityEngine;
using GameNetcodeStuff;

namespace QuickSort
{
    public static class Extensions
    {
        public static string Name(this GrabbableObject item)
        {
            return item.itemProperties.itemName.ToLower().Replace(" ", "_").Replace("-", "_");
        }

        public static string Name(this Item item)
        {
            return item.itemName.ToLower().Replace(" ", "_").Replace("-", "_");
        }
    }
}

