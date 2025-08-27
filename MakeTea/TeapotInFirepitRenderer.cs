using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakeTea
{
    public class TeapotInFirepitRenderer : IInFirepitRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 20;

        private ICoreClientAPI Api;
        private MultiTextureMeshRef TeapotRef;
        private BlockPos Pos;
        private float Temperature;
        private ILoadedSound BoilingSound;
        private ILoadedSound WhistleSound;
        private Matrixf ModelMat = new Matrixf();

        public TeapotInFirepitRenderer(ICoreClientAPI capi, ItemStack stack, BlockPos pos)
        {
            Api = capi;
            Pos = pos;

            var teapotBlock = capi.World.GetBlock(stack.Collectible.Code) as Teapot;
            string meshPath = "maketea:shapes/block/clay/teapot.json";
            capi.Tesselator.TesselateShape(teapotBlock, Shape.TryGet(capi, meshPath), out MeshData potMesh);
            TeapotRef = capi.Render.UploadMultiTextureMesh(potMesh);
        }

        public void Dispose()
        {
            TeapotRef?.Dispose();

            BoilingSound?.Stop();
            BoilingSound?.Dispose();

            WhistleSound?.Stop();
            WhistleSound?.Dispose();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            IRenderAPI rpi = Api.Render;
            Vec3d camPos = Api.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(Pos.X, Pos.Y, Pos.Z);

            prog.DontWarpVertices = 0;
            prog.AddRenderFlags = 0;
            prog.RgbaAmbientIn = rpi.AmbientColor;
            prog.RgbaFogIn = rpi.FogColor;
            prog.FogMinIn = rpi.FogMin;
            prog.FogDensityIn = rpi.FogDensity;
            prog.RgbaTint = ColorUtil.WhiteArgbVec;
            prog.NormalShaded = 1;
            prog.ExtraGodray = 0;
            prog.SsaoAttn = 0;
            prog.AlphaTest = 0.05f;
            prog.OverlayOpacity = 0;


            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(Pos.X - camPos.X + 0.001f, Pos.Y - camPos.Y, Pos.Z - camPos.Z - 0.001f)
                .Translate(-1/8f, 1/16f, 1/8f)
                .RotateY((float)(Math.PI * 3 / 32))
                .Values;

            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMultiTextureMesh(TeapotRef, "tex");

            prog.Stop();
        }

        private void UpdateSound(ref ILoadedSound sound, string path, float volume)
        {
            if (volume > 0)
            {
                if (sound == null)
                {
                    sound = Api.World.LoadSound(new SoundParams()
                    {
                        Location = new AssetLocation(path),
                        ShouldLoop = true,
                        Position = Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                        DisposeOnFinish = false,
                        Range = 10f,
                        ReferenceDistance = 3f,
                        Volume = volume,
                        RelativePosition = false,
                    });
                    sound.Start();
                }
                else
                {
                    sound.SetVolume(volume);
                }

            }
            else
            {
                if (sound != null)
                {
                    sound.Stop();
                    sound.Dispose();
                    sound = null;
                }
            }
        }

        public void OnUpdate(float temperature)
        {
            Temperature = temperature;

            float boilIntensity = GameMath.Clamp((Temperature - 50) / 50, 0, 1);
            UpdateSound(ref BoilingSound, "maketea:sounds/blocks/teapotboil", boilIntensity);
            float whistleIntensity = GameMath.Clamp((Temperature - 90) / 10, 0, 1);
            UpdateSound(ref WhistleSound, "maketea:sounds/blocks/teapotwhistle", whistleIntensity);
        }

        public void OnCookingComplete()
        {
        }
    }
}
