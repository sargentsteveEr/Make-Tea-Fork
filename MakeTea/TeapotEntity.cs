using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;
using System;

namespace MakeTea
{
    class ItemSlotTeapotInput : ItemSlot
    {

        public static bool CanHold(AssetLocation Code)
        {
            return Code.FirstCodePart() == "flower";
        }

        private IWorldAccessor World;

        public void Initialize(IWorldAccessor world)
        {
            World = world;
        }

        public ItemSlotTeapotInput(InventoryBase inventory, IWorldAccessor world) : base(inventory)
        {
            MaxSlotStackSize = 2;
            World = world;
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            Block block = sourceSlot?.Itemstack?.Block;
            return block != null && CanHold(block.Code);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            if (sourceSlot.Itemstack == null) return base.CanTakeFrom(sourceSlot, priority);
            return CanHold(sourceSlot);
        }

        public override bool CanTake()
        {
            if (itemstack == null || World == null) return base.CanTake();
            var temp = Itemstack.Collectible?.GetTemperature(World, Itemstack) ?? 0;
            return temp <= Teapot.ROOM_TEMPERATURE;
        }
    }

    class ItemSlotTeapotLiquid : ItemSlotLiquidOnly
    {
        public ItemSlotTeapotLiquid(InventoryBase inventory, float capacityLitres) : base(inventory, capacityLitres)
        {
        }

        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (Empty) base.ActivateSlot(sourceSlot, ref op);
            // prevent taking out liquid with bare hands
        }

        public override ItemStack TakeOut(int quantity)
        {
            return null;
        }

        public override ItemStack TakeOutWhole()
        {
            return null;
        }
    }

    internal class TeapotEntity : BlockEntityLiquidContainer, ITemperatureSensitive
    {
        public override string InventoryClassName => "teapot";

        private Teapot ownBlock;
        public ItemStack ItemStack;

        public LiquidTopOpenContainerProps Props;

        private GuiDialogTeapot invDialog;

        private double brewStartedHours = 0;
        private double lastUpdateHours  = 0;
        
        private const float PassiveCoolPerSecond = 0.1f;
        private bool IsBrewing => brewStartedHours > 0;
        public TeapotEntity() : base()
        {
            inventory = new InventoryGeneric(2, null, null, (id, self) =>
            {
                if (id == Teapot.ITEM_SLOT) return new ItemSlotTeapotInput(self, Api?.World);
                else return new ItemSlotTeapotLiquid(self, 2);
            })
            {
                BaseWeight = 1,
                OnGetSuitability = GetSuitability
            };


            inventory.SlotModified += Inventory_SlotModified;
        }

        private float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
        {
            // TODO: validate if this is useful for Teapot
            // prevent for example rot overflowing into the liquid slot, on a shift-click, when slot[0] is already full of 64 x rot
            if (targetSlot == inventory[Teapot.LIQUID_SLOT])
            {
                if (inventory[Teapot.ITEM_SLOT].StackSize > 0)
                {
                    ItemStack currentStack = inventory[Teapot.LIQUID_SLOT].Itemstack;
                    ItemStack testStack = sourceSlot.Itemstack;
                    if (currentStack.Collectible.Equals(currentStack, testStack, GlobalConstants.IgnoredStackAttributes)) return -1;
                }
            }

            // TODO: test InventoryBase.GetSuitability
            return (isMerge ? (inventory.BaseWeight + 3) : (inventory.BaseWeight + 1)) + (sourceSlot.Inventory is InventoryBasePlayer ? 1 : 0);
        }

        protected override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            // TODO: is this useful?
            if (atBlockFace == BlockFacing.UP) return inventory[Teapot.ITEM_SLOT];
            return null;
        }

        private ItemStack[] GetStacks()
        {
            var stacks = GetContentStacks(false);
            for (var i = 0; i < stacks.Length; i++)
            {
                if (stacks[i] == null)
                    stacks[i] = new ItemStack();
            }
            return stacks;
        }

        private void Update()
        {
            if (ItemStack != null && ownBlock != null)
            {
                ownBlock.SetContents(ItemStack, GetStacks());
                var state = ownBlock.Update(Api.World, ItemStack, Inventory, true);
                invDialog?.UpdateContents(state);

            }
        }

        private void Inventory_SlotModified(int slotId)
        {
            if (slotId == Teapot.ITEM_SLOT)
                CoolNow(0f); // Set item's temperature equal to the liquid's
            Update();
        }

        public void Pickup(IPlayer byPlayer)
        {
            if (ItemStack != null)
            {

                if (!byPlayer.Entity.TryGiveItemStack(ItemStack))
                {
                    Api.World.SpawnItemEntity(ItemStack, Pos);
                }
                Api.World.BlockAccessor.SetBlock(0, Pos);
            }
        }

        public void OnPlayerRightClick(IPlayer byPlayer)
        {
            if (ItemStack != null) {
                FindCurrentRecipe();
                if (Api.Side == EnumAppSide.Client)
                {
                    ToggleInventoryDialogClient(byPlayer);
                }
            }
        }

        public TeapotRecipe FindCurrentRecipe()
        {
            return ownBlock.FindMatchingRecipe(ItemStack, GetStacks());
        }

        protected void ToggleInventoryDialogClient(IPlayer byPlayer)
        {
            if (invDialog == null)
            {
                ICoreClientAPI capi = Api as ICoreClientAPI;
                invDialog = new GuiDialogTeapot(Lang.Get("maketea:teapot-dialog-title"), Inventory, Pos, Api as ICoreClientAPI);
                invDialog.OnClosed += () =>
                {
                    invDialog = null;
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Close, null);
                    capi.Network.SendPacketClient(Inventory.Close(byPlayer));
                };
                invDialog.OpenSound = AssetLocation.Create("maketea:sounds/blocks/teapotopen", Block.Code.Domain);
                invDialog.CloseSound = AssetLocation.Create("maketea:sounds/blocks/teapotclose", Block.Code.Domain);

                invDialog.TryOpen();
                capi.Network.SendPacketClient(Inventory.Open(byPlayer));
                capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Open, null);
            }
            else
            {
                invDialog.TryClose();
            }
        }


        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
            inventory.Pos = Pos;
            inventory.ResolveBlocksOrItems();



            (inventory[Teapot.ITEM_SLOT] as ItemSlotTeapotInput).Initialize(api.World);
            CoolNow(0f);
            ownBlock = Block as Teapot;

            Props = new LiquidTopOpenContainerProps();
            if (ownBlock?.Attributes?["liquidContainerProps"].Exists == true)
                Props = ownBlock.Attributes["liquidContainerProps"].AsObject<LiquidTopOpenContainerProps>(null, ownBlock.Code.Domain);

            Update();

            RegisterDelayedCallback(_ => RehookHeatSource(), 50);
            RegisterGameTickListener(RegularUpdate, 200);
        }
        private void RegularUpdate(float dt)
        {
            Update();

            // If we’re above ambient and not obviously on a very hot source,
            // bleed a bit toward ROOM_TEMPERATURE so UI doesn’t get “stuck”.
            var liq = inventory[Teapot.LIQUID_SLOT]?.Itemstack;
            if (liq?.Collectible != null)
            {
                var t = liq.Collectible.GetTemperature(Api.World, liq);
                if (t > Teapot.ROOM_TEMPERATURE + 0.1f)
                {
                    // Small, smooth step; this also keeps ItemStack in sync via CoolNow()
                    CoolNow(PassiveCoolPerSecond * dt);
                }
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (ItemStack != null)
            {
                tree["blockTree"] = ItemStack.Attributes;
            }

            tree.SetDouble("brewStartedHours", brewStartedHours);
            tree.SetDouble("lastUpdateHours",  lastUpdateHours);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            var attributes = tree["blockTree"] as ITreeAttribute;
            if (attributes != null)
            {
                ItemStack = new ItemStack(Block) { Attributes = attributes };
            }
            brewStartedHours = tree.GetDouble("brewStartedHours", 0);
            lastUpdateHours  = tree.GetDouble("lastUpdateHours",  0);
        }


        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (byItemStack != null)
            {

                // Deal with situation where the itemStack had some liquid contents, and BEContainer.OnBlockPlaced() placed this into the inputSlot not the liquidSlot
                ItemSlot inputSlot = Inventory[Teapot.ITEM_SLOT];
                ItemSlot liquidSlot = Inventory[Teapot.LIQUID_SLOT];
                if (!inputSlot.Empty && liquidSlot.Empty)
                {
                    var liqProps = BlockLiquidContainerBase.GetContainableProps(inputSlot.Itemstack);
                    if (liqProps != null)
                    {
                        Inventory.TryFlipItems(Teapot.LIQUID_SLOT, inputSlot);
                    }
                }
                ItemStack = byItemStack.GetEmptyClone();
                ItemStack.StackSize = 1;
            }
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);

            if (packetid < 1000)
            {
                Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);

                // Tell server to save this chunk to disk again
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos).MarkModified();

                return;
            }

            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                player.InventoryManager?.CloseInventory(Inventory);
            }

            if (packetid == (int)EnumBlockEntityPacketId.Open)
            {
                player.InventoryManager?.OpenInventory(Inventory);
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);

            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                (Api.World as IClientWorldAccessor).Player.InventoryManager.CloseInventory(Inventory);
                invDialog?.TryClose();
                invDialog?.Dispose();
                invDialog = null;
            }
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            invDialog?.TryClose();
            // base.OnBlockBroken(byPlayer); Don't drop contents, as they stay inside the teapot
        }

        public bool IsHot
        {
            get
            {
                var stack = inventory[Teapot.LIQUID_SLOT].Itemstack;
                return (stack?.Collectible?.GetTemperature(Api.World, stack) ?? 0) > Teapot.ROOM_TEMPERATURE;
            }
        }

        public void CoolNow(float amountRel)
        {
            var liquid = inventory[Teapot.LIQUID_SLOT]?.Itemstack;
            var temperature = liquid?.Collectible?.GetTemperature(Api.World, liquid) ?? 0;
            var newTemperature = Math.Max(Teapot.ROOM_TEMPERATURE, temperature - amountRel);

            foreach (var slot in inventory)
            {
                var stack = slot.Itemstack;
                if (stack?.Collectible != null)
                {
                    if (stack.Collectible.GetTemperature(Api.World, stack) == newTemperature) continue;
                    stack.Collectible.SetTemperature(Api.World, stack, newTemperature);
                    slot.MarkDirty();
                }
            }
            if (ItemStack?.Collectible != null)
            {
                ItemStack.Collectible.SetTemperature(Api.World, ItemStack, newTemperature);
            }
        }
        
        private void RehookHeatSource()
        {
            var posBelow = Pos.DownCopy();
            var beFirepit = Api?.World?.BlockAccessor?.GetBlockEntity(posBelow) as BlockEntityFirepit;
            if (beFirepit != null)
            {
                beFirepit.Inventory?[beFirepit.Inventory.Count - 1]?.MarkDirty();
                beFirepit.MarkDirty(true);
            }
            MarkDirty(true);
        }
    }
}
