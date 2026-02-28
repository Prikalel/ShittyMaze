using Microsoft.Xna.Framework;

namespace Maze3D.Entities
{
    /// <summary>
    /// Player entity representing the player's state in the game.
    /// Contains position, velocity, and movement properties.
    /// </summary>
    public class Player
    {
        private Vector3 position;
        private Vector3 velocity;
        private float speed;

        /// <summary>
        /// Default movement speed in units per second.
        /// </summary>
        public const float DefaultSpeed = 5.0f;

        /// <summary>
        /// Player eye height from ground.
        /// </summary>
        public const float EyeHeight = 0.7f;

        /// <summary>
        /// Player collision radius.
        /// </summary>
        public const float CollisionRadius = 0.3f;

        /// <summary>
        /// Gets or sets the player's world position.
        /// </summary>
        public Vector3 Position
        {
            get => position;
            set => position = value;
        }

        /// <summary>
        /// Gets or sets the player's velocity.
        /// </summary>
        public Vector3 Velocity
        {
            get => velocity;
            set => velocity = value;
        }

        /// <summary>
        /// Gets or sets the player's movement speed in units per second.
        /// </summary>
        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        /// <summary>
        /// Creates a new player at the specified position.
        /// </summary>
        /// <param name="position">Initial player position.</param>
        public Player(Vector3 position)
        {
            this.position = position;
            velocity = Vector3.Zero;
            speed = DefaultSpeed;
        }

        /// <summary>
        /// Creates a new player at the specified position with custom speed.
        /// </summary>
        /// <param name="position">Initial player position.</param>
        /// <param name="speed">Movement speed in units per second.</param>
        public Player(Vector3 position, float speed)
        {
            this.position = position;
            velocity = Vector3.Zero;
            this.speed = speed;
        }

        /// <summary>
        /// Gets the player's position on the XZ plane (ignoring Y).
        /// </summary>
        /// <returns>2D position vector.</returns>
        public Vector2 GetXzPosition()
        {
            return new Vector2(position.X, position.Z);
        }

        /// <summary>
        /// Sets the player's position from XZ coordinates, maintaining current Y.
        /// </summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="z">Z coordinate.</param>
        public void SetXzPosition(float x, float z)
        {
            position.X = x;
            position.Z = z;
        }
    }
}
