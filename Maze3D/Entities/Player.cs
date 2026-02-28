using Microsoft.Xna.Framework;

namespace Maze3D.Entities
{
    /// <summary>
    /// Player entity representing the player's state in the game.
    /// </summary>
    public class Player
    {
        public const float DefaultSpeed = 5.0f;

        public Vector3 Position { get; set; }

        public float Speed { get; }

        public Player(Vector3 position)
        {
            Position = position;
            Speed = DefaultSpeed;
        }
    }
}
