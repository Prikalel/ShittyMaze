using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.Input;
using Maze3D.Entities;
using Maze3D.Maze;
using Maze3D.Graphics;
using Maze3D.Physics;

namespace Maze3D.Core
{
    /// <summary>
    /// Main game class for the 3D maze game (Web-only).
    /// </summary>
    public class Game1 : Game
    {
        private readonly GraphicsDeviceManager graphics;

        private Camera camera;
        private Player player;
        private MazeData mazeData;
        private MazeRenderer mazeRenderer;
        private CollisionDetector collisionDetector;
        private GoalObject goalObject;
        private BasicEffect goalEffect;

        private Texture2D enemyTexture;
        private readonly List<Enemy> enemies = new List<Enemy>();
        private const int EnemyCount = 3;

        private bool contentLoaded = false;

        // First-person weapon overlay drawn fullscreen on top of the 3D scene.
        // The idle weapon (T_fullscreen_first_person_weapon) is always visible;
        // on a left click the fire frame (..._shot_frame_with_fire) is shown for
        // ShotDuration seconds (which is also the shot cooldown) before reverting.
        private SpriteBatch spriteBatch;
        private Texture2D weaponTexture;
        private Texture2D weaponShotTexture;
        private Song shotSound;
        private const float ShotDuration = 1.0f;
        private float shotTimer;
        private bool shootRequested;

        private string baseUrl;

        /// <summary>
        /// Mouse sensitivity for camera rotation (radians per pixel).
        /// </summary>
        public float MouseSensitivity { get; set; } = 0.003f;

        /// <summary>
        /// Accumulated mouse movement delta X (horizontal) to be applied to yaw.
        /// </summary>
        private float mouseDeltaX;

        /// <summary>
        /// Accumulated mouse movement delta Y (vertical). Currently unused because
        /// the camera is locked to horizontal rotation only.
        /// </summary>
        private float mouseDeltaY;

        /// <summary>
        /// Whether continuous forward motion is active. Started by pressing W,
        /// never auto-stopped (S only pauses while held).
        /// </summary>
        private bool isMoving;

        /// <summary>
        /// Keyboard state from the previous frame, used to detect key presses
        /// (edge transitions) rather than held keys.
        /// </summary>
        private KeyboardState previousKeyboardState;

        public Game1(string baseUrl)
        {
            this.baseUrl = baseUrl;
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            Window.Title = "3D First-Person Maze Game";

            mazeData = new MazeData();
            collisionDetector = new CollisionDetector(mazeData);

            var startPos = mazeData.GetStartPosition();
            player = new Player(new Vector3(startPos.X, 0.7f, startPos.Z));
            camera = new Camera(player.Position);
            mazeRenderer = new MazeRenderer(GraphicsDevice, mazeData);

            Vector3 finishPosition = mazeData.GetFinishPosition();
            goalObject = new GoalObject(GraphicsDevice, finishPosition);

            base.Initialize();
        }

        protected override void LoadContent()
        {
            mazeRenderer.LoadContent();

            try
            {
                var wallTexture = Content.Load<Texture2D>("Textures/T_Kirpich_7_BaseColor");
                var floorTexture = Content.Load<Texture2D>("Textures/T_Wood_Floor_2_BaseColor");
                var ceilingTexture = Content.Load<Texture2D>("Textures/T_Ceiling_Armstrong_1_BaseColor");

                mazeRenderer.SetTextures(wallTexture, floorTexture, ceilingTexture);
                contentLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading textures: {ex.Message}. Stack trace: {ex.StackTrace}");
            }

            goalEffect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false
            };

            try
            {
                enemyTexture = Content.Load<Texture2D>("Textures/T_Enemy");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading enemy texture: {ex.Message}");
                enemyTexture = null;
            }

            // Spawn enemies for the first maze now that the texture is available.
            SpawnEnemies();

            LoadFirstPersonWeapon();
        }

        /// <summary>
        /// Loads the fullscreen first-person weapon sprites and the shot sound.
        /// </summary>
        private void LoadFirstPersonWeapon()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);

            try
            {
                weaponTexture = Content.Load<Texture2D>("Textures/T_fullscreen_first_person_weapon");
                weaponShotTexture = Content.Load<Texture2D>("Textures/T_fullscreen_first_person_weapon_shot_frame_with_fire");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading weapon textures: {ex.Message}");
                weaponTexture = null;
                weaponShotTexture = null;
            }

            try
            {
                shotSound = Content.Load<Song>("Sounds/S_shot");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading shot sound: {ex.Message}");
                shotSound = null;
            }
        }

        protected override void Update(GameTime gameTime)
        {
            if (!contentLoaded)
                return;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            HandleMovement(deltaTime);
            HandleMouseRotation();
            UpdateWeapon(deltaTime);

            camera.Position = player.Position;
            camera.UpdateMatrices();
            goalObject?.Update(gameTime);
            foreach (Enemy enemy in enemies)
                enemy.Update(gameTime);

            if (goalObject != null && goalObject.CheckCollision(player.Position))
            {
                RestartLevel();
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// Applies accumulated mouse movement to camera rotation.
        /// Called from JavaScript interop when mouse moves while pointer is locked.
        /// </summary>
        /// <param name="deltaX">Horizontal mouse movement in pixels.</param>
        /// <param name="deltaY">Vertical mouse movement in pixels.</param>
        public void ApplyMouseDelta(float deltaX, float deltaY)
        {
            mouseDeltaX += deltaX;
            mouseDeltaY += deltaY;
        }

        private void HandleMouseRotation()
        {
            // The camera can only rotate horizontally (yaw). Vertical mouse
            // movement (pitch) is intentionally ignored.
            if (mouseDeltaX != 0f)
            {
                float deltaYaw = -mouseDeltaX * MouseSensitivity;
                camera.ApplyRotation(deltaYaw, 0f);

                mouseDeltaX = 0f;
            }

            // Discard vertical delta without applying it.
            mouseDeltaY = 0f;
        }

        /// <summary>
        /// Called from JavaScript interop when the left mouse button is pressed
        /// while the pointer is locked. Queues a shot request; the actual shot
        /// (visual + sound) is applied in <see cref="UpdateWeapon"/>, subject to
        /// the shot cooldown.
        /// </summary>
        public void RequestShoot()
        {
            shootRequested = true;
        }

        /// <summary>
        /// Updates the first-person weapon shot animation/cooldown.
        /// A queued left click starts a <see cref="ShotDuration"/>-long fire frame
        /// and plays the shot sound, but only if the previous shot has finished.
        /// </summary>
        private void UpdateWeapon(float deltaTime)
        {
            if (shootRequested)
            {
                shootRequested = false;

                if (shotTimer <= 0f)
                {
                    shotTimer = ShotDuration;

                    try
                    {
                        if (shotSound != null)
                            MediaPlayer.Play(shotSound);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error playing shot sound: {ex.Message}");
                    }
                }
            }

            if (shotTimer > 0f)
            {
                shotTimer -= deltaTime;
                if (shotTimer < 0f)
                    shotTimer = 0f;
            }
        }

        private void HandleMovement(float deltaTime)
        {
            var keyboardState = Keyboard.GetState();

            // W starts continuous forward motion (detected on key press, not hold).
            if (previousKeyboardState.IsKeyUp(Keys.W) && keyboardState.IsKeyDown(Keys.W))
            {
                isMoving = true;
            }

            // S, while held, temporarily halts the forward motion. Releasing S
            // resumes it (the character "always moves forward" once started).
            bool isStopped = keyboardState.IsKeyDown(Keys.S);

            previousKeyboardState = keyboardState;

            // Always move straight forward at the player's constant speed.
            Vector3 movement = Vector3.Zero;
            if (isMoving && !isStopped)
            {
                Vector3 forward = camera.GetForwardDirection();
                movement = forward * player.Speed * deltaTime;
            }

            if (movement.LengthSquared() > 0)
            {
                Vector3 newPosition = player.Position + movement;

                if (!collisionDetector.CheckCollision(newPosition))
                {
                    player.Position = newPosition;
                }
                else
                {
                    // Slide along walls when the direct path is blocked.
                    Vector3 xMove = new Vector3(movement.X, 0, 0);
                    Vector3 zMove = new Vector3(0, 0, movement.Z);

                    Vector3 testPosX = player.Position + xMove;
                    if (!collisionDetector.CheckCollision(testPosX))
                    {
                        player.Position = testPosX;
                    }

                    Vector3 testPosZ = player.Position + zMove;
                    if (!collisionDetector.CheckCollision(testPosZ))
                    {
                        player.Position = testPosZ;
                    }
                }
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            if (!contentLoaded)
            {
                base.Draw(gameTime);
                return;
            }

            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            RasterizerState rasterizerState = new RasterizerState
            {
                CullMode = CullMode.CullCounterClockwiseFace,
                FillMode = FillMode.Solid
            };
            GraphicsDevice.RasterizerState = rasterizerState;

            mazeRenderer.Draw(camera.ViewMatrix, camera.ProjectionMatrix);
            goalObject?.Draw(goalEffect, camera.ViewMatrix, camera.ProjectionMatrix);

            // Enemies are transparent billboards: alpha-blend so transparent pixels
            // are cut out, and disable back-face culling so the textured face is
            // always visible regardless of winding/platform cull conventions.
            if (enemies.Count > 0)
            {
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;

                foreach (Enemy enemy in enemies)
                    enemy.Draw(camera.ViewMatrix, camera.ProjectionMatrix, camera.Position);
            }

            DrawFirstPersonWeapon();

            base.Draw(gameTime);
        }

        /// <summary>
        /// Draws the fullscreen first-person weapon overlay on top of the 3D scene.
        /// Shows the fire frame while <see cref="shotTimer"/> > 0, otherwise the
        /// idle weapon. Transparent pixels are blended out so the scene shows through.
        /// </summary>
        private void DrawFirstPersonWeapon()
        {
            Texture2D current = shotTimer > 0f ? weaponShotTexture : weaponTexture;
            if (current == null)
                return;

            Viewport viewport = GraphicsDevice.Viewport;
            var destination = new Rectangle(0, 0, viewport.Width, viewport.Height);

            spriteBatch.Begin();
            spriteBatch.Draw(current, destination, Color.White);
            spriteBatch.End();
        }

        /// <summary>
        /// Restarts the level by generating a new maze and resetting player position.
        /// </summary>
        private void RestartLevel()
        {
            int[][] newGrid = MazeGenerator.Generate();
            mazeData.UpdateGrid(newGrid);

            Vector3 finishPosition = mazeData.GetFinishPosition();
            goalObject.UpdatePosition(finishPosition);

            mazeRenderer.RebuildGeometry();

            Vector3 startPos = mazeData.GetStartPosition();
            player.Position = new Vector3(startPos.X, 0.7f, startPos.Z);

            // Stop auto-forward so the player starts each maze stationary.
            isMoving = false;
            previousKeyboardState = Keyboard.GetState();

            camera.Position = player.Position;
            camera.Reset();

            SpawnEnemies();
        }

        /// <summary>
        /// (Re)spawns the enemies for the current maze.
        /// One enemy is always placed in the open cell nearest to the player spawn
        /// (handy for debugging the billboard render). The rest go in random open
        /// cells. Open cells exclude the player start (value 2) and the finish
        /// (value 3), so enemies never overlap the spawn or the goal cube.
        /// </summary>
        private void SpawnEnemies()
        {
            enemies.Clear();
            if (enemyTexture == null)
                return;

            List<(int x, int z)> openCells = mazeData.GetOpenCells();
            if (openCells.Count == 0)
                return;

            Vector3 playerStart = mazeData.GetStartPosition();
            float enemySize = 0.8f * mazeData.CellSize; // 0.8 of a cell, sprite is square

            // Debug enemy: nearest open cell to the player spawn.
            int nearestIndex = 0;
            float nearestDistSq = float.MaxValue;
            for (int i = 0; i < openCells.Count; i++)
            {
                Vector3 w = mazeData.GridToWorld(openCells[i].x, openCells[i].z);
                float dx = w.X - playerStart.X;
                float dz = w.Z - playerStart.Z;
                float distSq = dx * dx + dz * dz;
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestIndex = i;
                }
            }

            var used = new HashSet<int> { nearestIndex };
            Vector3 nearestPos = mazeData.GridToWorld(openCells[nearestIndex].x, openCells[nearestIndex].z);
            enemies.Add(new Enemy(GraphicsDevice, enemyTexture, nearestPos, enemySize));

            // Remaining enemies: random distinct open cells.
            var rng = new Random();
            int remaining = EnemyCount - 1;
            while (remaining > 0 && used.Count < openCells.Count)
            {
                int idx = rng.Next(openCells.Count);
                if (used.Add(idx))
                {
                    Vector3 w = mazeData.GridToWorld(openCells[idx].x, openCells[idx].z);
                    enemies.Add(new Enemy(GraphicsDevice, enemyTexture, w, enemySize));
                    remaining--;
                }
            }
        }
    }
}
