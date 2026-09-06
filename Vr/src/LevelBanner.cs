using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// The fading "Level N" in-world text (requirement 4).
    ///
    /// On each level start the text is baked once into a 512x256
    /// RenderTarget2D using the existing Hud SpriteFont (white text with a
    /// black drop shadow, transparent background). While active, a textured
    /// quad is billboarded to the CURRENT head orientation, centered 1.8 m in
    /// front of the virtual eye anchor, and drawn LAST per eye with
    /// DepthStencilState.None so it appears on top of all geometry. Because the
    /// position is recomputed from the live head pose every frame, it stays in
    /// front of the eyes even while the player walks during the fade.
    ///
    /// Fade timing: fully visible for HoldSeconds, then alpha 1 -> 0 over
    /// FadeSeconds (~2.5 s total, then inactive until the next level).
    /// </summary>
    public class LevelBanner
    {
        private const int TextureWidth = 512;
        private const int TextureHeight = 256;

        /// <summary>Distance in front of the eyes where the banner floats (meters).</summary>
        private const float Distance = 1.8f;

        /// <summary>Quad size in meters (matches the 2:1 render target aspect).</summary>
        private const float QuadWidth = 0.9f;
        private const float QuadHeight = 0.45f;

        private const float HoldSeconds = 1.0f;
        private const float FadeSeconds = 1.5f;

        private readonly GraphicsDevice graphicsDevice;
        private readonly SpriteBatch spriteBatch;
        private readonly SpriteFont font;
        private readonly RenderTarget2D texture;
        private readonly BasicEffect effect;
        private readonly VertexPositionTexture[] quadVertices;
        private readonly short[] quadIndices;

        private float remaining;
        private bool bakePending;
        private string pendingText;

        /// <summary>True while the banner is visible (hold or fade phase).</summary>
        public bool IsActive => remaining > 0f;

        public LevelBanner(GraphicsDevice graphicsDevice, SpriteFont font)
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

            // Quad in the local XY plane (facing -Z, i.e. toward a viewer the
            // matrix will be oriented at). UVs map V=0 to the top.
            float halfW = QuadWidth * 0.5f;
            float halfH = QuadHeight * 0.5f;
            quadVertices = new VertexPositionTexture[4];
            quadVertices[0] = new VertexPositionTexture(new Vector3(-halfW, halfH, 0f), new Vector2(0f, 0f)); // top-left
            quadVertices[1] = new VertexPositionTexture(new Vector3(halfW, halfH, 0f), new Vector2(1f, 0f));  // top-right
            quadVertices[2] = new VertexPositionTexture(new Vector3(halfW, -halfH, 0f), new Vector2(1f, 1f)); // bottom-right
            quadVertices[3] = new VertexPositionTexture(new Vector3(-halfW, -halfH, 0f), new Vector2(0f, 1f)); // bottom-left

            quadIndices = new short[] { 0, 1, 2, 0, 2, 3 };
        }

        /// <summary>
        /// Starts showing the given text (e.g. "Level 3"). The text is baked
        /// into the render target lazily during the next Draw (changing render
        /// targets inside Update is avoided deliberately).
        /// </summary>
        public void Show(string text)
        {
            pendingText = text;
            bakePending = true;
            remaining = HoldSeconds + FadeSeconds;
        }

        /// <summary>Advances the fade timer.</summary>
        public void Update(float deltaTime)
        {
            if (remaining > 0f)
            {
                remaining -= deltaTime;
                if (remaining < 0f)
                    remaining = 0f;
            }
        }

        /// <summary>
        /// Draws the billboard quad on top of everything (no depth test).
        /// Must be called LAST in the per-eye draw sequence.
        /// </summary>
        public void Draw(Matrix view, Matrix projection, VrCameraRig rig)
        {
            if (!IsActive)
                return;

            if (bakePending)
                BakeText(pendingText);

            // Billboard facing the current head orientation: float in front of
            // the virtual eyes, always readable.
            Vector3 position = rig.EyeAnchor + rig.HeadForward3D * Distance;
            Matrix billboard = Matrix.CreateWorld(position, rig.HeadForward3D, Vector3.Up);

            float alpha = remaining > FadeSeconds ? 1f : remaining / FadeSeconds;

            effect.World = billboard;
            effect.View = view;
            effect.Projection = projection;
            effect.Alpha = alpha;

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

        /// <summary>Renders the text once into the render target (white + black shadow).</summary>
        private void BakeText(string text)
        {
            bakePending = false;

            RenderTargetBinding[] previousBindings = graphicsDevice.GetRenderTargets();
            graphicsDevice.SetRenderTarget(texture);
            graphicsDevice.Clear(Color.Transparent);

            if (font != null && text != null)
            {
                Vector2 textSize = font.MeasureString(text);
                Vector2 position = new Vector2(
                    (TextureWidth - textSize.X) * 0.5f,
                    (TextureHeight - textSize.Y) * 0.5f);

                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                // Drop shadow for contrast over any background.
                spriteBatch.DrawString(font, text, position + Vector2.One, Color.Black);
                spriteBatch.DrawString(font, text, position, Color.White);
                spriteBatch.End();
            }

            graphicsDevice.SetRenderTargets(previousBindings);
        }
    }
}
