using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API;
using System.IO;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.API.Util;
using Vintagestory.API.MathTools;
using System.Linq;

namespace MakeTea
{
    [DocumentAsJson]
    public class TeapotRecipeIngredient : CraftingRecipeIngredient
    {

        [DocumentAsJson] public string[] Codes;

        // runtime matching
        // TeapotRecipe.cs
        public bool Matches(ItemStack slot, out float normalizedQty)
    {
        normalizedQty = 0f;
        if (slot == null) return false;

        var coll = slot.Collectible;
        if (coll == null) return false;

        float qty = slot.StackSize;

        // Treat any stack with containable props as liquid-ish (covers item portions)
        var props = BlockLiquidContainerBase.GetContainableProps(slot);
        if (props != null)
        {
            if (props.ItemsPerLitre <= 0) return false;
            qty /= props.ItemsPerLitre;
        }
        // (if props == null) it's a solid; leave qty as stack count

        normalizedQty = qty / Math.Max(1, Quantity);

        // Prefer OR-match when Codes[] is present (API has array overloads)
        if ((Codes?.Length ?? 0) > 0)
        {
            var any = Codes.Where(s => !string.IsNullOrWhiteSpace(s))
                           .Select(s => new AssetLocation(s))
                           .ToArray();
            if (any.Length == 0) return false;
            return coll.WildCardMatch(any);   // ← array overload, match-ANY :contentReference[oaicite:1]{index=1}
        }

        if (Code == null) return false;
        return coll.WildCardMatch(Code);
    }




            public override bool Resolve(IWorldAccessor resolver, string sourceForErrorLogging)
            {
                // if using single 'code' let vanilla do it.
                if (Codes == null || Codes.Length == 0) return base.Resolve(resolver, sourceForErrorLogging);

                // expand each entry via a temporary vanilla ingredient,
                // then keep one representative for handbook/UI.
                ItemStack first = null;
                bool any = false;

                foreach (var wc in Codes)
                {
                    if (string.IsNullOrWhiteSpace(wc)) continue;

                    var temp = new CraftingRecipeIngredient
                    {
                        Type = this.Type,
                        Code = new AssetLocation(wc),
                        Quantity = this.Quantity,
                        IsTool = this.IsTool,
                        Name = this.Name,
                        AllowedVariants = this.AllowedVariants,
                        Attributes = this.Attributes
                    };

                    if (temp.Resolve(resolver, sourceForErrorLogging))
                    {
                        any = true;
                        if (first == null) first = this.ResolvedItemStack;
                    }
                }

                if (any)
                {
                    this.ResolvedItemStack = first;
                    return true;
                }

                resolver.Logger.Warning(
                    "Teapot recipe ingredient could not resolve any of the codes [{0}] in {1}",
                    string.Join(", ", Codes), sourceForErrorLogging
                );
                return false;
            }

        // network serialization
            public override void ToBytes(BinaryWriter writer)
            {
                var backup = Code;
                if ((Codes?.Length ?? 0) > 0 && Code == null)
                    Code = new AssetLocation(Codes[0]);  // representative to satisfy base

                base.ToBytes(writer);

                writer.Write(Codes?.Length ?? 0);
                if (Codes != null) foreach (var c in Codes) writer.Write(c);

                Code = backup;
            }

            public override void FromBytes(BinaryReader reader, IWorldAccessor resolver)
            {
                base.FromBytes(reader, resolver);

                int n = reader.ReadInt32();
                if (n > 0)
                {
                    Codes = new string[n];
                    for (int i = 0; i < n; i++) Codes[i] = reader.ReadString();
                }
                else Codes = null;

                if ((Codes?.Length ?? 0) > 0 && Code == null)
                    Code = new AssetLocation(Codes[0]);  // belt & suspenders for client
            }


        // If SDK instead requires IClassRegistryAPI, use this overload instead:
        // public override void FromBytes(BinaryReader reader, IClassRegistryAPI instancer) { ... same body ... }
            
        // lil note lol
        }



    [DocumentAsJson]
    public class TeapotOutputStack : JsonItemStack
    {
        [DocumentAsJson]
        public float Litres;

        public override void FromBytes(BinaryReader reader, IClassRegistryAPI instancer)
        {
            base.FromBytes(reader, instancer);
            Litres = reader.ReadSingle();
        }

        public override void ToBytes(BinaryWriter writer)
        {
            base.ToBytes(writer);
            writer.Write(Litres);
        }

        

        public new TeapotOutputStack Clone()
        {
            TeapotOutputStack teapotOutputStack = new TeapotOutputStack
            {
                Code = Code.Clone(),
                ResolvedItemstack = ResolvedItemstack?.Clone(),
                StackSize = StackSize,
                Type = Type,
                Litres = Litres
            };
            if (Attributes != null)
            {
                teapotOutputStack.Attributes = Attributes.Clone();
            }

            return teapotOutputStack;
        }
    }

    [DocumentAsJson]
    public class TeapotRecipe : RecipeBase, IByteSerializable
    {

        public override IEnumerable<IRecipeIngredient> RecipeIngredients => Ingredients;
        public override IRecipeOutput RecipeOutput => Output;

        [DocumentAsJson] public TeapotRecipeIngredient[] Ingredients;

        [DocumentAsJson] public TeapotOutputStack Output;

        [DocumentAsJson] public string Code;

        [DocumentAsJson] public double Duration;

        [DocumentAsJson] public double MinTemperature;

        [DocumentAsJson] public double MaxTemperature;

        public static double TEMPERATURE_ACCURACY_RATIO = 10.0;
        public static float MIN_QUALITY = 0.25f;


        public override void ToBytes(BinaryWriter writer)
        {
            base.ToBytes(writer);
            writer.Write(Code ?? "");
            writer.Write(Ingredients?.Length ?? 0);
            for (int i = 0; i < Ingredients.Length; i++) Ingredients[i].ToBytes(writer);

            Output?.ToBytes(writer);
            writer.Write(Duration);
            writer.Write(MinTemperature);
            writer.Write(MaxTemperature);
        }

        

        public override void FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
            base.FromBytes(reader, resolver);  // important in 1.22
            Code = reader.ReadString();

            Ingredients = new TeapotRecipeIngredient[reader.ReadInt32()];
            for (int i = 0; i < Ingredients.Length; i++)
            {
                Ingredients[i] = new TeapotRecipeIngredient();
                Ingredients[i].FromBytes(reader, resolver);
                Ingredients[i].Resolve(resolver, "Teapot Recipe (FromBytes)");
            }

            Output = new TeapotOutputStack();
            Output.FromBytes(reader, resolver.ClassRegistry);
            Output.Resolve(resolver, "Teapot Recipe (FromBytes)");

            Duration = reader.ReadDouble();
            MinTemperature = reader.ReadDouble();
            MaxTemperature = reader.ReadDouble();
        }
        
        

        public bool Matches(ItemStack[] stacks, float temperature, out float outsize)
        {
            outsize = 0;
            if (Ingredients == null || Ingredients.Length == 0) return false;
            if (stacks == null || stacks.Length < Ingredients.Length) return false;

            bool TryOrder(int a, int b, out float size)
            {
                size = 0f;
                if (!Ingredients[0].Matches(stacks[a], out float s0)) return false;
                if (!Ingredients[1].Matches(stacks[b], out float s1)) return false;
                if (Math.Abs(s0 - s1) > 1e-4f) return false;
                size = s0;
                return true;
            }

            if (Ingredients.Length == 2)
            {
                if (TryOrder(0, 1, out outsize)) return true;
                if (TryOrder(1, 0, out outsize)) return true;
                return false;
            }

            // fallback: original strict order for 3+ ingredients
            float total;
            if (!Ingredients[0].Matches(stacks[0], out total)) return false;
            for (int i = 1; i < Ingredients.Length; i++)
            {
                if (!Ingredients[i].Matches(stacks[i], out float slotSize) || Math.Abs(slotSize - total) > 1e-4f) return false;
            }
            outsize = total;
            return true;
        }

        public double TemperatureMatch(double temperature)
        {
            double distance = Math.Max(0.0, MinTemperature - temperature) + Math.Max(0.0, temperature - MaxTemperature);
            return 1.0 - Math.Min(1.0, distance / TEMPERATURE_ACCURACY_RATIO);
        }

        private NatFloat GetTransitionDuration()
        {
            var props = Output.ResolvedItemstack.Collectible.TransitionableProps;
            foreach (var prop in props)
            {
                if (prop.Type == EnumTransitionType.Perish)
                    return prop.TransitionHours;
            }
            return NatFloat.Zero;
        }

        
        public bool TryCraft(InventoryBase slots, float temperature, double craftingTime, double quality)
        {
            float outsize = 0;
            ItemStack[] stacks = slots.Select(s => s.Itemstack).ToArray();

            if (!Matches(stacks, temperature, out outsize) || outsize == 0)
                return false;

            if (temperature <= Teapot.ROOM_TEMPERATURE)
                return false;

            if (craftingTime < Duration)
                return false;

            for (int i = 0; i < Ingredients.Length && i < slots.Count; i++)
                {
                    var quantity = Ingredients[i].Quantity;
                    var stack = slots[i].Itemstack;
                    if (stack == null) continue;

                    if (stack.Collectible.IsLiquid())
                    {
                        WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(slots[i].Itemstack);
                        quantity = (int)(props.ItemsPerLitre * Ingredients[i].Quantity);
                    }

                    int consume = (int)MathF.Round((float)quantity * outsize);
                    stack.StackSize -= consume;

                    if (stack.StackSize <= 0)
                    {
                        slots[i].Itemstack = null;
                    }
                }
            var outputStack = Output.ResolvedItemstack.Clone();
            outputStack.StackSize = (int)(Output.Litres * BlockLiquidContainerBase.GetContainableProps(outputStack).ItemsPerLitre * outsize);
            double transition = (1.0 - Math.Clamp(quality / Duration, MIN_QUALITY, 1.0)) * GetTransitionDuration().avg;
            outputStack.Collectible.SetTransitionState(outputStack, EnumTransitionType.Perish, (float)transition);
            slots[0].Itemstack = outputStack;
            return true;
        }

        protected override Dictionary<string, HashSet<string>> GetNameToCodeMapping(IWorldAccessor world)

        {
            var mappings = new Dictionary<string, HashSet<string>>();

            if (Ingredients == null || Ingredients.Length == 0) return mappings;

            foreach (var ingred in Ingredients)
            {
                //  only build variant mappings when there's a wildcard in the PATH portion.
                // (Domain wildcards like "*:" are handled by WildcardUtil.Match() below.)
                var patternPath = ingred.Code?.Path;
                if (string.IsNullOrEmpty(patternPath)) continue;

                int firstStar = patternPath.IndexOf('*');
                if (firstStar < 0) continue; // no wildcard in PATH -> nothing to expand

                int lastStar  = patternPath.LastIndexOf('*');
                int prefixLen = Math.Max(0, firstStar);
                int suffixLen = Math.Max(0, patternPath.Length - lastStar - 1);

                var codes = new List<string>();

                if (ingred.Type == EnumItemClass.Block)
                {
                    for (int i = 0; i < world.Blocks.Count; i++)
                    {
                        var b = world.Blocks[i];
                        if (b?.Code == null || b.IsMissing) continue;

                        // only consider blocks that actually match the full (domain+path) wildcard.
                        if (!WildcardUtil.Match(ingred.Code, b.Code)) continue;

                        var path = b.Code.Path;
                        if (path.Length < prefixLen + suffixLen) continue; // defensive guard

                        string codepart = path.Substring(prefixLen, path.Length - prefixLen - suffixLen);

                        if (ingred.AllowedVariants != null && !ingred.AllowedVariants.Contains(codepart)) continue;

                        codes.Add(codepart);
                    }
                }
                else
                {
                    for (int i = 0; i < world.Items.Count; i++)
                    {
                        var it = world.Items[i];
                        if (it?.Code == null || it.IsMissing) continue;

                        // only consider items that actually match the full (domain+path) wildcard.
                        if (!WildcardUtil.Match(ingred.Code, it.Code)) continue;

                        var path = it.Code.Path;
                        if (path.Length < prefixLen + suffixLen) continue; // defensive guard

                        string codepart = path.Substring(prefixLen, path.Length - prefixLen - suffixLen);

                        if (ingred.AllowedVariants != null && !ingred.AllowedVariants.Contains(codepart)) continue;

                        codes.Add(codepart);
                    }
                }

                mappings[ingred.Name ?? ("wildcard" + mappings.Count)] = new HashSet<string>(codes);
            }

            return mappings;
        }


        public override bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            bool ok = true;
            if (Ingredients == null || Output == null)
            {
                world.Logger.Error($"Cannot resolve teapot recipe '{Name}', Ingredients or Output missing");
                return false;
            }

            foreach (var ingred in Ingredients) ok &= ingred.Resolve(world, sourceForErrorLogging);
            ok &= Output.Resolve(world, sourceForErrorLogging);

            // return ok.
            return ok;
            // ok.
        }

        public override TeapotRecipe Clone()
        {
            var copy = new TeapotRecipe();
            CloneTo(copy);
            return copy;
        }

        protected override void CloneTo(object recipe)
        {
            base.CloneTo(recipe);

            var copy = (TeapotRecipe)recipe;
            copy.Code = Code;
            copy.Duration = Duration;
            copy.MinTemperature = MinTemperature;
            copy.MaxTemperature = MaxTemperature;
            copy.Output = Output?.Clone();

            if (Ingredients != null)
            {
                copy.Ingredients = new TeapotRecipeIngredient[Ingredients.Length];
                for (int i = 0; i < Ingredients.Length; i++)
                {
                    copy.Ingredients[i] = (TeapotRecipeIngredient)Ingredients[i].Clone();
                }
            }
        }
    }

    public class TeapotRecipeLoader
    {
        public void LoadRecipes<T>(ICoreServerAPI api, string name, string path, Action<T> RegisterMethod) where T : RecipeBase
        {
            Dictionary<AssetLocation, JToken> many = api.Assets.GetMany<JToken>(api.Server.Logger, path);
            int num = 0;
            int quantityRegistered = 0;
            int quantityIgnored = 0;
            foreach (KeyValuePair<AssetLocation, JToken> item in many)
            {
                if (item.Value is JObject)
                {
                    LoadGenericRecipe(api, name, item.Key, item.Value.ToObject<T>(item.Key.Domain), RegisterMethod, ref quantityRegistered, ref quantityIgnored);
                    num++;
                }

                if (!(item.Value is JArray))
                {
                    continue;
                }

                foreach (JToken item2 in item.Value as JArray)
                {
                    LoadGenericRecipe(api, name, item.Key, item2.ToObject<T>(item.Key.Domain), RegisterMethod, ref quantityRegistered, ref quantityIgnored);
                    num++;
                }
            }

            api.World.Logger.Event("{0} {1}s loaded{2}", quantityRegistered, name, (quantityIgnored > 0) ? $" ({quantityIgnored} could not be resolved)" : "");
        }

        private void LoadGenericRecipe<T>(ICoreServerAPI api, string className, AssetLocation path, T recipe, Action<T> RegisterMethod, ref int quantityRegistered, ref int quantityIgnored) where T : RecipeBase
        {
            if (recipe == null || !recipe.Enabled) return;

            // Name is a RecipeBase field in 1.22 (same as vanilla BarrelRecipe)
            recipe.Name ??= path;

            // resolve all ingredient/output stacks
            if (!recipe.Resolve(api.World, className + " " + path))
            {
                quantityIgnored++;
                return;
            }

            // vanilla recipe loaders call this after Resolve(?)
            recipe.OnParsed(api.World);

            // let RecipeBase expand wildcards/variants into concrete recipes
            var expanded = recipe.GenerateRecipesForAllIngredientCombinations(api.World);

            bool any = false;
            foreach (var sub in expanded)
            {
                any = true;

                // sub is IRecipeBase, but in practice these are clones of T
                if (sub is T typed)
                {
                    RegisterMethod(typed);
                    quantityRegistered++;
                }
                else
                {
                    // if something odd happens, at least register the base instance
                    // (or skip with a warning?)
                    api.World.Logger.Warning(
                        "{0} file {1} produced a recipe of unexpected type {2}",
                        className, path, sub?.GetType()?.FullName ?? "<null>"
                    );
                }
            }

            if (!any)
            {
                api.World.Logger.Warning(
                    "{0} file {1} uses wildcards, but no matching variants were generated.",
                    className, path
                );
            }
        }
    }
}
