using Cairo;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace MakeTea
{
    internal class GuiDialogTeapot : GuiDialogBlockEntity
    {
        private EnumPosFlag screenPos;
        private TransitionState TransitionState;

        protected override double FloatyDialogPosition => 0.6;

        protected override double FloatyDialogAlign => 0.8;

        public override double DrawOrder => 0.2;

        public GuiDialogTeapot(string dialogTitle, InventoryBase inventory, BlockPos blockEntityPos, ICoreClientAPI capi)
        : base(dialogTitle, inventory, blockEntityPos, capi)
        {

        }

        private void SetupDialog()
        {
            ElementBounds hintBounds = ElementBounds.Fixed(0, 30, 210, 45);
            ElementBounds arrowBounds = ElementBounds.Fixed(0, 30, 200, 90);
            ElementBounds inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 60, 1, 1);
            ElementBounds liquidSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 153, 60, 1, 1);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(arrowBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithFixedAlignmentOffset(IsRight(screenPos) ? -GuiStyle.DialogToScreenPadding : GuiStyle.DialogToScreenPadding, 0)
                .WithAlignment(IsRight(screenPos) ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle);

            ClearComposers();
            SingleComposer = capi.Gui
                .CreateCompo("blockentityteapot" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(DialogTitle, Close)
                .BeginChildElements(bgBounds)
                    .AddDynamicText(BrewText(), CairoFont.WhiteDetailText(), hintBounds, "brewText")
                    .AddDynamicCustomDraw(arrowBounds, DrawArrow, "progressArrow")
                    .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[1] { Teapot.ITEM_SLOT }, inputSlotBounds, "inputSlot")
                    .AddDynamicText("", CairoFont.WhiteDetailText(), inputSlotBounds.RightCopy(23, 16).WithFixedSize(60, 30), "temperature")
                    .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[1] { Teapot.LIQUID_SLOT }, liquidSlotBounds, "liquidSlot")
                .EndChildElements()
                .Compose();
        }

        private void Close()
        {
            TryClose();
        }

        private float GetTemperature()
        {
            if (Inventory[Teapot.LIQUID_SLOT].Empty) return Teapot.ROOM_TEMPERATURE;
            ItemStack itemstack = Inventory[Teapot.LIQUID_SLOT].Itemstack;
            return itemstack.Collectible.GetTemperature(capi.World, itemstack);
        }

        private double GetTemperatureMatch()
        {
            var teapotEntity = capi.World.BlockAccessor.GetBlockEntity(base.BlockEntityPosition) as TeapotEntity;
            var currentRecipe = teapotEntity?.FindCurrentRecipe();
            if (currentRecipe == null) return 0f;
            return currentRecipe.TemperatureMatch(GetTemperature());
        }

        private void DrawArrow(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            double top = 30;
            ctx.Save();
            Matrix m = ctx.Matrix;
            m.Translate(GuiElement.scaled(63), GuiElement.scaled(top + 2));
            m.Scale(GuiElement.scaled(0.6), GuiElement.scaled(0.6));
            ctx.Matrix = m;
            capi.Gui.Icons.DrawArrowRight(ctx, 2);

            double dx = 0;
            if (TransitionState != null)
            {
                dx = TransitionState.TransitionedHours / TransitionState.TransitionHours;
            }

            ctx.Rectangle(GuiElement.scaled(5), 0, GuiElement.scaled(100 * dx), GuiElement.scaled(100));
            ctx.Clip();
            var gradient = new LinearGradient(0, 0, GuiElement.scaled(200), 0);

            var green = new Vec3d[2] { new(0, 0.4, 0), new(0.2, 0.6, 0.2) };
            var red = new Vec3d[2] { new(0.4, 0, 0), new(0.6, 0.2, 0.2) };
            var temperatureMatch = GetTemperatureMatch();
            for (int i = 0; i < 2; i++)
            {
                var mixed = green[i] * temperatureMatch + (1 - temperatureMatch) * red[i];
                gradient.AddColorStop(i, new Color(mixed.X, mixed.Y, mixed.Z, 1));
            }
            ctx.SetSource(gradient);
            capi.Gui.Icons.DrawArrowRight(ctx, 0, false, false);
            gradient.Dispose();
            ctx.Restore();
        }

        private string BrewText()
        {
            var teapotEntity = capi.World.BlockAccessor.GetBlockEntity(BlockEntityPosition) as TeapotEntity;
            var currentRecipe = teapotEntity?.FindCurrentRecipe();
            if (currentRecipe == null) return "";
                ItemStack resolvedItemstack = currentRecipe.Output.ResolvedItemstack;
            return Lang.Get("maketea:will-brew-into-hint", resolvedItemstack.GetName());
        }

        private string TemperatureText()
        {
            var temp = Math.Round(GetTemperature());
            if (temp <= Teapot.ROOM_TEMPERATURE) return "";
            return $"{temp}°C";
        }

        private void SendInvPacket(object packet)
        {
            capi.Network.SendBlockEntityPacket(base.BlockEntityPosition.X, base.BlockEntityPosition.Y, base.BlockEntityPosition.Z, packet);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            screenPos = GetFreePos("smallblockgui");
            OccupyPos("smallblockgui", screenPos);
            SetupDialog();
        }

        public override void OnGuiClosed()
        {
            base.SingleComposer.GetSlotGrid("inputSlot").OnGuiClosed(capi);
            base.OnGuiClosed();
            FreePos("smallblockgui", screenPos);
        }

        public void UpdateContents(TransitionState state)
        {
            TransitionState = state;
            SingleComposer.GetCustomDraw("progressArrow").Redraw();
            SingleComposer.GetDynamicText("brewText")?.SetNewText(BrewText());
            SingleComposer.GetDynamicText("temperature").SetNewText(TemperatureText());
        }
    }
}
