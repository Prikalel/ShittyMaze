using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Maze3D.Entities
{
    /// <summary>
    /// A camera-facing billboard enemy that patrols back and forth along a fixed,
    /// straight corridor path and can shoot the player when it has line of sight.
    ///
    /// The mesh is a single vertical quad (2 triangles / 4 vertices) which, every
    /// frame, is rotated about the Y axis so its front face points at the camera.
    /// This keeps the textured picture fully visible from the player's eye.
    ///
    /// Movement: once spawned the enemy owns an immutable straight path (two world
    /// endpoints). It walks from one end to the other and back forever, ignoring
    /// walls, the player, the goal and other enemies by design. Shooting does not
    /// affect its movement speed.
    ///
    /// Shooting: when the game logic (Game1) decides the enemy fires, it calls
    /// <see cref="TriggerShotSprite"/> + <see cref="ResetShootCooldown"/>. The shot
    /// sprite is shown for <see cref="ShotSpriteDuration"/> seconds before reverting
    /// to the default sprite; the shot cooldown (>= 3s) gates further shots.
    ///
    /// Rendering notes:
    ///  - Lighting is disabled everywhere in this project, so vertex normals are
    ///    irrelevant. Visibility is governed purely by triangle winding + cull mode.
    ///  - The quad is wound CLOCKWISE as seen from its +Z face (the side that ends
    ///    up pointing at the camera), so the textured face survives the project's
    ///    CullCounterClockwiseFace rasterizer.
    ///  - The enemy is drawn with CullMode.None + AlphaBlend so the textured face
    ///    is always shown and transparent pixels are cut out (both sprites).
    /// </summary>
    public class Enemy
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly BasicEffect effect;
        private readonly Texture2D texture;       // Default sprite.
        private readonly Texture2D shotTexture;   // Sprite shown while firing.

        private readonly VertexPositionTexture[] vertices;
        private readonly short[] indices;

        private readonly float size;

        // Patrol path (a straight corridor). The enemy ping-pongs between the ends.
        private readonly Vector3 pathStart;
        private readonly Vector3 pathEnd;
        private Vector3 currentTarget;

        // Current world position (moves along the path every frame).
        private Vector3 position;

        // Shooting state.
        private float shootCooldown;   // Time left before the enemy can fire again.
        private float shotSpriteTimer; // Time left the shot sprite stays visible.

        // Health / damage state.
        private int health;
        private float damageFlashTimer; // Time left the red damage tint stays visible.

        // Texture tint (BasicEffect.DiffuseColor multiplies the sprite RGB).
        // Red while the damage flash is active, white (identity) otherwise.
        private static readonly Vector3 NormalTint = new Vector3(1f, 1f, 1f);
        private static readonly Vector3 DamageTint = new Vector3(1f, 0.2f, 0.2f);

        /// <summary>
        /// Minimum time (seconds) between two shots from the same enemy.
        /// </summary>
        public const float ShotCooldownDuration = 3.0f;

        /// <summary>
        /// How long (seconds) the shot sprite is shown after firing.
        /// </summary>
        public const float ShotSpriteDuration = 0.5f;

        /// <summary>
        /// How long (seconds) the red damage tint is shown after the enemy is hit.
        /// </summary>
        public const float DamageFlashDuration = 0.3f;

        /// <summary>
        /// Enemy movement speed in world units per second.
        /// Half of the player's normal speed (see <see cref="Player.DefaultSpeed"/>).
        /// </summary>
        public const float MoveSpeed = Player.DefaultSpeed / 2f;

        /// <summary>Current world position of the enemy.</summary>
        public Vector3 Position => position;

        /// <summary>True when the shot cooldown has elapsed and the enemy may fire.</summary>
        public bool CanShoot => shootCooldown <= 0f;

        /// <summary>Remaining health points. 0 means destroyed.</summary>
        public int Health => health;

        /// <summary>True once the enemy has been destroyed (health <= 0).</summary>
        public bool IsDead => health <= 0;

        /// <summary>
        /// Creates a billboard enemy that patrols along a straight segment.
        /// </summary>
        /// <param name="graphicsDevice">Graphics device used for drawing.</param>
        /// <param name="texture">Default enemy sprite texture (supports transparency).</param>
        /// <param name="shotTexture">Sprite shown for <see cref="ShotSpriteDuration"/> after firing.</param>
        /// <param name="spawnPosition">
        /// World position where the enemy starts and which is one end of its path.
        /// Only X/Z are used; the quad is always grounded on the floor (Y = 0).
        /// </param>
        /// <param name="pathEndPosition">World position of the other end of the patrol path.</param>
        /// <param name="size">Quad width and height in world units.</param>
        /// <param name="health">Starting health points.</param>
        public Enemy(GraphicsDevice graphicsDevice, Texture2D texture, Texture2D shotTexture, Vector3 spawnPosition, Vector3 pathEndPosition, float size, int health)
        {
            this.graphicsDevice = graphicsDevice;
            this.texture = texture;
            this.shotTexture = shotTexture;
            this.size = size;
            this.health = health;

            // Ground to floor: ignore any incoming Y, base at Y = 0.
            this.position = new Vector3(spawnPosition.X, 0f, spawnPosition.Z);
            this.pathStart = this.position;
            this.pathEnd = new Vector3(pathEndPosition.X, 0f, pathEndPosition.Z);
            this.currentTarget = this.pathEnd;

            effect = new BasicEffect(graphicsDevice)
            {
                TextureEnabled = true,
                Texture = texture,
                LightingEnabled = false,
                VertexColorEnabled = false
            };

            vertices = new VertexPositionTexture[4];
            indices = new short[6];
            BuildQuad();
        }

        /// <summary>
        /// Builds a vertical quad centered on the X/Z origin, grounded on the floor
        /// (bottom at Y = 0, top at Y = size). The quad initially faces +Z.
        ///
        /// Winding is CLOCKWISE when viewed from +Z (the side facing the camera after
        /// the per-frame billboard rotation), so the textured face is rendered under
        /// the project's CullCounterClockwiseFace cull mode.
        /// </summary>
        private void BuildQuad()
        {
            float half = size / 2f;

            // Viewed from +Z (the side that will face the camera):
            //   top-left(-half, size)      top-right(half, size)
            //        +--------------------------+
            //        |                          |
            //   bottom-left(-half, 0)   bottom-right(half, 0)
            //
            // UVs: top of the quad maps to V=0 (top of the texture) so the sprite
            // appears right-side up.
            vertices[0] = new VertexPositionTexture(new Vector3(-half, size, 0f), new Vector2(0f, 0f)); // top-left
            vertices[1] = new VertexPositionTexture(new Vector3(half, size, 0f), new Vector2(1f, 0f));  // top-right
            vertices[2] = new VertexPositionTexture(new Vector3(half, 0f, 0f), new Vector2(1f, 1f));   // bottom-right
            vertices[3] = new VertexPositionTexture(new Vector3(-half, 0f, 0f), new Vector2(0f, 1f));  // bottom-left

            // Triangle 1: top-left -> top-right -> bottom-right  (CW from +Z)
            indices[0] = 0;
            indices[1] = 1;
            indices[2] = 2;
            // Triangle 2: top-left -> bottom-right -> bottom-left (CW from +Z)
            indices[3] = 0;
            indices[4] = 2;
            indices[5] = 3;
        }

        /// <summary>
        /// Per-frame update: patrol movement, shot cooldown and shot-sprite revert.
        /// Movement and shooting are independent, so firing never slows the enemy.
        /// </summary>
        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // A destroyed enemy is inert: no movement, no shooting, no sprite revert.
            // (Game1 removes destroyed enemies from its list, so this is a safety guard.)
            if (IsDead)
                return;

            // --- Patrol movement ---
            Vector3 toTarget = currentTarget - position;
            toTarget.Y = 0f;
            float distance = toTarget.Length();

            if (distance > 1e-5f)
            {
                float step = MoveSpeed * dt;
                if (step >= distance)
                {
                    position = currentTarget;
                    currentTarget = (currentTarget == pathEnd) ? pathStart : pathEnd;
                }
                else
                {
                    position += toTarget * (step / distance);
                }
            }

            // --- Shot cooldown (does not block movement) ---
            if (shootCooldown > 0f)
            {
                shootCooldown -= dt;
                if (shootCooldown < 0f)
                    shootCooldown = 0f;
            }

            // --- Shot sprite revert ---
            if (shotSpriteTimer > 0f)
            {
                shotSpriteTimer -= dt;
                if (shotSpriteTimer <= 0f)
                {
                    shotSpriteTimer = 0f;
                    effect.Texture = texture; // Revert to the default sprite.
                }
            }

            // --- Damage flash timer (red tint) ---
            if (damageFlashTimer > 0f)
            {
                damageFlashTimer -= dt;
                if (damageFlashTimer < 0f)
                    damageFlashTimer = 0f;
            }
        }

        /// <summary>
        /// Shows the shot sprite for <see cref="ShotSpriteDuration"/> seconds.
        /// </summary>
        public void TriggerShotSprite()
        {
            shotSpriteTimer = ShotSpriteDuration;
            if (shotTexture != null)
                effect.Texture = shotTexture;
        }

        /// <summary>
        /// Resets the shot cooldown so the enemy must wait before firing again.
        /// </summary>
        public void ResetShootCooldown()
        {
            shootCooldown = ShotCooldownDuration;
        }

        /// <summary>
        /// Applies damage to the enemy and triggers the red damage tint. Has no
        /// effect once the enemy is already destroyed.
        /// </summary>
        /// <param name="amount">Damage to apply (health points).</param>
        public void TakeDamage(int amount)
        {
            if (IsDead)
                return;

            health -= amount;
            if (health < 0)
                health = 0;

            // Show the red tint regardless of the current sprite (default or firing).
            damageFlashTimer = DamageFlashDuration;
        }

        /// <summary>
        /// Draws the billboard so its textured face points at the camera.
        /// Transparent areas of the sprite are blended out (cut).
        /// </summary>
        /// <param name="view">View matrix from the camera.</param>
        /// <param name="projection">Projection matrix.</param>
        /// <param name="cameraPosition">Camera world position (the billboard faces it).</param>
        public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition)
        {
            // Cylindrical billboard: rotate only around Y so the quad's +Z face aims
            // at the camera's XZ position. The camera is yaw-locked, so a Y-only
            // billboard is correct and the sprite stays upright.
            Vector3 toCamera = cameraPosition - position;
            toCamera.Y = 0f;
            if (toCamera.LengthSquared() < 1e-6f)
                toCamera = Vector3.UnitZ;
            toCamera.Normalize();

            // Matrix.CreateRotationY(theta) maps (0,0,1) -> (sin theta, 0, cos theta).
            // We want (0,0,1) to align with toCamera, therefore theta = atan2(x, z).
            float angle = (float)Math.Atan2(toCamera.X, toCamera.Z);

            effect.World = Matrix.CreateRotationY(angle) * Matrix.CreateTranslation(position);
            effect.View = view;
            effect.Projection = projection;

            // Red tint while the damage flash is active; applies to whichever sprite
            // (default or firing) is currently bound, so a hit during a shot still
            // shows red.
            effect.DiffuseColor = damageFlashTimer > 0f ? DamageTint : NormalTint;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    vertices, 0, vertices.Length,
                    indices, 0, indices.Length / 3);
            }
        }
    }
}
