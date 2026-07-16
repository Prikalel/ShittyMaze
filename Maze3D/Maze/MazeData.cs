using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Maze3D.Maze
{
    /// <summary>
    /// Represents the maze data structure as a 2D grid.
    /// 0 = open path, 1 = wall, 2 = start, 3 = finish.
    /// </summary>
    public class MazeData
    {
        private int[,] grid;
        private int width;
        private int height;
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
        /// Creates a new maze with a predefined layout.
        /// </summary>
        public MazeData()
        {
            grid = CreateGridArray();

            width = grid.GetLength(1);
            height = grid.GetLength(0);
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
            if (x < 0 || x >= width || z < 0 || z >= height)
                return true;

            return grid[z, x] == 1;
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
        /// Gets the starting position for the player (cell with value 2).
        /// </summary>
        /// <returns>World position for player start.</returns>
        public Vector3 GetStartPosition()
        {
            return FindCellPosition(2) ?? GridToWorld(width / 2, height / 2);
        }

        /// <summary>
        /// Gets the finish position for the maze (cell with value 3).
        /// </summary>
        /// <returns>World position for the finish/goal.</returns>
        public Vector3 GetFinishPosition()
        {
            return FindCellPosition(3) ?? GridToWorld(width - 2, height - 2);
        }

        /// <summary>
        /// Returns all open-path cells (grid value 0) as grid coordinates.
        /// Start (value 2) and finish (value 3) cells are NOT included, so a caller
        /// can safely use these positions for enemy placement.
        /// </summary>
        /// <returns>List of (x, z) grid coordinates of every open path cell.</returns>
        public List<(int x, int z)> GetOpenCells()
        {
            var cells = new List<(int x, int z)>();
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    if (grid[z, x] == 0)
                        cells.Add((x, z));
            return cells;
        }

        /// <summary>
        /// Picks a straight patrol path for an enemy starting at the given cell.
        ///
        /// From the start cell it looks at the 4 cardinal neighbours, picks one at
        /// random whose cell is not a wall, then walks in a straight line in that
        /// direction cell-by-cell until it hits a wall or the maze edge. The returned
        /// cell is the far end of that corridor segment (always at least 2 cells away
        /// from the start when an open neighbour exists).
        /// </summary>
        /// <param name="startX">Grid X of the enemy's spawn cell.</param>
        /// <param name="startZ">Grid Z of the enemy's spawn cell.</param>
        /// <param name="rng">Shared random source for direction selection.</param>
        /// <returns>Grid coordinates of the far end of the straight patrol path.</returns>
        public (int x, int z) GetStraightPathEnd(int startX, int startZ, Random rng)
        {
            int[] dxs = { 1, -1, 0, 0 };
            int[] dzs = { 0, 0, 1, -1 };

            // Collect the cardinal directions whose immediate neighbour is walkable.
            var openDirs = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (IsWalkable(startX + dxs[i], startZ + dzs[i]))
                    openDirs.Add(i);
            }

            if (openDirs.Count == 0)
                return (startX, startZ); // Trapped cell: path is a single point.

            int dir = openDirs[rng.Next(openDirs.Count)];
            int dx = dxs[dir];
            int dz = dzs[dir];

            // Extend in a straight line while the next cell is walkable.
            int cx = startX;
            int cz = startZ;
            while (IsWalkable(cx + dx, cz + dz))
            {
                cx += dx;
                cz += dz;
            }
            return (cx, cz);
        }

        /// <summary>
        /// Returns true if a cell is inside the maze and is not a wall (value 1).
        /// Open path (0), start (2) and finish (3) are all considered walkable.
        /// </summary>
        private bool IsWalkable(int x, int z)
        {
            if (x < 0 || x >= width || z < 0 || z >= height)
                return false;
            return grid[z, x] != 1;
        }

        /// <summary>
        /// Returns true if a straight line between two world positions (in the XZ
        /// plane) does not pass through any wall cell. Uses a grid DDA (Amanatides &
        /// Woo voxel traversal) so every cell the segment crosses is tested. The
        /// start and end cells themselves are not checked (they hold the two actors,
        /// which are never inside a wall).
        /// </summary>
        /// <param name="from">Start world position.</param>
        /// <param name="to">End world position.</param>
        /// <returns>True if no wall is crossed between the two points.</returns>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            float dirX = to.X - from.X;
            float dirZ = to.Z - from.Z;
            float length = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 1e-6f)
                return true; // Same point: nothing in between.

            float ux = dirX / length;
            float uz = dirZ / length;

            int x = (int)Math.Floor(from.X / cellSize);
            int z = (int)Math.Floor(from.Z / cellSize);
            int endX = (int)Math.Floor(to.X / cellSize);
            int endZ = (int)Math.Floor(to.Z / cellSize);

            int stepX = ux > 0 ? 1 : (ux < 0 ? -1 : 0);
            int stepZ = uz > 0 ? 1 : (uz < 0 ? -1 : 0);

            float tMaxX, tMaxZ, tDeltaX, tDeltaZ;
            if (stepX != 0)
            {
                float nextBoundaryX = (stepX > 0 ? (x + 1) : x) * cellSize;
                tMaxX = (nextBoundaryX - from.X) / ux;
                tDeltaX = Math.Abs(cellSize / ux);
            }
            else
            {
                tMaxX = float.MaxValue;
                tDeltaX = float.MaxValue;
            }

            if (stepZ != 0)
            {
                float nextBoundaryZ = (stepZ > 0 ? (z + 1) : z) * cellSize;
                tMaxZ = (nextBoundaryZ - from.Z) / uz;
                tDeltaZ = Math.Abs(cellSize / uz);
            }
            else
            {
                tMaxZ = float.MaxValue;
                tDeltaZ = float.MaxValue;
            }

            while (!(x == endX && z == endZ))
            {
                if (tMaxX < tMaxZ)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }

                if (x == endX && z == endZ)
                    break; // Reached the target cell (never a wall).

                if (IsWall(x, z))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Casts a ray from <paramref name="origin"/> in the given (normalized, XZ)
        /// <paramref name="direction"/> and returns the world point where it first
        /// enters a wall cell (or leaves the maze). Used to precompute a projectile's
        /// despawn distance at spawn time.
        /// </summary>
        /// <param name="origin">Ray origin (world position).</param>
        /// <param name="direction">Normalized direction (XZ).</param>
        /// <returns>World position of the first wall hit along the ray.</returns>
        public Vector3 GetRayWallHit(Vector3 origin, Vector3 direction)
        {
            float ux = direction.X;
            float uz = direction.Z;

            int x = (int)Math.Floor(origin.X / cellSize);
            int z = (int)Math.Floor(origin.Z / cellSize);

            int stepX = ux > 0 ? 1 : (ux < 0 ? -1 : 0);
            int stepZ = uz > 0 ? 1 : (uz < 0 ? -1 : 0);

            float tMaxX, tMaxZ, tDeltaX, tDeltaZ;
            if (stepX != 0)
            {
                float nextBoundaryX = (stepX > 0 ? (x + 1) : x) * cellSize;
                tMaxX = (nextBoundaryX - origin.X) / ux;
                tDeltaX = Math.Abs(cellSize / ux);
            }
            else
            {
                tMaxX = float.MaxValue;
                tDeltaX = 0f;
            }

            if (stepZ != 0)
            {
                float nextBoundaryZ = (stepZ > 0 ? (z + 1) : z) * cellSize;
                tMaxZ = (nextBoundaryZ - origin.Z) / uz;
                tDeltaZ = Math.Abs(cellSize / uz);
            }
            else
            {
                tMaxZ = float.MaxValue;
                tDeltaZ = 0f;
            }

            const float maxDistance = 1000f;
            int guard = 0;
            while (guard++ < 10000)
            {
                float t;
                if (tMaxX < tMaxZ)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }

                // IsWall is true for out-of-bounds, so the maze edge is a hit too.
                if (IsWall(x, z))
                    return new Vector3(origin.X + ux * t, origin.Y, origin.Z + uz * t);

                if (t > maxDistance)
                    break;
            }

            return new Vector3(origin.X + ux * maxDistance, origin.Y, origin.Z + uz * maxDistance);
        }

        /// <summary>
        /// Updates the maze grid with a new layout.
        /// </summary>
        /// <param name="newGrid">New grid array (jagged array from MazeGenerator).</param>
        public void UpdateGrid(int[][] newGrid)
        {
            ConvertJaggedTo2D(newGrid);
        }

        private static int[,] CreateGridArray()
        {
            int[][] generated = MazeGenerator.Generate();
            int rows = generated.Length;
            int cols = generated[0].Length;

            int[,] gridArray = new int[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    gridArray[i, j] = generated[i][j];
                }
            }

            return gridArray;
        }

        private Vector3? FindCellPosition(int cellValue)
        {
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    if (grid[z, x] == cellValue)
                        return GridToWorld(x, z);
            return null;
        }

        private void ConvertJaggedTo2D(int[][] source)
        {
            height = source.Length;
            width = source[0].Length;
            grid = new int[height, width];
            for (int i = 0; i < height; i++)
                for (int j = 0; j < width; j++)
                    grid[i, j] = source[i][j];
        }
    }
}
