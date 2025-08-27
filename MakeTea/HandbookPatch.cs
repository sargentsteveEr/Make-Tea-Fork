using Cairo;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace MakeTea;

[HarmonyPatchCategory("Other")]
[HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), nameof(CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo))]
public static class HandbookPatch
{
    private const float MedumPadding = 14f;
    private const float MarginBottom = 3f;
    private  static void AddHeading(List<RichTextComponentBase> components, ICoreClientAPI capi, string heading)
    {
        components.Add(new ClearFloatTextComponent(capi, MedumPadding));
        var headc = new RichTextComponent(capi, Lang.Get(heading) + "\n", CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold));
        components.Add(headc);
    }
    public static void AddTeaRecipes(List<RichTextComponentBase> components, ItemSlot inSlot, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
    {
        if (!ItemSlotTeapotInput.CanHold(inSlot.Itemstack.Collectible.Code) && inSlot.Itemstack.Collectible.WildCardMatch("maketea:tea-*")) return;

        var system = capi.ModLoader.GetModSystem<MakeTeaModSystem>();
        var recipes = system.GetTeapotRecipes();

        ItemStack maxstack = inSlot.Itemstack.Clone();
        maxstack.StackSize = maxstack.Collectible.MaxStackSize * 10; // to pass all ingredient quantity requirements

        var ingredientStacks = new List<ItemStack>();
        var teaStacks = new List<ItemStack>();

        foreach (var recipe in recipes)
        {
            foreach (var ingred in recipe.Ingredients)
            {
                if (ingred.SatisfiesAsIngredient(maxstack) && !ingredientStacks.Any(s => s.Equals(capi.World, recipe.Output.ResolvedItemstack, GlobalConstants.IgnoredStackAttributes)))
                {
                    ingredientStacks.Add(recipe.Output.ResolvedItemstack);
                }
            }
            if (recipe.Output.ResolvedItemstack.Collectible.Equals(inSlot.Itemstack.Collectible))
            {
                var herbIngredient = recipe.Ingredients.FirstOrDefault(s => ItemSlotTeapotInput.CanHold(s.Code));
                if (herbIngredient != null) {
                    foreach (var stack in allStacks)
                    {
                        var maxCandidateStack = stack.GetEmptyClone();
                        maxCandidateStack.StackSize = maxCandidateStack.Collectible.MaxStackSize * 10;
                        if (herbIngredient.SatisfiesAsIngredient(maxCandidateStack))
                        {
                            var tempStack = stack.Clone();
                            tempStack.Attributes.SetDouble("__maketea_min_temperature", recipe.MinTemperature);
                            tempStack.Attributes.SetDouble("__maketea_max_temperature", recipe.MaxTemperature);
                            teaStacks.Add(tempStack);
                        }
                    }
                }
            }
        }

        if (ingredientStacks.Count > 0)
        {
            AddHeading(components, capi, "maketea:handbook-herb-heading");
            components.Add(new ClearFloatTextComponent(capi, MedumPadding));

            while (ingredientStacks.Count > 0) // have to do it this slow way, because SlideshowItemstackTextComponent modifies the stack List
            {
                ItemStack dstack = ingredientStacks[0];
                ingredientStacks.RemoveAt(0);
                if (dstack == null) continue;

                var comp = new SlideshowItemstackTextComponent(capi, dstack, ingredientStacks, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)));
                components.Add(comp);
            }

            components.Add(new ClearFloatTextComponent(capi, MarginBottom));
        }

        if (teaStacks.Count > 0)
        {
            AddHeading(components, capi, "maketea:handbook-tea-heading");
            components.Add(new ClearFloatTextComponent(capi, MedumPadding));

            while (teaStacks.Count > 0) // have to do it this slow way, because SlideshowItemstackTextComponent modifies the stack List
            {
                ItemStack dstack = teaStacks[0];
                teaStacks.RemoveAt(0);
                if (dstack == null) continue;

                var comp = new SlideshowItemstackTextComponent(capi, dstack, teaStacks, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)));
                components.Add(comp);
                var minTemperature = dstack.Attributes.TryGetDouble("__maketea_min_temperature");
                var maxTemperature = dstack.Attributes.TryGetDouble("__maketea_max_temperature");
                if (minTemperature != null && maxTemperature != null)
                {
                    var temperatureHint = new RichTextComponent(capi, Lang.Get("maketea:handbook-temperature-hint", minTemperature, maxTemperature), CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold));
                    components.Add(temperatureHint);
                }
            }

            components.Add(new ClearFloatTextComponent(capi, MarginBottom));
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref RichTextComponentBase[] __result, ItemSlot inSlot, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
    {
        List<RichTextComponentBase> components = __result.ToList();
        AddTeaRecipes(components, inSlot, capi, allStacks, openDetailPageFor);
        __result = components.ToArray();
    }
}