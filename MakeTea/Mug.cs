using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MakeTea
{
    internal class Mug : BlockLiquidContainerTopOpened
    {
        public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
        {
            if (priority == EnumMergePriority.AutoMerge || priority == EnumMergePriority.DirectMerge)
            {
                var sinkContent = GetContent(sinkStack);
                var srcContent  = GetContent(sourceStack);

                bool sinkEmpty = sinkContent == null || sinkContent.StackSize <= 0;
                bool srcEmpty  = srcContent  == null || srcContent.StackSize  <= 0;

                // Only allow background merging when BOTH are empty
                if (!(sinkEmpty && srcEmpty))
                {
                    return 0;
                }
            }

            return base.GetMergableQuantity(sinkStack, sourceStack, priority);
        }
    }
}
