using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Maze3D.Entities
{
    /// <summary>
    /// An enemy projectile: a small gray cube that travels in a straight line
    /// toward a captured target position. It despawns when it reaches a
    /// precomputed wall intersection point or when it hits the player.
    /// </summary>
    public class Projectile
    {
        private readonly GraphicsDevice graphicsDevice;
        private VertexPositionColor[] vertices;
        private short[] indices;

        private Vector3 position;
        private readonly Vector3 direction; // Normalized travel direction (XZ).
        private readonly float speed;
        private readonly float despawnDistance; // Distance to the first wall along the path.
        private float traveled;
        private float rotationAngle;

        private readonly float size;
        private readonly float rotationSpeed = 6.0f; // Radians per second (visual spin).

        /// <summary>Current world position of the projectile.</summary>
        public Vector3 Position => position;

        /// <summary>True once the projectile has travelled far enough to hit a wall.</summary>
        public bool ReachedWall => traveled >= despawnDistance;

        /// <summary>
        /// Creates a projectile.
        /// </summary>
        /// <param name="graphicsDevice">Graphics device used for drawing.</param>
        /// <param name="spawnPosition">World position where the shot originates.</param>
        /// <param name="direction">Normalized XZ travel direction.</param>
        /// <param name="speed">Travel speed in world units per second.</param>
        /// <param name="despawnDistance">Distance to the first wall along the path.</param>
        /// <param name="size">Cube edge length in world units.</param>
        public Projectile(GraphicsDevice graphicsDevice, Vector3 spawnPosition, Vector3 direction, float speed, float despawnDistance, float size)
        {
            this.graphicsDevice = graphicsDevice;
            this.position = spawnPosition;
            this.direction = direction;
            this.speed = speed;
            this.despawnDistance = despawnDistance;
            this.size = size;
            BuildCube();
        }

        /// <summary>
        /// Builds a small cube (same winding as GoalObject so it is visible under
        /// the project's CullCounterClockwiseFace rasterizer).
        /// </summary>
        private void BuildCube()
        {
            Color color = Color.Gray;
            float half = size / 2f;

            Vector3[] corners = new Vector3[8];
            corners[0] = new(-half, -half, -half);
            corners[1] = new(half, -half, -half);
            corners[2] = new(half, half, -half);
            corners[3] = new(-half, half, -half);
            corners[4] = new(-half, -half, half);
            corners[5] = new(half, -half, half);
            corners[6] = new(half, half, half);
            corners[7] = new(-half, half, half);

            vertices = new VertexPositionColor[24];

            // Front face
            vertices[0] = new(corners[4], color);
            vertices[1] = new(corners[5], color);
            vertices[2] = new(corners[6], color);
            vertices[3] = new(corners[7], color);

            // Back face
            vertices[4] = new(corners[1], color);
            vertices[5] = new(corners[0], color);
            vertices[6] = new(corners[3], color);
            vertices[7] = new(corners[2], color);

            // Left face
            vertices[8] = new(corners[0], color);
            vertices[9] = new(corners[4], color);
            vertices[10] = new(corners[7], color);
            vertices[11] = new(corners[3], color);

            // Right face
            vertices[12] = new(corners[5], color);
            vertices[13] = new(corners[1], color);
            vertices[14] = new(corners[2], color);
            vertices[15] = new(corners[6], color);

            // Top face
            vertices[16] = new(corners[7], color);
            vertices[17] = new(corners[6], color);
            vertices[18] = new(corners[2], color);
            vertices[19] = new(corners[3], color);

            // Bottom face
            vertices[20] = new(corners[0], color);
            vertices[21] = new(corners[1], color);
            vertices[22] = new(corners[5], color);
            vertices[23] = new(corners[4], color);

            indices = new short[36];
            for (int i = 0; i < 6; i++)
            {
                int faceStart = i * 4;
                int indexStart = i * 6;

                indices[indexStart] = (short)faceStart;
                indices[indexStart + 1] = (short)(faceStart + 1);
                indices[indexStart + 2] = (short)(faceStart + 2);
                indices[indexStart + 3] = (short)faceStart;
                indices[indexStart + 4] = (short)(faceStart + 2);
                indices[indexStart + 5] = (short)(faceStart + 3);
            }
        }

        /// <summary>
        /// Advances the projectile along its direction. Movement is independent of
        /// the firer's speed.
        /// </summary>
        /// <param name="deltaTime">Elapsed seconds since the last frame.</param>
        public void Update(float deltaTime)
        {
            float step = speed * deltaTime;
            position += direction * step;
            traveled += step;
            rotationAngle += rotationSpeed * deltaTime;
        }

        /// <summary>
        /// Draws the projectile as a spinning gray cube.
        /// </summary>
        /// <param name="effect">Vertex-color BasicEffect to render with.</param>
        /// <param name="view">View matrix from the camera.</param>
        /// <param name="projection">Projection matrix.</param>
        public void Draw(BasicEffect effect, Matrix view, Matrix projection)
        {
            effect.World = Matrix.CreateRotationY(rotationAngle) * Matrix.CreateTranslation(position);
            effect.View = view;
            effect.Projection = projection;
            effect.VertexColorEnabled = true;
            effect.LightingEnabled = false;
            effect.TextureEnabled = false;

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
