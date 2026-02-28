using Microsoft.Xna.Framework;

namespace Maze3D.Maze
{
    /// <summary>
    /// Represents the maze data structure as a2D grid.
    /// 0 = open path, 1 = wall.
    /// </summary>
    public class MazeData
    {
        private readonly int[,] grid;
        private readonly int width;
        private readonly int height;
        private readonly float cellSize;

        /// <summary>
        /// Gets the maze width in cells.
        /// </summary>
        public int Width => width;

        /// <summary>
        /// Gets the maze height in cells.
        /// </summary>
        public int Height => height;

        /// <summary>
        /// Gets the size of each cell in world units.
        /// </summary>
        public float CellSize => cellSize;

        /// <summary>
        /// Gets the2D grid array.
        /// </summary>
        public int[,] Grid => grid;

        /// <summary>
        /// Creates a new maze with a predefined layout.
        /// </summary>
        public MazeData()
        {
            // Predefined maze layout (15x15)
            // 1 = wall, 0 = path
            grid = new int[,]
            {
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                { 1, 2, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1 },
                { 1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 0, 1, 0, 1 },
                { 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1 },
                { 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 1, 0, 1 },
                { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1 },
                { 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 1 },
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
                { 1, 0, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 0, 1 },
                { 1, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1 },
                { 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 1, 1, 1, 1 },
                { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1 },
                { 1, 1, 1, 0, 1, 1, 1, 0, 1, 0, 1, 0, 1, 0, 1 },
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3, 1 },
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }
            };

            width = grid.GetLength(1); // X dimension
            height = grid.GetLength(0); // Z dimension
            cellSize = 2.0f;
        }

        /// <summary>
        /// Checks if a grid cell is a wall.
        /// </summary>
        /// <param name="x">Grid X coordinate.</param>
        /// <param name="z">Grid Z coordinate.</param>
        /// <returns>True if the cell is a wall, false otherwise.</returns>
        public bool IsWall(int x, int z)
        {
            // Check bounds
            if (x < 0 || x >= width || z < 0 || z >= height)
                return true; // Out of bounds treated as wall

            return grid[z, x] == 1;
        }

        /// <summary>
        /// Gets the value of a grid cell.
        /// </summary>
        /// <param name="x">Grid X coordinate.</param>
        /// <param name="z">Grid Z coordinate.</param>
        /// <returns>Cell value (0 = path, 1 = wall).</returns>
        public int GetCell(int x, int z)
        {
            if (x < 0 || x >= width || z < 0 || z >= height)
                return 1; // Out of bounds treated as wall

            return grid[z, x];
        }

        /// <summary>
        /// Converts world position to grid coordinates.
        /// </summary>
        /// <param name="worldPosition">World position vector.</param>
        /// <returns>Tuple containing grid X and Z coordinates.</returns>
        public (int x, int z) WorldToGrid(Vector3 worldPosition)
        {
            int gridX = (int)(worldPosition.X / cellSize);
            int gridZ = (int)(worldPosition.Z / cellSize);
            return (gridX, gridZ);
        }

        /// <summary>
        /// Converts world position to grid coordinates.
        /// </summary>
        /// <param name="worldX">World X coordinate.</param>
        /// <param name="worldZ">World Z coordinate.</param>
        /// <returns>Tuple containing grid X and Z coordinates.</returns>
        public (int x, int z) WorldToGrid(float worldX, float worldZ)
        {
            int gridX = (int)(worldX / cellSize);
            int gridZ = (int)(worldZ / cellSize);
            return (gridX, gridZ);
        }

        /// <summary>
        /// Converts grid coordinates to world position (cell center).
        /// </summary>
        /// <param name="gridX">Grid X coordinate.</param>
        /// <param name="gridZ">Grid Z coordinate.</param>
        /// <returns>World position at cell center.</returns>
        public Vector3 GridToWorld(int gridX, int gridZ)
        {
            return new Vector3(
                gridX * cellSize + cellSize / 2f,
                0f,
                gridZ * cellSize + cellSize / 2f
            );
        }

        /// <summary>
        /// Gets the starting position for the player (first open cell).
        /// </summary>
        /// <returns>World position for player start.</returns>
        public Vector3 GetStartPosition()
        {
            // Find first open cell (typically near top-left)
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (grid[z, x] == 0)
                    {
                        return GridToWorld(x, z);
                    }
                }
            }
            // Fallback to center if no open cell found
            return GridToWorld(width / 2, height / 2);
        }

        /// <summary>
        /// Gets the total world width of the maze.
        /// </summary>
        public float GetWorldWidth()
        {
            return width * cellSize;
        }

        /// <summary>
        /// Gets the total world height (depth) of the maze.
        /// </summary>
        public float GetWorldHeight()
        {
            return height * cellSize;
        }
    }
}
