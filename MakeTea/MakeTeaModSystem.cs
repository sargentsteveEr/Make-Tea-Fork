using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using HarmonyLib;

namespace MakeTea
{
    public class TeapotRecipeRegistry : RecipeRegistryGeneric<TeapotRecipe>
    {
        private readonly Dictionary<string, TeapotRecipe> lookupMap = new();

        public void Add(TeapotRecipe teapotRecipe)
        {
            // Avoid duplicates if AssetsLoaded runs more than once across reloads
            if (teapotRecipe == null || string.IsNullOrEmpty(teapotRecipe.Code)) return;
            if (lookupMap.ContainsKey(teapotRecipe.Code)) return;

            lookupMap[teapotRecipe.Code] = teapotRecipe;
            Recipes.Add(teapotRecipe);
        }

        public TeapotRecipe GetById(string code)
        {
            if (code == null) return null;
            return lookupMap.TryGetValue(code, out var recipe) ? recipe : null;
        }

        public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
        {
            base.FromBytes(resolver, quantity, data);

            // Rebuild fast lookup after sync from server -> client
            lookupMap.Clear();
            foreach (var recipe in Recipes)
            {
                if (recipe?.Code != null) lookupMap.TryAdd(recipe.Code, recipe);
            }
        }

        public void Clear()
        {
            Recipes.Clear();
            lookupMap.Clear();
        }
    }

    public class MakeTeaModSystem : ModSystem
    {
        private TeapotRecipeRegistry teapotRecipes;
        public Harmony harmony;

        public List<TeapotRecipe> GetTeapotRecipes() => teapotRecipes?.Recipes;

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("MakeTea_Teapot", typeof(Teapot));
            api.RegisterBlockClass("MakeTea_Mug", typeof(Mug));
            api.RegisterBlockEntityClass("MakeTea_TeapotEntity", typeof(TeapotEntity));
            teapotRecipes = api.RegisterRecipeRegistry<TeapotRecipeRegistry>("teapotrecipes");
            if (!Harmony.HasAnyPatches(Mod.Info.ModID))
            {
                harmony = new Harmony(Mod.Info.ModID);
                harmony.PatchAll();
            }
        }
        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            if (api is ICoreServerAPI sapi)
            {
                LoadRecipes(sapi);
            }
        }

        public TeapotRecipe GetTeapotRecipeById(string id) => teapotRecipes?.GetById(id);

        private void LoadRecipes(ICoreServerAPI api)
        {
            teapotRecipes.Clear();

            new TeapotRecipeLoader()
                .LoadRecipes<TeapotRecipe>(api, "teapot recipe", "recipes/teapot", teapotRecipes.Add);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(Mod.Info.ModID);
            base.Dispose();
        }
    }
}
