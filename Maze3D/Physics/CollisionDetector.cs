using Microsoft.Xna.Framework;
using Maze3D.Maze;

namespace Maze3D.Physics
{
    /// <summary>
    /// Handles collision detection between the player and maze walls.
    /// Uses grid-based collision with a bounding sphere approach.
    /// </summary>
    public class CollisionDetector
    {
        private readonly MazeData mazeData;
        private readonly float playerRadius;

        /// <summary>
        /// Default player collision radius.
        /// </summary>
        public const float DefaultPlayerRadius = 0.3f;

        /// <summary>
        /// Creates a new collision detector for the given maze.
        /// </summary>
        /// <param name="mazeData">The maze data to check collisions against.</param>
        public CollisionDetector(MazeData mazeData)
        {
            this.mazeData = mazeData;
            playerRadius = DefaultPlayerRadius;
        }

        /// <summary>
        /// Checks if a position collides with any wall in the maze.
        /// </summary>
        /// <param name="position">The world position to check.</param>
        /// <returns>True if there is a collision, false otherwise.</returns>
        public bool CheckCollision(Vector3 position)
        {
            return CheckCollision(position, playerRadius);
        }

        /// <summary>
        /// Checks if a position with the given radius collides with any wall.
        /// </summary>
        /// <param name="position">The world position to check.</param>
        /// <param name="radius">The collision radius.</param>
        /// <returns>True if there is a collision, false otherwise.</returns>
        public bool CheckCollision(Vector3 position, float radius)
        {
            float cellSize = mazeData.CellSize;

            // Get the grid cell the player is in
            var (centerX, centerZ) = mazeData.WorldToGrid(position);

            // Check surrounding cells (3x3 area around player)
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int checkX = centerX + dx;
                    int checkZ = centerZ + dz;

                    // If this cell is a wall, check for collision
                    if (mazeData.IsWall(checkX, checkZ))
                    {
                        // Calculate wall bounds
                        float wallMinX = checkX * cellSize;
                        float wallMaxX = (checkX + 1) * cellSize;
                        float wallMinZ = checkZ * cellSize;
                        float wallMaxZ = (checkZ + 1) * cellSize;

                        // Find closest point on wall to player
                        float closestX = MathHelper.Clamp(position.X, wallMinX, wallMaxX);
                        float closestZ = MathHelper.Clamp(position.Z, wallMinZ, wallMaxZ);

                        // Calculate distance from player to closest point
                        float distX = position.X - closestX;
                        float distZ = position.Z - closestZ;
                        float distanceSquared = distX * distX + distZ * distZ;

                        // Check if player's bounding sphere intersects the wall
                        if (distanceSquared < radius * radius)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
