using Microsoft.Xna.Framework;

namespace Maze3D.Core
{
    /// <summary>
    /// First-person camera with mouse-controlled look rotation.
    /// Uses Euler angles (yaw and pitch) for orientation.
    /// </summary>
    public class Camera
    {
        // Camera properties
        private Vector3 position;
        private float yaw;      // Horizontal rotation (Y-axis)
        private float pitch;    // Vertical rotation (X-axis)

        // Camera settings
        private const float DefaultFieldOfView = MathHelper.PiOver4 * 2; // 45 degrees
        private const float DefaultNearPlane = 0.1f;
        private const float DefaultFarPlane = 1000f;
        private const float DefaultMouseSensitivity = 0.002f;

        // Matrices
        private Matrix viewMatrix;
        private Matrix projectionMatrix;

        // Settings
        public float FieldOfView { get; set; }
        public float NearPlane { get; set; }
        public float FarPlane { get; set; }
        public float MouseSensitivity { get; set; }
        public float AspectRatio { get; private set; }

        /// <summary>
        /// Camera world position.
        /// </summary>
        public Vector3 Position
        {
            get => position;
            set => position = value;
        }

        /// <summary>
        /// Horizontal rotation in radians.
        /// </summary>
        public float Yaw => yaw;

        /// <summary>
        /// Vertical rotation in radians (clamped).
        /// </summary>
        public float Pitch => pitch;

        /// <summary>
        /// The view matrix for rendering.
        /// </summary>
        public Matrix ViewMatrix => viewMatrix;

        /// <summary>
        /// The projection matrix for rendering.
        /// </summary>
        public Matrix ProjectionMatrix => projectionMatrix;

        /// <summary>
        /// Creates a new camera at the specified position.
        /// </summary>
        /// <param name="position">Initial camera position.</param>
        public Camera(Vector3 position)
        {
            this.position = position;
            yaw = 0f;
            pitch = 0f;

            FieldOfView = DefaultFieldOfView;
            NearPlane = DefaultNearPlane;
            FarPlane = DefaultFarPlane;
            MouseSensitivity = DefaultMouseSensitivity;

            // Default aspect ratio (will be updated when UpdateMatrices is called)
            AspectRatio = 16f / 9f;

            UpdateMatrices();
        }

        /// <summary>
        /// Applies mouse movement to camera rotation.
        /// </summary>
        /// <param name="deltaX">Mouse X delta in pixels.</param>
        /// <param name="deltaY">Mouse Y delta in pixels.</param>
        public void ApplyMouseLook(float deltaX, float deltaY)
        {
            // Update yaw (horizontal rotation)
            yaw -= deltaX * MouseSensitivity;

            // Update pitch (vertical rotation)
            pitch -= deltaY * MouseSensitivity;

            // Clamp pitch to prevent camera flip (±89 degrees)
            const float maxPitch = MathHelper.PiOver2 - 0.01f;
            pitch = MathHelper.Clamp(pitch, -maxPitch, maxPitch);

            // Normalize yaw to 0-2π range
            while (yaw < 0)
                yaw += MathHelper.TwoPi;
            while (yaw > MathHelper.TwoPi)
                yaw -= MathHelper.TwoPi;
        }

        /// <summary>
        /// Gets the forward direction vector based on current rotation.
        /// </summary>
        /// <returns>Normalized forward vector (Y component is0 for FPS movement).</returns>
        public Vector3 GetForwardDirection()
        {
            // For FPS movement, we want horizontal-only forward
            // (not looking up/down affects movement direction)
            return new Vector3(
                (float)System.Math.Sin(yaw),
                0f,
                (float)System.Math.Cos(yaw)
            );
        }

        /// <summary>
        /// Gets the right direction vector (perpendicular to forward).
        /// Calculated as cross product of forward and up vectors.
        /// </summary>
        /// <returns>Normalized right vector.</returns>
        public Vector3 GetRightDirection()
        {
        // Right = Forward × Up (cross product gives perpendicular vector)
        Vector3 forward = GetForwardDirection();
        return Vector3.Cross(forward, Vector3.Up);
        }

        /// <summary>
        /// Gets the actual look direction including pitch (for rendering).
        /// </summary>
        /// <returns>Normalized look direction vector.</returns>
        public Vector3 GetLookDirection()
        {
            return new Vector3(
                (float)(System.Math.Cos(pitch) * System.Math.Sin(yaw)),
                (float)System.Math.Sin(pitch),
                (float)(System.Math.Cos(pitch) * System.Math.Cos(yaw))
            );
        }

        /// <summary>
        /// Updates view and projection matrices.
        /// Call this after changing position or rotation, or when viewport resizes.
        /// </summary>
        /// <param name="aspectRatio">Optional new aspect ratio.</param>
        public void UpdateMatrices(float? aspectRatio = null)
        {
            if (aspectRatio.HasValue)
            {
                AspectRatio = aspectRatio.Value;
            }

            // Calculate view matrix
            Vector3 lookDirection = GetLookDirection();
            Vector3 target = position + lookDirection;
            viewMatrix = Matrix.CreateLookAt(position, target, Vector3.Up);

            // Calculate projection matrix
            projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                FieldOfView,
                AspectRatio,
                NearPlane,
                FarPlane
            );
        }
    }
}
