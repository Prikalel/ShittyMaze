using System;
using System.Linq;
using Labyrinthian;

namespace Maze3D.Maze;

public static class MazeGenerator
{
    /// <summary>
    /// Generate a random maze array. Start position is marked as 2, finish as 3.
    /// </summary>
    /// <returns>A jagged array representing the maze: 0 = path, 1 = wall, 2 = start, 3 = finish.</returns>
    public static int[][] Generate()
    {
        return Generate(10, 10); // return Generate(15, 15);
    }

    /// <summary>
    /// Generate a random maze array with specified dimensions.
    /// </summary>
    /// <param name="width">Width of the maze in cells.</param>
    /// <param name="height">Height of the maze in cells.</param>
    /// <returns>A jagged array representing the maze: 0 = path, 1 = wall, 2 = start, 3 = finish.</returns>
    public static int[][] Generate(int width, int height)
    {
        // Create an orthogonal maze (2D grid)
        var maze = new OrthogonalMaze(height, width);

        // Generate using Prim's algorithm
        var generator = new PrimGeneration(maze);
        generator.Generate();

        // Convert to int[][] format
        // The output format expands each cell to 2x2+1 to show walls
        int outputHeight = height * 2 + 1;
        int outputWidth = width * 2 + 1;

        int[][] mazeArray = new int[outputHeight][];
        for (int i = 0; i < outputHeight; i++)
        {
            mazeArray[i] = new int[outputWidth];
            for (int j = 0; j < outputWidth; j++)
                mazeArray[i][j] = 1; // Fill with walls initially
        }

        // Convert Labyrinthian cells to the maze array format
        // Use Index to calculate row and column
        int cols = width;

        foreach (var cell in maze.Cells)
        {
            if (cell == null) continue;

            // Calculate row and column from cell Index
            int cellIndex = cell.Index;
            int cellRow = cellIndex / cols;
            int cellCol = cellIndex % cols;

            int row = cellRow * 2 + 1;
            int col = cellCol * 2 + 1;
            mazeArray[row][col] = 0; // Mark cell as path

            // Check if cell is connected to its neighbors
            var neighbors = cell.Neighbors;
            foreach (var neighbor in neighbors)
            {
                if (neighbor == null) continue;

                // Check if cells are connected (there's a passage between them)
                if (maze.AreCellsConnected(cell, neighbor))
                {
                    int neighborIndex = neighbor.Index;
                    int neighborRow = neighborIndex / cols;
                    int neighborCol = neighborIndex % cols;

                    int neighborOutputRow = neighborRow * 2 + 1;
                    int neighborOutputCol = neighborCol * 2 + 1;

                    // Open the wall between cell and neighbor
                    int wallRow = (row + neighborOutputRow) / 2;
                    int wallCol = (col + neighborOutputCol) / 2;

                    if (wallRow >= 0 && wallRow < outputHeight && wallCol >= 0 && wallCol < outputWidth)
                    {
                        mazeArray[wallRow][wallCol] = 0;
                    }
                }
            }
        }

        // Mark start position (top-left cell)
        int startRow = 1;
        int startCol = 1;
        mazeArray[startRow][startCol] = 2;

        // Mark finish position (bottom-right cell)
        int endRow = (height - 1) * 2 + 1;
        int endCol = (width - 1) * 2 + 1;
        mazeArray[endRow][endCol] = 3;

        return mazeArray;
    }
}
