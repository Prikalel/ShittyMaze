using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Maze3D.Entities
{
    /// <summary>
    /// Represents the goal/finish object in the maze.
    /// A small green cube with rotation animation.
    /// </summary>
    public class GoalObject
    {
        private readonly GraphicsDevice graphicsDevice;
        private VertexPositionColor[] vertices;
        private short[] indices;
        private float rotationAngle;
        private Vector3 position;
        private readonly float size = 0.3f; // Small cube size
        private readonly float rotationSpeed = 2.0f; // Radians per second

        /// <summary>
        /// Creates a new goal object at the specified position.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for rendering.</param>
        /// <param name="position">World position for the goal.</param>
        public GoalObject(GraphicsDevice graphicsDevice, Vector3 position)
        {
            this.graphicsDevice = graphicsDevice;
            this.position = position + new Vector3(0, 0.5f, 0); // Raise slightly above floor
            InitializeCube();
        }

        private void InitializeCube()
        {
            Color color = Color.LimeGreen;

            Vector3[] corners = new Vector3[8];
            float halfSize = size / 2f;

            corners[0] = new(-halfSize, -halfSize, -halfSize);
            corners[1] = new(halfSize, -halfSize, -halfSize);
            corners[2] = new(halfSize, halfSize, -halfSize);
            corners[3] = new(-halfSize, halfSize, -halfSize);
            corners[4] = new(-halfSize, -halfSize, halfSize);
            corners[5] = new(halfSize, -halfSize, halfSize);
            corners[6] = new(halfSize, halfSize, halfSize);
            corners[7] = new(-halfSize, halfSize, halfSize);

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
        /// Updates the goal object (rotation animation).
        /// </summary>
        /// <param name="gameTime">Game time for frame-independent animation.</param>
        public void Update(GameTime gameTime)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            rotationAngle += rotationSpeed * deltaTime;

            // Keep angle in reasonable range
            if (rotationAngle > MathHelper.TwoPi)
                rotationAngle -= MathHelper.TwoPi;
        }

        /// <summary>
        /// Updates the goal position to a new location.
        /// </summary>
        /// <param name="newPosition">New world position for the goal.</param>
        public void UpdatePosition(Vector3 newPosition)
        {
            position = newPosition + new Vector3(0, 0.5f, 0); // Raise slightly above floor
        }

        /// <summary>
        /// Draws the goal object.
        /// </summary>
        /// <param name="effect">The basic effect to use for rendering.</param>
        /// <param name="view">View matrix from camera.</param>
        /// <param name="projection">Projection matrix.</param>
        public void Draw(BasicEffect effect, Matrix view, Matrix projection)
        {
            effect.View = view;
            effect.Projection = projection;
            effect.World = Matrix.CreateRotationY(rotationAngle) * Matrix.CreateTranslation(position);
            effect.VertexColorEnabled = true;
            effect.LightingEnabled = false;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    vertices,
                    0,
                    vertices.Length,
                    indices,
                    0,
                    indices.Length / 3
                );
            }
        }

        /// <summary>
        /// Checks if the player has reached the goal.
        /// </summary>
        /// <param name="playerPosition">Player's world position.</param>
        /// <param name="threshold">Distance threshold for collision.</param>
        /// <returns>True if player reached the goal.</returns>
        public bool CheckCollision(Vector3 playerPosition, float threshold = 0.8f)
        {
            float distance = Vector3.Distance(
                new(playerPosition.X, 0, playerPosition.Z),
                new(position.X, 0, position.Z)
            );
            return distance < threshold;
        }
    }
}
