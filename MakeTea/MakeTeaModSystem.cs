using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;
using HarmonyLib;

namespace MakeTea
{
    public class TeapotRecipeRegistry: RecipeRegistryGeneric<TeapotRecipe>
    {
        private readonly Dictionary<string, TeapotRecipe> lookupMap = new();
        public void Add(TeapotRecipe teapotRecipe)
        {
            if (lookupMap.ContainsKey(teapotRecipe.Code)) return;
            lookupMap.TryAdd(teapotRecipe.Code, teapotRecipe);
            Recipes.Add(teapotRecipe);
        }

        public TeapotRecipe GetById(string code)
        {
            return lookupMap.TryGetValue(code);
        }

        public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
        {
            base.FromBytes(resolver, quantity, data);
            lookupMap.Clear();
            foreach (var recipe in Recipes)
                lookupMap.TryAdd(recipe.Code, recipe);
        }
    }

    public class MakeTeaModSystem : ModSystem
    {
        private TeapotRecipeRegistry teapotRecipes;
        public Harmony harmony;

        public List<TeapotRecipe> GetTeapotRecipes()
        {
            return teapotRecipes.Recipes;
        }
        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("MakeTea_Teapot", typeof(Teapot));
            api.RegisterBlockClass("MakeTea_Mug", typeof(Mug));
            api.RegisterBlockEntityClass("MakeTea_TeapotEntity", typeof(TeapotEntity));
            teapotRecipes = api.RegisterRecipeRegistry<TeapotRecipeRegistry>("teapotrecipes");
            if (!Harmony.HasAnyPatches(Mod.Info.ModID))
            {
                harmony = new Harmony(Mod.Info.ModID);
                harmony.PatchAll(); // Applies all harmony patches
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            LoadRecipes(api);
        }

        public TeapotRecipe GetTeapotRecipeById(string id)
        {
            return teapotRecipes.GetById(id);
        }

        private void LoadRecipes(ICoreServerAPI api)
        {
            // TODO: figure out why recipes arrive here twice
            new TeapotRecipeLoader().LoadRecipes<TeapotRecipe>(api, "teapot recipe", "recipes/teapot", teapotRecipes.Add);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(Mod.Info.ModID);
            base.Dispose();
        }
    }
}
