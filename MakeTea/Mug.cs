using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
namespace MakeTea
{
    internal class Mug : BlockLiquidContainerTopOpened
    {
        protected override void tryEatStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            bool handle = true;
            handle &= slot.Itemstack != null;
            var contentStack = GetContent(slot.Itemstack);
            handle = handle && contentStack != null && contentStack.StackSize > 0;
            handle = handle && (contentStack.Item?.WildCardMatch("teaportion-*") ?? false);
            handle = handle && (contentStack.Item.Attributes["makeTeaPortionProps"].Exists);
            handle = handle && byEntity.HasBehavior<EntityBehaviorTemporalStabilityAffected>();
            var oldSize = contentStack?.StackSize ?? 0;
            base.tryEatStop(secondsUsed, slot, byEntity);
            var newContent = GetContent(slot.Itemstack);
            var consumedSize = oldSize - (newContent?.StackSize ?? 0);
            if (handle && consumedSize > 0)
            {
                ItemSlot dummySlot = GetContentInDummySlot(slot, contentStack);
                var states = contentStack.Collectible.UpdateAndGetTransitionStates(api.World, dummySlot);
                // TODO: The way base.tryEatStop reads spoilstate seems not to respect liquids?? Verify and bug report
                var spoilState = states.FirstOrDefault(s => s.Props.Type == EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
                WaterTightContainableProps containableProps = GetContainableProps(contentStack);;
                var stabilityGain = contentStack.Item.Attributes["makeTeaPortionProps"]["stabilityGain"].AsFloat();
                var stabilityBehavior = byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
                stabilityBehavior.OwnStability += stabilityGain * consumedSize * (1f - spoilState) / containableProps.ItemsPerLitre;

            }
        }
    }
}
