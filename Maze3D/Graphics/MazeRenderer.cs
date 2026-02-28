using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Maze3D.Maze;

namespace Maze3D.Graphics
{
    /// <summary>
    /// Renders the3D maze environment including walls and floor.
    /// Uses BasicEffect for rendering with textured vertices.
    /// </summary>
    public class MazeRenderer
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly MazeData mazeData;
        private BasicEffect effect;

        private VertexBuffer wallVertexBuffer;
        private VertexBuffer floorVertexBuffer;
        private VertexBuffer ceilingVertexBuffer;
        private int wallVertexCount;
        private int floorVertexCount;
        private int ceilingVertexCount;

        // Textures
        private Texture2D wallTexture;
        private Texture2D floorTexture;
        private Texture2D ceilingTexture;

        // Wall height
        private const float WallHeight = 2.0f;

        /// <summary>
        /// Creates a new maze renderer.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="mazeData">The maze data to render.</param>
        public MazeRenderer(GraphicsDevice graphicsDevice, MazeData mazeData)
        {
            this.graphicsDevice = graphicsDevice;
            this.mazeData = mazeData;
        }

        /// <summary>
        /// Loads content and builds vertex buffers.
        /// </summary>
        public void LoadContent()
        {
            // Load textures
            wallTexture = new Texture2D(graphicsDevice, 1, 1);
            wallTexture.Name = "Wall Texture";
            floorTexture = new Texture2D(graphicsDevice, 1, 1);
            floorTexture.Name = "Floor Texture";
            ceilingTexture = new Texture2D(graphicsDevice, 1, 1);
            ceilingTexture.Name = "Ceiling Texture";
            
            // Create basic effect
            effect = new(graphicsDevice)
            {
                VertexColorEnabled = false,
                LightingEnabled = false,
                TextureEnabled = true
            };

            // Build geometry
            BuildWallVertexBuffer();
            BuildFloorVertexBuffer();
            BuildCeilingVertexBuffer();
        }

        /// <summary>
        /// Sets the textures to use for rendering.
        /// </summary>
        public void SetTextures(Texture2D wall, Texture2D floor, Texture2D ceiling)
        {
            wallTexture = wall;
            floorTexture = floor;
            ceilingTexture = ceiling;
        }

        /// <summary>
        /// Rebuilds geometry when maze data changes.
        /// </summary>
        public void RebuildGeometry()
        {
            // Dispose old buffers
            wallVertexBuffer?.Dispose();
            floorVertexBuffer?.Dispose();
            ceilingVertexBuffer?.Dispose();

            // Rebuild geometry with new maze data
            BuildWallVertexBuffer();
            BuildFloorVertexBuffer();
            BuildCeilingVertexBuffer();
        }

        private void BuildWallVertexBuffer()
        {
            var vertices = new System.Collections.Generic.List<VertexPositionColorTexture>();
            float cellSize = mazeData.CellSize;

            for (int z = 0; z < mazeData.Height; z++)
            {
                for (int x = 0; x < mazeData.Width; x++)
                {
                    if (mazeData.IsWall(x, z))
                    {
                        // Calculate wall bounds
                        float minX = x * cellSize;
                        float maxX = (x + 1) * cellSize;
                        float minZ = z * cellSize;
                        float maxZ = (z + 1) * cellSize;
                        float minY = 0f;
                        float maxY = WallHeight;

                        // Add wall faces with CW winding when viewed from inside the corridor
                        // (player is inside the maze, looking at walls from the inside)

                        // Front face (positive Z side) - visible when looking from negative Z
                        // CW order when viewed from inside (negative Z looking toward positive Z)
                        AddQuad(vertices,
                            new(minX, minY, maxZ), new(0, 1),
                            new(minX, maxY, maxZ), new(0, 0),
                            new(maxX, maxY, maxZ), new(1, 0),
                            new(maxX, minY, maxZ), new(1, 1),
                            Color.White);

                        // Back face (negative Z side) - visible when looking from positive Z
                        // CW order when viewed from inside (positive Z looking toward negative Z)
                        AddQuad(vertices,
                            new(maxX, minY, minZ), new(0, 1),
                            new(maxX, maxY, minZ), new(0, 0),
                            new(minX, maxY, minZ), new(1, 0),
                            new(minX, minY, minZ), new(1, 1),
                            Color.White);

                        // Right face (positive X side) - visible when looking from negative X
                        // CW order when viewed from inside (negative X looking toward positive X)
                        AddQuad(vertices,
                            new(maxX, minY, maxZ), new(0, 1),
                            new(maxX, maxY, maxZ), new(0, 0),
                            new(maxX, maxY, minZ), new(1, 0),
                            new(maxX, minY, minZ), new(1, 1),
                            Color.White);

                        // Left face (negative X side) - visible when looking from positive X
                        // CW order when viewed from inside (positive X looking toward negative X)
                        AddQuad(vertices,
                            new(minX, minY, minZ), new(0, 1),
                            new(minX, maxY, minZ), new(0, 0),
                            new(minX, maxY, maxZ), new(1, 0),
                            new(minX, minY, maxZ), new(1, 1),
                            Color.White);

                        // Top face - visible when looking up from below (negative Y)
                        // CW order when viewed from below
                        // This is the top of the wall, mapped to ceiling texture
                        AddQuad(vertices,
                            new(minX, maxY, maxZ), new(0, 1),
                            new(maxX, maxY, maxZ), new(1, 1),
                            new(maxX, maxY, minZ), new(1, 0),
                            new(minX, maxY, minZ), new(0, 0),
                            Color.White);
                    }
                }
            }

            wallVertexCount = vertices.Count;
            wallVertexBuffer = new(
                graphicsDevice,
                typeof(VertexPositionColorTexture),
                wallVertexCount,
                BufferUsage.WriteOnly);
            wallVertexBuffer.SetData(vertices.ToArray());
        }

        private void BuildFloorVertexBuffer()
        {
            var vertices = new System.Collections.Generic.List<VertexPositionColorTexture>();
            float cellSize = mazeData.CellSize;
            float mazeWidth = mazeData.Width * cellSize;
            float mazeHeight = mazeData.Height * cellSize;

            // Create a single large floor quad (visible from above - player looks down)
            // CW winding when viewed from above (from positive Y)
            AddQuad(vertices,
                new(0, 0, 0), new(0, 0),            // bottom-left
                new(mazeWidth, 0, 0), new(mazeData.Width, 0), // bottom-right
                new(mazeWidth, 0, mazeHeight), new(mazeData.Width, mazeData.Height), // top-right
                new(0, 0, mazeHeight), new(0, mazeData.Height), // top-left
                Color.White);

            floorVertexCount = vertices.Count;
            floorVertexBuffer = new(
                graphicsDevice,
                typeof(VertexPositionColorTexture),
                floorVertexCount,
                BufferUsage.WriteOnly);
            floorVertexBuffer.SetData(vertices.ToArray());
        }

        private void BuildCeilingVertexBuffer()
        {
            var vertices = new System.Collections.Generic.List<VertexPositionColorTexture>();
            float cellSize = mazeData.CellSize;
            float mazeWidth = mazeData.Width * cellSize;
            float mazeHeight = mazeData.Height * cellSize;

            // Create ceiling (visible from below - player looks up)
            // CW winding when viewed from below (from negative Y)
            // UV coordinates: 1 repeat per maze cell
            AddQuad(vertices,
                new(mazeWidth, WallHeight, 0), new(mazeData.Width, 0),
                new(0, WallHeight, 0), new(0, 0),
                new(0, WallHeight, mazeHeight), new(0, mazeData.Height),
                new(mazeWidth, WallHeight, mazeHeight), new(mazeData.Width, mazeData.Height),
                Color.White);

            ceilingVertexCount = vertices.Count;
            ceilingVertexBuffer = new(
                graphicsDevice,
                typeof(VertexPositionColorTexture),
                ceilingVertexCount,
                BufferUsage.WriteOnly);
            ceilingVertexBuffer.SetData(vertices.ToArray());
        }

        /// <summary>
        /// Adds a quad (two triangles) to the vertex list.
        /// Uses counter-clockwise winding order (default for XNA/MonoGame).
        /// With CullMode.CullCounterClockwiseFace, CW faces are visible, CCW are culled.
        /// Vertices should be ordered CW when viewed from the visible side.
        /// </summary>
        private void AddQuad(
            System.Collections.Generic.List<VertexPositionColorTexture> vertices,
            Vector3 v0, Vector2 uv0,
            Vector3 v1, Vector2 uv1,
            Vector3 v2, Vector2 uv2,
            Vector3 v3, Vector2 uv3,
            Color color)
        {
            // First triangle
            vertices.Add(new(v0, color, uv0));
            vertices.Add(new(v1, color, uv1));
            vertices.Add(new(v2, color, uv2));

            // Second triangle
            vertices.Add(new(v0, color, uv0));
            vertices.Add(new(v2, color, uv2));
            vertices.Add(new(v3, color, uv3));
        }

        private void DrawBuffer(VertexBuffer buffer, Texture2D texture, int vertexCount)
        {
            if (buffer == null || texture == null || vertexCount == 0) return;
            effect.Texture = texture;
            graphicsDevice.SetVertexBuffer(buffer);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, vertexCount / 3);
            }
        }

        /// <summary>
        /// Draws the maze using the provided view and projection matrices.
        /// </summary>
        /// <param name="viewMatrix">Camera view matrix.</param>
        /// <param name="projectionMatrix">Camera projection matrix.</param>
        public void Draw(Matrix viewMatrix, Matrix projectionMatrix)
        {
            effect.View = viewMatrix;
            effect.Projection = projectionMatrix;
            effect.World = Matrix.Identity;

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            DrawBuffer(floorVertexBuffer, floorTexture, floorVertexCount);
            DrawBuffer(ceilingVertexBuffer, ceilingTexture, ceilingVertexCount);
            DrawBuffer(wallVertexBuffer, wallTexture, wallVertexCount);
        }
    }
}
