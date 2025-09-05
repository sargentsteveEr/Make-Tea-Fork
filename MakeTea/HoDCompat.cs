// using System;
// using System.Linq;
// using Vintagestory.API.Common;
// using Vintagestory.API.Datastructures;

// namespace MakeTea
// {
//     internal static class HoDCompat
//     {
//         private static bool hodActive;
//         public static void SetActive(bool active) => hodActive = active;
//         public static bool IsActive => hodActive;

//         public static void StampSourceLiquidCode(ItemStack outTea, ItemStack inLiquid)
//         {
//             if (!hodActive || outTea?.Attributes == null || inLiquid?.Collectible == null) return;
//             outTea.Attributes.SetString("hodSourceLiquid", inLiquid.Collectible.Code?.ToShortString());
//         }

//         public static void CopyHydrationFromStack(ItemStack outTea, ItemStack inLiquid)
//         {
//             if (!hodActive || outTea?.Attributes == null || inLiquid?.Attributes == null) return;
//             CopyMatchingKeys(inLiquid.Attributes, outTea.Attributes);
//         }

//         public static void CacheItemJsonAttributes(ItemStack outTea, CollectibleObject liquidCollectible)
//         {
//             if (!hodActive || outTea?.Attributes == null || liquidCollectible?.Attributes == null) return;

//             IAttribute asAttr = liquidCollectible.Attributes.ToAttribute(); // JsonObject -> IAttribute
//             var tree = outTea.Attributes.GetOrAddTreeAttribute("hodInheritedItemAttrs") as ITreeAttribute;
//             if (asAttr is ITreeAttribute liquidItemTree)
//             {
//                 // replace or store
//                 outTea.Attributes["hodInheritedItemAttrs"] = liquidItemTree.Clone();
//             }
//         }

//         public static void SurfaceHydrationKeysForHoD(ItemStack teaStack)
//         {
//             if (!hodActive || teaStack?.Attributes == null) return;


//             // fallback
//             var inherited = teaStack.Attributes.GetTreeAttribute("hodInheritedItemAttrs");
//             if (inherited != null)
//             {
//                 CopyMatchingKeys(inherited, teaStack.Attributes, onlyIfAbsent: true);
//             }
//         }

//         // replace later with actual hydrateordiedrate states
//         private static bool LooksLikeHydrationKey(string key)
//         {
//             if (string.IsNullOrEmpty(key)) return false;
//             key = key.ToLowerInvariant();
//             return key.Contains("hydrate") || key.Contains("hydration") || key.Contains("thirst") || key.Contains("hod");
//         }

//         private static void CopyMatchingKeys(ITreeAttribute from, ITreeAttribute to, bool onlyIfAbsent = false)
//         {
//             foreach (var key in from.Keys)
//             {
//                 if (!LooksLikeHydrationKey(key)) continue;
//                 if (onlyIfAbsent && to.HasAttribute(key)) continue;
//                 to[key] = from[key]?.Clone();
//             }
//         }
//     }
// }
