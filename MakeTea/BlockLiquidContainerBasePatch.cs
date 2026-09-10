using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MakeTea;

// Useful article: https://harmony.pardeike.net/articles/patching-prefix.html
[HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop")]
public class BlockLiquidContainerBasePatch
{
    // cache reflection calls for better performance during gameplay
    private static readonly MethodInfo GetContentInDummySlotMethod = AccessTools.Method(typeof(BlockLiquidContainerBase), "GetContentInDummySlot");
    private static readonly FieldInfo ApiField = AccessTools.Field(typeof(BlockLiquidContainerBase), "api");

    [HarmonyPrefix]
    public static void TryEatStopPrefix(
        BlockLiquidContainerBase __instance, out TryEatStopState __state,
        float secondsUsed, ItemSlot slot, EntityAgent byEntity)
    {
        __state = new TryEatStopState();

        if (slot.Itemstack == null) return;

        var handle = true;
        handle &= slot.Itemstack != null;
        var contentStack = __instance.GetContent(slot.Itemstack);
        handle = handle && contentStack != null && contentStack.StackSize > 0;
        handle = handle && (contentStack.Item?.WildCardMatch("teaportion-*") ?? false);
        handle = handle && contentStack.Item.Attributes["makeTeaPortionProps"].Exists;
        handle = handle && byEntity.HasBehavior<EntityBehaviorTemporalStabilityAffected>();

        if (handle)
        {
            __state.OldSize = contentStack?.StackSize ?? 0;
            __state.OldSlotStackSize = slot.Itemstack?.StackSize ?? 0;
            __state.ContentStack = contentStack;
        }
    }

    [HarmonyPostfix]
    public static void TryEatStopPostfix(
        BlockLiquidContainerBase __instance, TryEatStopState __state,
        float secondsUsed, ItemSlot slot, EntityAgent byEntity)
    {
        if (__state.OldSize <= 0 || __state.ContentStack == null || slot.Itemstack == null || __state.OldSlotStackSize == 0) return;

        int currentSize;
        if (__state.OldSlotStackSize > (slot.Itemstack?.StackSize ?? 0))
        {
            // use a standard loop instead of LINQ for better performance
            ItemSlot slitSlot = null;
            var inventory = byEntity.ActiveHandItemSlot.Inventory;

            for (int i = 0; i < inventory.Count; i++)
            {
                var s = inventory[i];
                if (s != slot && inventory.DirtySlots.Contains(i) && s.Itemstack?.Id == slot.Itemstack?.Id)
                {
                    slitSlot = s;
                    break;
                }
            }

            currentSize = __instance.GetContent(slitSlot?.Itemstack)?.StackSize ?? 0;
        }
        else
        {
            currentSize = __instance.GetContent(slot.Itemstack)?.StackSize ?? 0;
        }

        var consumedSize = __state.OldSize - currentSize;
        if (consumedSize <= 0) return;

        // use the cached reflection fields
        var dummySlot = GetContentInDummySlotMethod?.Invoke(__instance, new object[] { slot, __state.ContentStack }) as ItemSlot;
        var api = ApiField?.GetValue(__instance) as ICoreAPI;

        if (api == null) return;

        var states = __state.ContentStack.Collectible.UpdateAndGetTransitionStates(api.World, dummySlot);
        
        // using a loop instead of LINQ FirstOrDefault to find the spoil state
        // should remove all the LINQ overhead too from the postFix method, not by much compared to other mods but every frame counts, yeah?
        float spoilState = 0f;
        if (states != null)
        {
            foreach (var s in states)
            {
                if (s.Props.Type == EnumTransitionType.Perish)
                {
                    spoilState = s.TransitionLevel;
                    break;
                }
            }
        }
        
        var containableProps = BlockLiquidContainerBase.GetContainableProps(__state.ContentStack);

        if (containableProps == null) return;

        var stabilityGain = __state.ContentStack.Item.Attributes["makeTeaPortionProps"]["stabilityGain"].AsFloat();
        var stabilityBehavior = byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        if (stabilityBehavior == null) return;

        var stabilityGainTotal = stabilityGain * consumedSize * Math.Max(0.0f, 1f - spoilState) / containableProps.ItemsPerLitre;
        
        stabilityBehavior.OwnStability += stabilityGainTotal;
    }

    public class TryEatStopState
    {
        public ItemStack ContentStack;
        public int OldSize;
        public int OldSlotStackSize;
    }
}