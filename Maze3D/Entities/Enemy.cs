using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Maze3D.Entities
{
    /// <summary>
    /// A stationary enemy rendered as a camera-facing billboard (sprite).
    ///
    /// The mesh is a single vertical quad (2 triangles / 4 vertices) which, every
    /// frame, is rotated about the Y axis so its front face points at the camera.
    /// This keeps the textured picture fully visible from the player's eye.
    ///
    /// Notes on rendering:
    ///  - Lighting is disabled everywhere in this project, so vertex normals are
    ///    irrelevant. Visibility is governed purely by triangle winding + cull mode.
    ///  - The quad is wound CLOCKWISE as seen from its +Z face (the side that ends
    ///    up pointing at the camera), which makes the textured face survive the
    ///    project's CullCounterClockwiseFace rasterizer.
    ///  - In addition the enemy is drawn with CullMode.None + AlphaBlend so the
    ///    textured face is always shown and transparent pixels are cut out.
    /// </summary>
    public class Enemy
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly BasicEffect effect;
        private readonly Texture2D texture;

        private readonly VertexPositionTexture[] vertices;
        private readonly short[] indices;

        private readonly Vector3 position;
        private readonly float size;

        /// <summary>
        /// Creates a billboard enemy.
        /// </summary>
        /// <param name="graphicsDevice">Graphics device used for drawing.</param>
        /// <param name="texture">Enemy sprite texture (supports transparency).</param>
        /// <param name="position">
        /// World position (an XZ cell center). Only X/Z are used; the quad is always
        /// grounded on the floor so its base sits at Y = 0.
        /// </param>
        /// <param name="size">Quad width and height in world units.</param>
        public Enemy(GraphicsDevice graphicsDevice, Texture2D texture, Vector3 position, float size)
        {
            this.graphicsDevice = graphicsDevice;
            this.texture = texture;
            // Ground to floor: ignore any incoming Y, base at Y = 0.
            this.position = new Vector3(position.X, 0f, position.Z);
            this.size = size;

            effect = new BasicEffect(graphicsDevice)
            {
                TextureEnabled = true,
                Texture = texture,
                LightingEnabled = false,
                VertexColorEnabled = false
            };

            vertices = new VertexPositionTexture[4];
            indices = new short[6];
            BuildQuad();
        }

        /// <summary>
        /// Builds a vertical quad centered on the X/Z origin, grounded on the floor
        /// (bottom at Y = 0, top at Y = size). The quad initially faces +Z.
        ///
        /// Winding is CLOCKWISE when viewed from +Z (the side facing the camera after
        /// the per-frame billboard rotation), so the textured face is rendered under
        /// the project's CullCounterClockwiseFace cull mode.
        /// </summary>
        private void BuildQuad()
        {
            float half = size / 2f;

            // Viewed from +Z (the side that will face the camera):
            //   top-left(-half, size)      top-right(half, size)
            //        +--------------------------+
            //        |                          |
            //   bottom-left(-half, 0)   bottom-right(half, 0)
            //
            // UVs: top of the quad maps to V=0 (top of the texture) so the sprite
            // appears right-side up.
            vertices[0] = new VertexPositionTexture(new Vector3(-half, size, 0f), new Vector2(0f, 0f)); // top-left
            vertices[1] = new VertexPositionTexture(new Vector3(half, size, 0f), new Vector2(1f, 0f));  // top-right
            vertices[2] = new VertexPositionTexture(new Vector3(half, 0f, 0f), new Vector2(1f, 1f));   // bottom-right
            vertices[3] = new VertexPositionTexture(new Vector3(-half, 0f, 0f), new Vector2(0f, 1f));  // bottom-left

            // Triangle 1: top-left -> top-right -> bottom-right  (CW from +Z)
            indices[0] = 0;
            indices[1] = 1;
            indices[2] = 2;
            // Triangle 2: top-left -> bottom-right -> bottom-left (CW from +Z)
            indices[3] = 0;
            indices[4] = 2;
            indices[5] = 3;
        }

        /// <summary>
        /// No behaviour yet — enemies just stand still. Kept for future use
        /// (movement, animation, AI, etc.).
        /// </summary>
        public void Update(GameTime gameTime)
        {
        }

        /// <summary>
        /// Draws the billboard so its textured face points at the camera.
        /// Transparent areas of the sprite are blended out (cut).
        /// </summary>
        /// <param name="view">View matrix from the camera.</param>
        /// <param name="projection">Projection matrix.</param>
        /// <param name="cameraPosition">Camera world position (the billboard faces it).</param>
        public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition)
        {
            // Cylindrical billboard: rotate only around Y so the quad's +Z face aims
            // at the camera's XZ position. The camera is yaw-locked, so a Y-only
            // billboard is correct and the sprite stays upright.
            Vector3 toCamera = cameraPosition - position;
            toCamera.Y = 0f;
            if (toCamera.LengthSquared() < 1e-6f)
                toCamera = Vector3.UnitZ;
            toCamera.Normalize();

            // Matrix.CreateRotationY(theta) maps (0,0,1) -> (sin theta, 0, cos theta).
            // We want (0,0,1) to align with toCamera, therefore theta = atan2(x, z).
            float angle = (float)Math.Atan2(toCamera.X, toCamera.Z);

            effect.World = Matrix.CreateRotationY(angle) * Matrix.CreateTranslation(position);
            effect.View = view;
            effect.Projection = projection;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    vertices, 0, vertices.Length,
                    indices, 0, indices.Length / 3);
            }
        }
    }
}
