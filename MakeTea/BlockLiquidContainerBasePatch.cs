using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MakeTea;

// Useful article: https://harmony.pardeike.net/articles/patching-prefix.html
[HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop")]
public class BlockLiquidContainerBasePatch
{
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
            // We need to find the item that was split from the original stack.
            // I don't really know how to do this property, so this is my [JT] best attempt:
            // 1. look for dirty slots in player inventory,
            // 2. find slots matching the itemId, ignoring current slot, take first,
            // 3. check for content size there.
            var slitSlot = byEntity.ActiveHandItemSlot.Inventory.Where(
                (s, i) =>
                    s != slot &&
                    byEntity.ActiveHandItemSlot.Inventory.DirtySlots.Contains(i) &&
                    s.Itemstack?.Id == slot.Itemstack?.Id
                ).FirstOrDefault();
            currentSize = __instance.GetContent(slitSlot?.Itemstack)?.StackSize ?? 0;
        }
        else
        {
            currentSize = __instance.GetContent(slot.Itemstack)?.StackSize ?? 0;
        }

        var consumedSize = __state.OldSize - currentSize;
        if (consumedSize <= 0) return;

        var dummySlot = typeof(BlockLiquidContainerBase)
            .GetMethod("GetContentInDummySlot", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(__instance, [slot, __state.ContentStack]) as ItemSlot;
        var api = typeof(BlockLiquidContainerBase)
            .GetField("api", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(__instance) as ICoreAPI;

        if (api == null) return;

        // api.Logger.Debug(
        //     "MakeTeaMod: entity [{0}] consuming {1} units of [{2}] liquid from container [{3}]",
        //     byEntity.GetName(), consumedSize, __state.ContentStack.GetName(), slot.Itemstack.GetName());

        var states = __state.ContentStack.Collectible.UpdateAndGetTransitionStates(api.World, dummySlot);
        var spoilState = states.FirstOrDefault(s => s.Props.Type == EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
        var containableProps = BlockLiquidContainerBase.GetContainableProps(__state.ContentStack);

        if (containableProps == null) return;

        var stabilityGain = __state.ContentStack.Item.Attributes["makeTeaPortionProps"]["stabilityGain"].AsFloat();
        var stabilityBehavior = byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        if (stabilityBehavior == null) return;

        var stabilityGainTotal = stabilityGain * consumedSize * Math.Max(0.0f, 1f - spoilState) / containableProps.ItemsPerLitre;
        // api.Logger.Debug(
        //     "MakeTeaMod: entity [{0}] with stability {1:F3}, gain +{2:F3} stability using [{3}] liquid ({4:P1} spoiled) from container [{5}]",
        //     byEntity.GetName(), stabilityBehavior.OwnStability, stabilityGainTotal, __state.ContentStack.GetName(),
        //     spoilState, slot.Itemstack.GetName());

        stabilityBehavior.OwnStability += stabilityGainTotal;
    }

    public class TryEatStopState
    {
        public ItemStack ContentStack;
        public int OldSize;
        public int OldSlotStackSize;
    }
}
