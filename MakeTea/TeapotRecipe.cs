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




            public new bool Resolve(IWorldAccessor resolver, string sourceForErrorLogging)
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
                        if (first == null) first = temp.ResolvedItemstack;
                    }
                }

                if (any)
                {
                    this.ResolvedItemstack = first;  // API exposes only singular representative
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
    public class TeapotRecipe : IByteSerializable, IRecipeBase<TeapotRecipe>
    {
        [DocumentAsJson] public int RecipeId;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Defines the set of ingredients used inside the teapot. Teapots can have a maximum of one item and one liquid ingredient.
        /// </summary>
        [DocumentAsJson] public TeapotRecipeIngredient[] Ingredients;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The final output of this recipe.
        /// </summary>
        [DocumentAsJson] public TeapotOutputStack Output;

        /// <summary>
        /// <!--<jsonoptional>Obsolete</jsonoptional>-->
        /// Unused. Defines a name for the recipe.
        /// </summary>
        [DocumentAsJson] public AssetLocation Name { get; set; }

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>True</jsondefault>-->
        /// Should this recipe be loaded by the recipe loader?
        /// </summary>
        [DocumentAsJson] public bool Enabled { get; set; } = true;

        IRecipeIngredient[] IRecipeBase<TeapotRecipe>.Ingredients => Ingredients;

        IRecipeOutput IRecipeBase<TeapotRecipe>.Output => Output;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// A code for this recipe, used to create an entry in the handbook.
        /// </summary>
        [DocumentAsJson] public string Code;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Defines the time it takes for the tea to brew
        /// </summary>
        [DocumentAsJson] public double Duration;
        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Defines the lowest temperature at which tea can brew at full quality
        /// </summary>
        [DocumentAsJson] public double MinTemperature;
        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Defines the highest temperature at which tea can brew at full quality
        /// </summary>
        [DocumentAsJson] public double MaxTemperature;

        public static double TEMPERATURE_ACCURACY_RATIO = 10.0;
        public static float MIN_QUALITY = 0.25f;


        public void ToBytes(BinaryWriter writer)
        {
            writer.Write(Code);
            writer.Write(Ingredients.Length);
            for (int i = 0; i < Ingredients.Length; i++)
            {
                Ingredients[i].ToBytes(writer);
            }

            Output.ToBytes(writer);
            writer.Write(Duration);
            writer.Write(MinTemperature);
            writer.Write(MaxTemperature);
        }

        

        public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
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

            for (var i = 0; i < slots.Count; i++)
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

        public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)

        // honestly at a certain point im not even sure what the hell im writing lol

        {
            var mappings = new Dictionary<string, string[]>();

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

                mappings[ingred.Name ?? ("wildcard" + mappings.Count)] = codes.ToArray();
            }

            return mappings;
        }


        public bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            bool ok = true;

            for (int i = 0; i < Ingredients.Length; i++)
            {
                var ingred = Ingredients[i];
                bool iOk = ingred.Resolve(world, sourceForErrorLogging);
                ok &= iOk;
            }

            ok &= Output.Resolve(world, sourceForErrorLogging);

            if (ok)
            {
                var lprops = BlockLiquidContainerBase.GetContainableProps(Output.ResolvedItemstack);
                if (lprops != null)
                {
                    if (Output.Litres < 0)
                    {
                        if (Output.Quantity > 0)
                        {
                            world.Logger.Warning("Barrel recipe {0}, output {1} does not define a litres attribute but a stacksize, will assume stacksize=litres for backwards compatibility.", sourceForErrorLogging, Output.Code);
                            Output.Litres = Output.Quantity;
                        }
                        else Output.Litres = 1;

                    }

                    Output.Quantity = (int)(lprops.ItemsPerLitre * Output.Litres);
                }
            }

            return ok;
        }

        public TeapotRecipe Clone()
        {
            TeapotRecipe copy = new TeapotRecipe();
            copy.RecipeId = RecipeId;
            copy.Ingredients = Ingredients;
            copy.Output = Output;
            copy.Name = Name;
            copy.Enabled = Enabled;
            copy.Code = Code;
            copy.Duration = Duration;
            copy.MinTemperature = MinTemperature;
            copy.MaxTemperature = MaxTemperature;
            return copy;
        }
    }

    public class TeapotRecipeLoader
    {
        public void LoadRecipes<T>(ICoreServerAPI api, string name, string path, Action<T> RegisterMethod) where T : IRecipeBase<T>
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

        private void LoadGenericRecipe<T>(ICoreServerAPI api, string className, AssetLocation path, T recipe, Action<T> RegisterMethod, ref int quantityRegistered, ref int quantityIgnored) where T : IRecipeBase<T>
        {
            if (!recipe.Enabled)
            {
                return;
            }

            if (recipe.Name == null)
            {
                recipe.Name = path;
            }

            ref T reference = ref recipe;
            T val = default(T);
            if (val == null)
            {
                val = reference;
                reference = ref val;
            }

            Dictionary<string, string[]> nameToCodeMapping = reference.GetNameToCodeMapping(api.World);
            if (nameToCodeMapping.Count > 0)
            {
                List<T> list = new List<T>();
                int num = 0;
                bool flag = true;
                foreach (KeyValuePair<string, string[]> item in nameToCodeMapping)
                {
                    num = ((!flag) ? (num * item.Value.Length) : item.Value.Length);
                    flag = false;
                }

                flag = true;
                foreach (KeyValuePair<string, string[]> item2 in nameToCodeMapping)
                {
                    string key = item2.Key;
                    string[] value = item2.Value;
                    for (int i = 0; i < num; i++)
                    {
                        T val2;
                        if (flag)
                        {
                            list.Add(val2 = recipe.Clone());
                        }
                        else
                        {
                            val2 = list[i];
                        }

                        if (val2.Ingredients != null)
                        {
                            IRecipeIngredient[] ingredients = val2.Ingredients;
                            foreach (IRecipeIngredient recipeIngredient in ingredients)
                            {
                                if (recipeIngredient.Name == key)
                                {
                                    recipeIngredient.Code = recipeIngredient.Code.CopyWithPath(recipeIngredient.Code.Path.Replace("*", value[i % value.Length]));
                                }
                            }
                        }

                        val2.Output.FillPlaceHolder(item2.Key, value[i % value.Length]);
                    }

                    flag = false;
                }

                if (list.Count == 0)
                {
                    api.World.Logger.Warning("{1} file {0} make uses of wildcards, but no blocks or item matching those wildcards were found.", path, className);
                }

                {
                    foreach (T item3 in list)
                    {
                        T current3 = item3;
                        ref T reference2 = ref current3;
                        val = default(T);
                        if (val == null)
                        {
                            val = reference2;
                            reference2 = ref val;
                        }

                        if (!reference2.Resolve(api.World, className + " " + path))
                        {
                            quantityIgnored++;
                            continue;
                        }

                        RegisterMethod(current3);
                        quantityRegistered++;
                    }

                    return;
                }
            }

            ref T reference3 = ref recipe;
            val = default(T);
            if (val == null)
            {
                val = reference3;
                reference3 = ref val;
            }

            if (!reference3.Resolve(api.World, className + " " + path))
            {
                quantityIgnored++;
                return;
            }

            RegisterMethod(recipe);
            quantityRegistered++;
        }
    }
}
