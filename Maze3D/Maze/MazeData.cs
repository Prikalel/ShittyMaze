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
