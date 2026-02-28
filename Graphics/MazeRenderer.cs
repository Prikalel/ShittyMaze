using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Maze3D.Maze;

namespace Maze3D.Graphics
{
    /// <summary>
    /// Renders the3D maze environment including walls and floor.
    /// Uses BasicEffect for rendering with vertex colors.
    /// </summary>
    public class MazeRenderer
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly MazeData mazeData;
        private BasicEffect effect;

        private VertexBuffer wallVertexBuffer;
        private VertexBuffer floorVertexBuffer;
        private int wallVertexCount;
        private int floorVertexCount;

        // Wall colors
        private static readonly Color WallColorTop = new Color(180, 140, 100);
        private static readonly Color WallColorSide = new Color(140, 100, 70);
        private static readonly Color FloorColor = new Color(60, 60, 80);
        private static readonly Color CeilingColor = new Color(40, 40, 50);

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
            // Create basic effect
            effect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false
            };

            // Build geometry
            BuildWallVertexBuffer();
            BuildFloorVertexBuffer();
        }

        private void BuildWallVertexBuffer()
        {
            var vertices = new System.Collections.Generic.List<VertexPositionColor>();
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
                            new Vector3(minX, minY, maxZ),
                            new Vector3(minX, maxY, maxZ),
                            new Vector3(maxX, maxY, maxZ),
                            new Vector3(maxX, minY, maxZ),
                            WallColorSide);

                        // Back face (negative Z side) - visible when looking from positive Z
                        // CW order when viewed from inside (positive Z looking toward negative Z)
                        AddQuad(vertices,
                            new Vector3(maxX, minY, minZ),
                            new Vector3(maxX, maxY, minZ),
                            new Vector3(minX, maxY, minZ),
                            new Vector3(minX, minY, minZ),
                            WallColorSide);

                        // Right face (positive X side) - visible when looking from negative X
                        // CW order when viewed from inside (negative X looking toward positive X)
                        AddQuad(vertices,
                            new Vector3(maxX, minY, maxZ),
                            new Vector3(maxX, maxY, maxZ),
                            new Vector3(maxX, maxY, minZ),
                            new Vector3(maxX, minY, minZ),
                            WallColorSide);

                        // Left face (negative X side) - visible when looking from positive X
                        // CW order when viewed from inside (positive X looking toward negative X)
                        AddQuad(vertices,
                            new Vector3(minX, minY, minZ),
                            new Vector3(minX, maxY, minZ),
                            new Vector3(minX, maxY, maxZ),
                            new Vector3(minX, minY, maxZ),
                            WallColorSide);

                        // Top face - visible when looking up from below (negative Y)
                        // CW order when viewed from below
                        AddQuad(vertices,
                            new Vector3(minX, maxY, maxZ),
                            new Vector3(maxX, maxY, maxZ),
                            new Vector3(maxX, maxY, minZ),
                            new Vector3(minX, maxY, minZ),
                            WallColorTop);
                    }
                }
            }

            wallVertexCount = vertices.Count;
            wallVertexBuffer = new VertexBuffer(
                graphicsDevice,
                typeof(VertexPositionColor),
                wallVertexCount,
                BufferUsage.WriteOnly);
            wallVertexBuffer.SetData(vertices.ToArray());
        }

        private void BuildFloorVertexBuffer()
        {
            var vertices = new System.Collections.Generic.List<VertexPositionColor>();
            float cellSize = mazeData.CellSize;
            float mazeWidth = mazeData.Width * cellSize;
            float mazeHeight = mazeData.Height * cellSize;

            // Create a single large floor quad (visible from above - player looks down)
            // CW winding when viewed from above (from positive Y)
            AddQuad(vertices,
                new Vector3(0, 0, 0),
                new Vector3(0, 0, mazeHeight),
                new Vector3(mazeWidth, 0, mazeHeight),
                new Vector3(mazeWidth, 0, 0),
                FloorColor);

            // Create ceiling (visible from below - player looks up)
            // CW winding when viewed from below (from negative Y)
            AddQuad(vertices,
                new Vector3(mazeWidth, WallHeight, 0),
                new Vector3(0, WallHeight, 0),
                new Vector3(0, WallHeight, mazeHeight),
                new Vector3(mazeWidth, WallHeight, mazeHeight),
                CeilingColor);

            floorVertexCount = vertices.Count;
            floorVertexBuffer = new VertexBuffer(
                graphicsDevice,
                typeof(VertexPositionColor),
                floorVertexCount,
                BufferUsage.WriteOnly);
            floorVertexBuffer.SetData(vertices.ToArray());
        }

        /// <summary>
        /// Adds a quad (two triangles) to the vertex list.
        /// Uses counter-clockwise winding order (default for XNA/MonoGame).
        /// With CullMode.CullCounterClockwiseFace, CW faces are visible, CCW are culled.
        /// Vertices should be ordered CW when viewed from the visible side.
        /// </summary>
        private void AddQuad(
            System.Collections.Generic.List<VertexPositionColor> vertices,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Color color)
        {
            // First triangle
            vertices.Add(new VertexPositionColor(v0, color));
            vertices.Add(new VertexPositionColor(v1, color));
            vertices.Add(new VertexPositionColor(v2, color));

            // Second triangle
            vertices.Add(new VertexPositionColor(v0, color));
            vertices.Add(new VertexPositionColor(v2, color));
            vertices.Add(new VertexPositionColor(v3, color));
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

            // Draw floor and ceiling
            graphicsDevice.SetVertexBuffer(floorVertexBuffer);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, floorVertexCount / 3);
            }

            // Draw walls
            graphicsDevice.SetVertexBuffer(wallVertexBuffer);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, wallVertexCount / 3);
            }
        }
    }
}
