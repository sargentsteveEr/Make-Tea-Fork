using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using System.Linq;
using System.Text;
using Vintagestory.API.Config;

namespace MakeTea
{
    enum BrewConvertState : int
    {
        Inactive = 0,
        Brewing = 1,
        Brewed = 2,
    }

    internal class Teapot : BlockLiquidContainerTopOpened, IInFirepitRendererSupplier
    {
        public const int LIQUID_SLOT = 0;
        public const int ITEM_SLOT = 1;
        public const int ROOM_TEMPERATURE = 20; // all vanilla containers hard-code it

        public override bool CanDrinkFrom => true;

        private static double Now(ICoreAPI api) // returns time in real-life minutes
        {
            return api.World.Calendar.ElapsedSeconds / 60d / api.World.Calendar.SpeedOfTime / api.World.Calendar.CalendarSpeedMul;
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            HeldPriorityInteract = true;

            if (Attributes?["liquidContainerProps"]?.Exists == true)
            {
                // Note: capacityLitresFromAttributes is defined in the base class
                capacityLitresFromAttributes = Attributes["liquidContainerProps"]["capacityLitres"].AsFloat(2f);
            }

            if (api.Side != EnumAppSide.Client) return;
            ICoreClientAPI capi = api as ICoreClientAPI;

            var teapotInteractions = ObjectCacheUtil.GetOrCreate(api, "teaMakingbase", () =>
            {
                var herbStacks = new List<ItemStack>();
                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    if (ItemSlotTeapotInput.CanHold(obj.Code))
                    {
                        var stacks = obj.GetHandBookStacks(capi);
                        if (stacks != null) herbStacks.AddRange(stacks);
                    }
                }

                return new WorldInteraction[] {
                    new()
                    {
                        ActionLangCode = "maketea:blockhelp-teapot-herb-rightclick",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = herbStacks.ToArray(),
                    },
                    new()
                    {
                        ActionLangCode = "maketea:blockhelp-teapot-pickup",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true,
                        Itemstacks = Array.Empty<ItemStack>(),
                        HotKeyCode = "shift",
                    },
                };
            });

            interactions = interactions.Concat(teapotInteractions).ToArray();
        }

        public override void OnHeldInteractStart(
            ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
            bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent) return;

            var world = byEntity.World;
            var ba = world.BlockAccessor;

            // 1) FIREPIT PATH

            if (blockSel != null)
            {
                var beFirepit = ba.GetBlockEntity(blockSel.Position) as BlockEntityFirepit;
                if (beFirepit != null)
                {
                    if (byEntity.Controls.ShiftKey)
                    {
                        var inv = beFirepit.Inventory;
                        ItemSlot target = null;

                        var op = new ItemStackMergeOperation(
                            world,
                            EnumMouseButton.Right,
                            (EnumModifierKey)0,                 // avoid EnumModifierKey.None on older builds
                            EnumMergePriority.ConfirmedMerge,
                            slot.StackSize
                        );
                        var best = inv.GetBestSuitedSlot(slot, op);

                        if (best != null && best.weight > 0)
                        {
                            int idx = inv.GetSlotId(best.slot);
                            if (idx != 0) target = best.slot;   // skip vanilla fuel slot (index 0)
                        }

                        if (target == null)
                        {
                            for (int i = 0; i < inv.Count; i++)
                            {
                                if (i == 0) continue;            // skip fuel slot
                                var s = inv[i];
                                if (!s.Empty) continue;
                                if (!s.CanHold(slot)) continue;
                                target = s;
                                break;
                            }
                        }

                        // last resort: accept whatever 'best' suggested (may be fuel if nothing else fits)
                        if (target == null && best != null && best.weight > 0)
                        {
                            target = best.slot;
                        }

                        if (target != null)
                        {
                            int moved = slot.TryPutInto(world, target, slot.StackSize);
                            if (moved > 0)
                            {
                                slot.MarkDirty();
                                int sid = inv.GetSlotId(target);
                                if (sid >= 0) inv.MarkSlotDirty(sid);
                                beFirepit.MarkDirty(true);

                                handling = EnumHandHandling.PreventDefaultAction; // consume: no drink animation
                                return;
                            }
                        }

                        // don't let HoD steal it for drinking
                        handling = EnumHandHandling.PreventDefaultAction;
                        return;
                    }

                    // not sneaking over firepit = let base handle (pour, UI, etc.)
                    base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                    return;
                }
            }

            // 2) SHIFT + PLACE ON GROUND
            if (byEntity.Controls.ShiftKey)
            {
                if (blockSel != null)
                {
                    var player = (byEntity as EntityPlayer)?.Player;
                    if (player != null && slot?.Itemstack != null)
                    {
                        BlockPos placePos = blockSel.Position;
                        if (ba.GetBlock(placePos).Replaceable < 6000)
                        {
                            placePos = placePos.Copy();
                            placePos.Add(blockSel.Face);
                        }

                        var placeSel = new BlockSelection
                        {
                            Position = placePos,
                            Face = blockSel.Face,
                            HitPosition = blockSel.HitPosition
                        };

                        string fail = null;
                        if (TryPlaceBlock(world, player, slot.Itemstack, placeSel, ref fail))
                        {
                            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                            {
                                slot.TakeOut(1);
                                slot.MarkDirty();
                            }

                            handling = EnumHandHandling.PreventDefaultAction; // consume: no drink animation
                            return;
                        }
                    }
                }

                // still consume to avoid any drink animation
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }


            // 3) NO SHIFT
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        }


        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack stack, BlockSelection blockSel, ref string failureCode)
    {
        if (byPlayer?.Entity?.Controls?.ShiftKey != true)
        {
            return false;
        }
        return base.TryPlaceBlock(world, byPlayer, stack, blockSel, ref failureCode);
    }



        public override bool OnHeldInteractStep(
            float secondsUsed, ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel)
        {
            if (byEntity.Controls.ShiftKey) return false; // block “use” only while sneaking
            return base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
        }


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
                WaterTightContainableProps containableProps = GetContainableProps(contentStack); ;
                var stabilityGain = contentStack.Item.Attributes["makeTeaPortionProps"]["stabilityGain"].AsFloat();
                var stabilityBehavior = byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
                stabilityBehavior.OwnStability += stabilityGain * consumedSize * (1f - spoilState) / containableProps.ItemsPerLitre;

            }
        }



        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel != null && !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return false;
            }

            TeapotEntity teapotEntity = null;
            if (blockSel?.Position != null)
            {
                teapotEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position) as TeapotEntity;
            }

            bool handled = false;

            if (byPlayer.Entity.Controls.ShiftKey && byPlayer.Entity.RightHandItemSlot.Empty)
            {
                teapotEntity?.Pickup(byPlayer);
                handled = true;
            }

            handled = handled || base.OnBlockInteractStart(world, byPlayer, blockSel);

            if (!handled && blockSel?.Position != null)
            {
                teapotEntity?.OnPlayerRightClick(byPlayer);
                handled = true;
            }

            return handled;
        }

        public override ItemStack[] GetContents(IWorldAccessor world, ItemStack itemstack)
        {
            return base.GetContents(world, itemstack).Select(s => (s != null && s.StackSize == 0) ? null : s).ToArray();
        }

        public ItemStack[] GetStacks(IWorldAccessor world, ItemStack itemstack)
        {
            return base.GetContents(world, itemstack);
        }

        private static ItemStack[] PatchContents(ItemStack[] stacks, ItemStack fillInValue)
        {
            if (stacks.Length == 2)
                return stacks;

            // patch stacks that come from BaseLiquidContainer's SetContent
            // so it doesn't override the item slot contents
            var patchedStacks = new ItemStack[2]
            {
                fillInValue,
                fillInValue,
            };
            if (stacks.Length > 0)
            {
                var stack = stacks[LIQUID_SLOT];
                if (stack?.Block?.IsLiquid() ?? true)
                    patchedStacks[LIQUID_SLOT] = stack;
                else
                    patchedStacks[ITEM_SLOT] = stack;
            }
            return patchedStacks;
        }

        public override void SetContents(ItemStack containerStack, ItemStack[] stacks)
        {
            stacks ??= Array.Empty<ItemStack>();
            if (stacks.Length < 2)
            {
                var patchedStacks = PatchContents(stacks, null);
                if (stacks.Length == 0)
                {
                    patchedStacks[LIQUID_SLOT] = new ItemStack();
                }
                stacks = patchedStacks;
            }
            var currentContents = PatchContents(GetContents(api.World, containerStack), new ItemStack());
            for (var i = 0; i < stacks.Length; i++)
            {
                if (stacks[i] != null)
                    currentContents[i] = stacks[i];
            }
            base.SetContents(containerStack, currentContents);
        }

        private MakeTeaModSystem GetModSystem()
        {
            return api.ModLoader.GetModSystem<MakeTeaModSystem>();
        }

        private const string CURRENT_RECIPE_ATTRIBUTE = "currentRecipeId";

        public TeapotRecipe GetCurrentRecipe(ItemStack stack)
        {
            var id = stack.Attributes.GetString(CURRENT_RECIPE_ATTRIBUTE);
            if (id != null)
                return GetModSystem().GetTeapotRecipeById(id);
            else
                return null;
        }

        public static void SetCurrentRecipe(ItemStack stack, TeapotRecipe recipe)
        {
            var code = recipe?.Code;
            if (code == null) // attribute serializer crashes when encountering null "strings"
                stack.Attributes.RemoveAttribute(CURRENT_RECIPE_ATTRIBUTE);
            else
                stack.Attributes.SetString(CURRENT_RECIPE_ATTRIBUTE, recipe.Code);
        }

        private const string CRAFTING_TIME_ATTRIBUTE = "craftingTime";

        public static double GetCraftingTime(ItemStack stack, double defaultVlaue = 0.0)
        {
            return stack.Attributes.TryGetDouble(CRAFTING_TIME_ATTRIBUTE) ?? defaultVlaue;
        }

        public static void SetCraftingTime(ItemStack stack, double hours)
        {
            stack.Attributes.SetDouble(CRAFTING_TIME_ATTRIBUTE, hours);
        }

        private const string CRAFTING_QUALITY_ATTRIBUTE = "craftingQuality";

        public static double GetCraftingQuality(ItemStack stack)
        {
            return stack.Attributes.TryGetDouble(CRAFTING_QUALITY_ATTRIBUTE) ?? 0.0;
        }

        public static void SetCraftingQuality(ItemStack stack, double hours)
        {
            stack.Attributes.SetDouble(CRAFTING_QUALITY_ATTRIBUTE, hours);
        }

        private float GetLiquidTemperature(ItemStack container)
        {
            return GetTemperature(api.World, container);
        }

        public TeapotRecipe FindMatchingRecipe(ItemStack stack, ItemStack[] inventory)
        {
            if (inventory.Length < 2 || inventory[LIQUID_SLOT] == null || inventory[ITEM_SLOT] == null) return null;

            // NEW: normalize order so index 0 is the liquid
            var norm = (inventory[LIQUID_SLOT].Collectible?.IsLiquid() ?? false)
                ? inventory
                : new ItemStack[] { inventory[ITEM_SLOT], inventory[LIQUID_SLOT] };

            float temperature = GetLiquidTemperature(stack);

            TeapotRecipe foundRecipe = null;
            foreach (var recipe in GetModSystem().GetTeapotRecipes())
            {
                if (recipe.Matches(norm, temperature, out float outsize))
                {
                    foundRecipe = recipe;
                    break;
                }
            }
            if (foundRecipe != GetCurrentRecipe(stack))
            {
                SetCurrentRecipe(stack, foundRecipe);
                SetCraftingQuality(stack, 0);
                SetCraftingTime(stack, Now(api));
            }
            return foundRecipe;
        }

        private InventoryBase MakeTemporaryInventory(ItemStack[] contents)
        {
            var inventory = new InventoryGeneric(2, null, null, (id, self) =>
            {
                if (id == ITEM_SLOT) return new ItemSlotTeapotInput(self, api.World);
                else return new ItemSlotLiquidOnly(self, 2);
            })
            {
                BaseWeight = 1
            };
            if (contents.Length > 2)
                api.Logger.Warning("Teapot content has more slots than expected");
            if (contents.Length == 1)
            {
                var stack = contents[0];
                if (stack?.Block?.IsLiquid() ?? stack?.Item?.IsLiquid() ?? false)
                    inventory[LIQUID_SLOT].Itemstack = stack;
                else
                    inventory[ITEM_SLOT].Itemstack = stack;
            }
            else
            {
                for (var i = 0; i < contents.Length; i++)
                    inventory[i].Itemstack = contents[i];
            }
            return inventory;
        }

        private static TransitionState MakeTransitionState(double currentCraftingDuration, double totalCraftingDuration, BrewConvertState state)
        {
            return new TransitionState()
            {
                Props = new TransitionableProperties() { Type = EnumTransitionType.Convert },
                FreshHoursLeft = 0,
                FreshHours = 0,
                TransitionHours = (float)totalCraftingDuration,
                TransitionedHours = (float)currentCraftingDuration,
                TransitionLevel = (int)state,
            };
        }

        public override bool RequiresTransitionableTicking(IWorldAccessor world, ItemStack container)
        {
            return base.RequiresTransitionableTicking(world, container) || FindMatchingRecipe(container, GetStacks(world, container)) != null;
        }

        private double? LastUpdate = null;

        public TransitionState Update(IWorldAccessor world, ItemStack potStack, InventoryBase inventory, bool write)
        {
            var contents = GetStacks(world, potStack);
            var currentRecipe = FindMatchingRecipe(potStack, contents);
            if (currentRecipe == null) return MakeTransitionState(0, 1, BrewConvertState.Inactive);
            var now = Now(api); ;
            var craftingTime = GetCraftingTime(potStack, now);
            var craftingQuality = GetCraftingQuality(potStack);
            // handle negative crafting duration, as client-side time can drift very far from server time
            double craftingDuration = Math.Max(0d, Math.Min(currentRecipe.Duration, now - craftingTime));
            double dt = Math.Min(now - (LastUpdate ?? now), currentRecipe.Duration - craftingDuration);
            LastUpdate = now;
            var temperature = GetLiquidTemperature(potStack);
            inventory ??= MakeTemporaryInventory(contents);
            bool crafted = currentRecipe.TryCraft(inventory, temperature, craftingDuration, craftingQuality);
            if (write)
            {
                if (crafted)
                {
                    SetCurrentRecipe(potStack, null);
                    for (int i = 0; i < inventory.Count; i++)
                        contents[i] = inventory[i].Itemstack ?? new ItemStack();
                    SetContents(potStack, contents);
                }
                else
                {
                    ItemStack liquidStack = contents[LIQUID_SLOT];
                    if (liquidStack == null) return null;
                    double match = currentRecipe.TemperatureMatch(temperature);
                    SetCraftingQuality(potStack, craftingQuality + match * dt);
                }
            }
            return MakeTransitionState(craftingDuration, currentRecipe.Duration, crafted ? BrewConvertState.Brewed : BrewConvertState.Brewing);
        }

        public override void SetTemperature(IWorldAccessor world, ItemStack itemstack, float temperature, bool delayCooldown = true)
        {
            base.SetTemperature(world, itemstack, temperature, delayCooldown);
            BlockUpdate(world, itemstack, null);
        }

        private TransitionState BlockUpdate(IWorldAccessor world, ItemStack container, ItemSlot inslot)
        {
            var state = Update(world, container, null, true); // only write attributes on server to prevent race condition fighting
            if (world.Side == EnumAppSide.Server && state != null && state.TransitionLevel >= (int)BrewConvertState.Brewed)
                inslot?.MarkDirty();
            return state;
        }

        public override TransitionState[] UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot inslot)
        {
            var states = base.UpdateAndGetTransitionStates(world, inslot) ?? new TransitionState[0];
            var state = BlockUpdate(world, inslot.Itemstack, inslot);
            if (state == null) return states;
            else return states.Append(state).ToArray();
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            StringBuilder output = new StringBuilder();
            Teapot teapotBlock = world.BlockAccessor.GetBlock(pos) as Teapot;
            if (world.BlockAccessor.GetBlockEntity(pos) is TeapotEntity teapotEntity)
            {
                output.Append(GetItemInfo(teapotEntity.GetContentStacks(false)));
                AddLiquidPerishInfo(teapotEntity.Inventory[LIQUID_SLOT], output);
                var stack = teapotEntity.ItemStack;
                if (stack != null)
                {
                    AddBrewInfo(stack, output);
                    AddTemperatureInfo(stack, output);
                }
            }

            StringBuilder behaviourOutput = new StringBuilder();
            foreach (BlockBehavior bh in BlockBehaviors)
            {
                behaviourOutput.Append(bh.GetPlacedBlockInfo(world, pos, forPlayer));
            }
            if (behaviourOutput.Length > 0)
            {
                output.AppendLine(); // Insert a blank line if there is more to add (e.g. reinforceable)
                output.Append(behaviourOutput);
            }

            return output.ToString();
        }

        private void AddTemperatureInfo(ItemStack stack, StringBuilder output)
        {
            var temperature = GetLiquidTemperature(stack);
            if (temperature > Teapot.ROOM_TEMPERATURE)
            {
                output.AppendLine(Lang.Get("maketea:temperature-info", Math.Round(temperature, 0)));
            }
        }

        private void AddBrewInfo(ItemStack stack, StringBuilder output)
        {
            var stacks = GetStacks(api.World, stack);
            var dummyInv = new DummyInventory(api);
            ItemSlot dummySlot = new DummySlot(stack, dummyInv);
            TransitionState[] states = stack.Collectible.UpdateAndGetTransitionStates(api.World, dummySlot);
            var recipe = FindMatchingRecipe(stack, stacks);
            if (recipe == null) return;
            var temperatureMatch = recipe.TemperatureMatch(GetLiquidTemperature(stack));
            if (states != null && !dummySlot.Empty)
            {
                foreach (var state in states)
                {
                    TransitionableProperties props = state.Props;
                    if (props.Type != EnumTransitionType.Convert || state.TransitionLevel != (int)BrewConvertState.Brewing)
                        continue;
                    var ratio = state.TransitionedHours / state.TransitionHours;
                    if (ratio == 0)
                        continue;

                    // matches colors in GuiDialogTeapot
                    var red = new Vec3d(0.6, 0.2, 0.2);
                    var green = new Vec3d(0.2, 0.6, 0.2);
                    var color = (temperatureMatch * green + (1 - temperatureMatch) * red) * 255;
                    var colorString = $"#{(int)(color.X):x2}{(int)(color.Y):x2}{(int)(color.Z):x2}";
                    output.AppendLine(Lang.Get("maketea:teapot-brewing-progress", (int)Math.Round(ratio * 100f), colorString));
                }
            }
        }

        private void AddLiquidPerishInfo(ItemSlot slot, StringBuilder output)
        {
            var liquidStack = GetContent(slot.Itemstack);
            if (liquidStack != null)
            {
                var dummyslot = GetContentInDummySlot(slot, liquidStack);
                TransitionState[] states = liquidStack.Collectible.UpdateAndGetTransitionStates(api.World, dummyslot);
                if (states != null && !dummyslot.Empty)
                {
                    bool nowSpoiling = false;
                    foreach (var state in states)
                    {
                        nowSpoiling |= AppendPerishableInfoText(dummyslot, output, api.World, state, nowSpoiling) > 0;
                    }
                }
            }
        }

        public override void GetContentInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
        {
            dsc.Append(GetItemInfo(GetStacks(world, inSlot.Itemstack)));
            AddLiquidPerishInfo(inSlot, dsc);
            AddBrewInfo(inSlot.Itemstack, dsc);
        }

        private static StringBuilder GetItemInfo(ItemStack[] inventory)
        {
            var result = new StringBuilder();
            if (inventory != null)
            {
                foreach (var stack in inventory)
                {
                    if (stack == null || stack.StackSize == 0 || stack.Collectible == null) continue;
                    if (stack.Collectible.IsLiquid())
                    {
                        WaterTightContainableProps containableProps = GetContainableProps(stack);
                        var liters = stack.StackSize / containableProps.ItemsPerLitre;
                        string incontainerrname = Lang.Get(stack.Collectible.Code.Domain + ":incontainer-" + stack.Class.ToString().ToLowerInvariant() + "-" + stack.Collectible.Code.Path);
                        result.AppendLine(Lang.Get("maketea:litres-of", liters, incontainerrname));
                    }
                    else
                    {
                        var name = stack.Collectible.GetHeldItemName(stack);
                        result.AppendLine(Lang.Get("maketea:multiple-of", name, stack.StackSize));
                    }
                }
            }
            if (result.Length == 0) result.AppendLine(Lang.Get("maketea:empty"));
            return result;
        }

        public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
        {
            if (priority == EnumMergePriority.DirectMerge && (sinkStack?.Block is ILiquidSink || sourceStack?.Block is ILiquidSink))
                return Math.Clamp(MaxStackSize - sinkStack.StackSize, 1, sourceStack.StackSize);
            return base.GetMergableQuantity(sinkStack, sourceStack, priority);
        }

        public override void TryMergeStacks(ItemStackMergeOperation op)
        {
            if (op.CurrentPriority != EnumMergePriority.DirectMerge)
            {
                if (Math.Min(op.SinkSlot.Itemstack.Collectible.MaxStackSize - op.SinkSlot.Itemstack.StackSize, op.SourceSlot.Itemstack.StackSize) > 0) base.TryMergeStacks(op);
                return;
            }

            op.MovableQuantity = GetMergableQuantity(op.SinkSlot.Itemstack, op.SourceSlot.Itemstack, op.CurrentPriority);

            ItemStack bufferStack = null;
            if (op.SourceSlot.Itemstack.StackSize > 1)
                bufferStack = op.SourceSlot.TakeOut(Math.Clamp(op.SourceSlot.StackSize - op.MovableQuantity, 0, op.SourceSlot.StackSize));

            if (ServeIntoStack(op.SourceSlot, op.SinkSlot, op.World))
            {
                if (!op.ActingPlayer.Entity.TryGiveItemStack(bufferStack))
                    op.World.SpawnItemEntity(bufferStack, op.ActingPlayer.Entity.Pos.AsBlockPos);
            }
            else
            {
                DummySlot bufferSlot = new(bufferStack);
                bufferSlot.TryPutInto(op.World, op.SourceSlot, bufferSlot.StackSize);
            }
        }

        private bool ServeIntoStack(ItemSlot sinkSlot, ItemSlot sourceSlot, IWorldAccessor world)
        {
            var sourceStack = sourceSlot.Itemstack;
            var sinkStack = sinkSlot.Itemstack;

            if (sourceStack?.Block is ILiquidSource && sinkStack?.Block is ILiquidSink)
            {
                var sourceBlock = (sourceStack.Block is Teapot) ? sourceStack.Block as Teapot : sourceStack.Block as ILiquidSource;
                var sourceLiters = sourceBlock.GetCurrentLitres(sourceStack);
                var sourceContent = sourceBlock.GetContent(sourceStack);

                var sinkBlock = sinkStack.Block as ILiquidSink;
                var sinkContent = sinkBlock.GetContent(sinkStack);
                var sinkLiters = sinkBlock.GetCurrentLitres(sinkStack);

                if (sourceLiters == 0 && sinkLiters == 0) return false;
                if (sourceLiters > 0 && sinkLiters > 0 && !sinkContent.Equals(world, sourceContent, GlobalConstants.IgnoredStackAttributes))
                    return false;

                // if the teapot is empty, or the bowl is full, allow pouring in water
                if (sourceLiters == 0 || sinkLiters == sinkBlock.CapacityLitres)
                {
                    if (sourceBlock is not ILiquidSink || sinkBlock is not ILiquidSource) return false;
                    (sourceStack, sinkStack) = (sinkStack, sourceStack);
                    (sourceBlock, sinkBlock) = (sinkBlock as ILiquidSource, sourceBlock as ILiquidSink);
                    (sourceLiters, sinkLiters) = (sinkLiters, sourceLiters);
                    (sourceContent, sinkContent) = (sinkContent, sourceContent);
                }

                float litersToTransfer = Math.Min(sinkBlock.CapacityLitres - sinkLiters, sourceLiters);
                var sizeToTransfer = (int)Math.Floor(sourceContent.StackSize * litersToTransfer / sourceLiters);
                var buffer = sourceBlock.TryTakeContent(sourceStack, sizeToTransfer);
                var putQuantity = sinkBlock.TryPutLiquid(sinkStack, buffer, sourceLiters);
                sinkSlot.MarkDirty();
                sourceSlot.MarkDirty();
                return putQuantity > 0;
            }
            else if (sourceStack?.Block is Teapot && sinkStack?.Block != null)
            {
                // TODO: create a set of all tea ingredients from the recipes instead
                if (!ItemSlotTeapotInput.CanHold(sinkStack.Block.Code)) return false;
                var teapotBlock = sourceStack.Block as Teapot;
                var contents = teapotBlock.GetStacks(world, sourceStack);
                var itemStack = contents.ElementAtOrDefault(ITEM_SLOT);
                if (itemStack == null || itemStack.StackSize == 0)
                {
                    itemStack = sinkStack.Clone();
                    itemStack.StackSize = 0;
                }
                var itemDummySlot = new ItemSlotTeapotInput(new DummyInventory(api, 1), api.World)
                {
                    Itemstack = itemStack
                };
                var op = new ItemStackMergeOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.ConfirmedMerge, sourceStack.StackSize)
                {
                    // This operation is inverted compared to pouring tea from teapot
                    SourceSlot = sinkSlot,
                    SinkSlot = itemDummySlot
                };
                var oldSourceSize = sourceStack.StackSize;
                itemStack.Collectible.TryMergeStacks(op);
                var newContents = new ItemStack[2];
                newContents[ITEM_SLOT] = itemDummySlot.Itemstack;
                newContents[LIQUID_SLOT] = contents.ElementAtOrDefault(LIQUID_SLOT) ?? new ItemStack();
                teapotBlock.SetContents(sourceStack, newContents);
                sourceSlot.MarkDirty();
                sinkSlot.MarkDirty();
                return oldSourceSize != sourceStack.StackSize;
            }
            else
            {
                return false;
            }
        }

        public IInFirepitRenderer GetRendererWhenInFirepit(ItemStack stack, BlockEntityFirepit firepit, bool forOutputSlot)
        {
            return new TeapotInFirepitRenderer(api as ICoreClientAPI, stack, firepit.Pos);
        }

        public EnumFirepitModel GetDesiredFirepitModel(ItemStack stack, BlockEntityFirepit firepit, bool forOutputSlot)
        {
            return EnumFirepitModel.Wide;
        }

        public override bool Equals(ItemStack thisStack, ItemStack otherStack, params string[] ignoreAttributeSubTrees)
        {
            // otherwise cooking sound keeps restarting all the time
            if (ignoreAttributeSubTrees == GlobalConstants.IgnoredStackAttributes)
                ignoreAttributeSubTrees = GlobalConstants.IgnoredStackAttributes
                    .Concat(new string[] { CRAFTING_TIME_ATTRIBUTE, CRAFTING_QUALITY_ATTRIBUTE, CURRENT_RECIPE_ATTRIBUTE })
                    .ToArray();
            return base.Equals(thisStack, otherStack, ignoreAttributeSubTrees);
        }
    } // end class Teapot
}     // end namespace MakeTea
