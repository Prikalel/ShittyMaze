using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// REMOVABLE world-space debug readout for on-device input verification
    /// (debug of "thumbstick dead / weapon missing"). Shows a small line of
    /// text billboarded above the view center with:
    ///   R conn:1|0      - TouchController.GetState(RTouch).IsConnected
    ///   grip:ok|lost|no-model - right grip pose tracked / weapon model state
    ///   stick:(x,y)     - right thumbstick (deadzone applied)
    ///   trig:0.00       - right trigger analog value
    ///
    /// Remove by deleting this file and the three "debugHud" usages in
    /// VrMazeGame (field, LoadContent init, Update SetText, Draw call), or by
    /// setting <see cref="Enabled"/> to false.
    /// </summary>
    public class VrDebugHud
    {
        /// <summary>Master switch; set false (or delete the file) once input is verified.</summary>
        public const bool Enabled = true;

        private const int TextureWidth = 512;
        private const int TextureHeight = 96;

        /// <summary>Distance in front of the eyes where the text floats (meters).</summary>
        private const float Distance = 2.0f;

        /// <summary>Vertical offset above the view center (meters).</summary>
        private const float HeightOffset = 0.45f;

        private const float QuadWidth = 0.8f;
        private const float QuadHeight = 0.15f;

        /// <summary>Minimum seconds between text bakes (the text rarely changes).</summary>
        private const float BakeInterval = 0.2f;

        private readonly GraphicsDevice graphicsDevice;
        private readonly SpriteBatch spriteBatch;
        private readonly SpriteFont font;
        private readonly RenderTarget2D texture;
        private readonly BasicEffect effect;
        private readonly VertexPositionTexture[] quadVertices;
        private readonly short[] quadIndices;

        private string currentText;
        private string bakedText;
        private float bakeCountdown;

        public VrDebugHud(GraphicsDevice graphicsDevice, SpriteFont font)
        {
            this.graphicsDevice = graphicsDevice;
            this.font = font;
            this.spriteBatch = new SpriteBatch(graphicsDevice);

            texture = new RenderTarget2D(
                graphicsDevice, TextureWidth, TextureHeight,
                mipMap: false, SurfaceFormat.Color, DepthFormat.None);

            effect = new BasicEffect(graphicsDevice)
            {
                TextureEnabled = true,
                Texture = texture,
                LightingEnabled = false,
                VertexColorEnabled = false,
            };

            float halfW = QuadWidth * 0.5f;
            float halfH = QuadHeight * 0.5f;
            quadVertices = new VertexPositionTexture[4];
            quadVertices[0] = new VertexPositionTexture(new Vector3(-halfW, halfH, 0f), new Vector2(0f, 0f)); // top-left
            quadVertices[1] = new VertexPositionTexture(new Vector3(halfW, halfH, 0f), new Vector2(1f, 0f));  // top-right
            quadVertices[2] = new VertexPositionTexture(new Vector3(halfW, -halfH, 0f), new Vector2(1f, 1f)); // bottom-right
            quadVertices[3] = new VertexPositionTexture(new Vector3(-halfW, -halfH, 0f), new Vector2(0f, 1f)); // bottom-left

            quadIndices = new short[] { 0, 1, 2, 0, 2, 3 };
        }

        /// <summary>Sets the text to display (re-baked at most every BakeInterval).</summary>
        public void SetText(string text)
        {
            currentText = text;
        }

        /// <summary>Throttled bake scheduling; call once per frame.</summary>
        public void Update(float deltaTime)
        {
            bakeCountdown -= deltaTime;
            if (bakeCountdown > 0f)
                return;
            bakeCountdown = BakeInterval;

            if (font != null && currentText != null && currentText != bakedText)
            {
                BakeText(currentText);
                bakedText = currentText;
            }
        }

        /// <summary>Draws the billboard on top of everything; call LAST per eye.</summary>
        public void Draw(Matrix view, Matrix projection, VrCameraRig rig)
        {
            if (bakedText == null)
                return;

            Vector3 position = rig.EyeAnchor + rig.HeadForward3D * Distance + Vector3.Up * HeightOffset;
            Matrix billboard = Matrix.CreateWorld(position, rig.HeadForward3D, Vector3.Up);

            effect.World = billboard;
            effect.View = view;
            effect.Projection = projection;
            effect.Alpha = 1f;

            graphicsDevice.BlendState = BlendState.AlphaBlend;
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    quadVertices, 0, quadVertices.Length,
                    quadIndices, 0, quadIndices.Length / 3);
            }
        }

        /// <summary>Renders the text once into the render target (lime + black shadow).</summary>
        private void BakeText(string text)
        {
            RenderTargetBinding[] previousBindings = graphicsDevice.GetRenderTargets();
            graphicsDevice.SetRenderTarget(texture);
            graphicsDevice.Clear(Color.Transparent);

            Vector2 textSize = font.MeasureString(text);
            float scale = Math.Min(1f, (TextureWidth - 16f) / Math.Max(textSize.X, 1f));
            Vector2 position = new Vector2(8f, (TextureHeight - textSize.Y * scale) * 0.5f);

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            spriteBatch.DrawString(font, text, position + Vector2.One, Color.Black, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(font, text, position, Color.Lime, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.End();

            graphicsDevice.SetRenderTargets(previousBindings);
        }
    }
}
