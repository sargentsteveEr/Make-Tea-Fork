using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using HarmonyLib;
using Vintagestory.GameContent;

namespace MakeTea
{
    [HarmonyPatchCategory("Other")]
    [HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.SplitStackAndPerformAction))]
    internal class PreventDupingPatch
    {

        [HarmonyPrefix]
        public static bool Prefix(ref int __result, Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
        {
            if (slot.Itemstack?.StackSize == 1)
            {
                int moved = action(slot.Itemstack);

                if (moved > 0)
                {
                    int maxstacksize = slot.Itemstack.Collectible.MaxStackSize;

                    (byEntity as EntityPlayer)?.WalkInventory((pslot) =>
                    {
                        if (pslot.Empty || pslot is ItemSlotCreative || pslot.StackSize == pslot.Itemstack.Collectible.MaxStackSize) return true;
                        // the original passes DirectMerge here, even though it's an AutoMerge
                        int mergableq = slot.Itemstack.Collectible.GetMergableQuantity(slot.Itemstack, pslot.Itemstack, EnumMergePriority.AutoMerge);
                        if (mergableq == 0) return true;

                        var selfLiqBlock = slot.Itemstack.Collectible as BlockLiquidContainerBase;
                        var invLiqBlock = pslot.Itemstack.Collectible as BlockLiquidContainerBase;

                        if ((selfLiqBlock?.GetContent(slot.Itemstack)?.StackSize ?? 0) != (invLiqBlock?.GetContent(pslot.Itemstack)?.StackSize ?? 0)) return true;

                        // this assumes that all mergeable items are identical, which is not the case
                        // when merging merge-interactable containers like the teapots, or other container blocks, this can result in duping
                        slot.Itemstack.StackSize += mergableq;
                        pslot.TakeOut(mergableq);

                        slot.MarkDirty();
                        pslot.MarkDirty();
                        return true;
                    });
                }

                __result = moved;
                return false;
            }
            return true;
        }
    }
}
