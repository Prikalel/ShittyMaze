using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.Audio;
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
        private Texture2D enemyShotTexture;
        private readonly List<Enemy> enemies = new List<Enemy>();

        private SoundEffect playerHitSound;
        private SoundEffect enemyShotSound;
        private SoundEffect enemyGotShotSound;  // Played when a player ray hits an enemy.
        private SoundEffect enemyDeadSound;     // Played when an enemy is destroyed.
        private readonly List<Projectile> bullets = new List<Projectile>();
        private BasicEffect bulletEffect;

        // Shared random source for spawn + shooting target selection.
        private readonly Random gameRandom = new Random();

        // Looping background music. One track per level (1..4); levels beyond the
        // available tracks pick one at random. Played via the global MediaPlayer.
        private Song[] mainThemes;
        private bool musicStarted;

        // Enemy projectile tuning.
        private const float ProjectileSpeed = Player.DefaultSpeed * 2f; // 2x player speed
        private const float BulletCollisionRadius = 0.1f;              // 3x smaller than player radius
        private const float ProjectileSize = 0.12f;                    // ~2-3x smaller than the goal cube (0.3)

        // Level progression + lives (health).
        private int levelNumber = 1;
        private int lives = 3;
        private const int MaxLives = 3;

        // Maze texture sets: default (level 1) + alternate sets for levels 2-4.
        private Texture2D defaultWallTexture;
        private Texture2D defaultFloorTexture;
        private Texture2D defaultCeilingTexture;
        private readonly List<(Texture2D wall, Texture2D floor, Texture2D ceiling)> levelTextureSets = new List<(Texture2D wall, Texture2D floor, Texture2D ceiling)>();

        // Random floor textures used from level 5 onward (T_random_floor1..9).
        private readonly List<Texture2D> randomFloorTextures = new List<Texture2D>();

        // Hearts HUD overlay; index = lives - 1 (0..2).
        private Texture2D[] heartsTextures;

        private bool contentLoaded = false;

        // First-person weapon overlay drawn fullscreen on top of the 3D scene.
        // The idle weapon (T_fullscreen_first_person_weapon) is always visible;
        // on a left click the fire frame (..._shot_frame_with_fire) is shown for
        // ShotDuration seconds (which is also the shot cooldown) before reverting.
        private SpriteBatch spriteBatch;
        private Texture2D weaponTexture;
        private Texture2D weaponShotTexture;
        private SoundEffect shotSound;
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
                defaultWallTexture = Content.Load<Texture2D>("Textures/T_Kirpich_7_BaseColor");
                defaultFloorTexture = Content.Load<Texture2D>("Textures/T_Wood_Floor_2_BaseColor");
                defaultCeilingTexture = Content.Load<Texture2D>("Textures/T_Ceiling_Armstrong_1_BaseColor");

                mazeRenderer.SetTextures(defaultWallTexture, defaultFloorTexture, defaultCeilingTexture);
                contentLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading textures: {ex.Message}. Stack trace: {ex.StackTrace}");
            }

            LoadLevelTextureSets();
            LoadRandomFloorTextures();
            LoadHeartsTextures();

            goalEffect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false
            };

            try
            {
                enemyTexture = Content.Load<Texture2D>("Textures/T_Enemy");
                enemyShotTexture = Content.Load<Texture2D>("Textures/T_Enemy_shot_sprite");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading enemy textures: {ex.Message}");
                enemyTexture = null;
                enemyShotTexture = null;
            }

            try
            {
                playerHitSound = Content.Load<SoundEffect>("Sounds/S_player_got_shot");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading player hit sound: {ex.Message}");
                playerHitSound = null;
            }

            try
            {
                enemyShotSound = Content.Load<SoundEffect>("Sounds/S_enemy_shot");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading enemy shot sound: {ex.Message}");
                enemyShotSound = null;
            }

            try
            {
                enemyGotShotSound = Content.Load<SoundEffect>("Sounds/S_enemy_got_shot");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading enemy got shot sound: {ex.Message}");
                enemyGotShotSound = null;
            }

            try
            {
                enemyDeadSound = Content.Load<SoundEffect>("Sounds/S_enemy_dead");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading enemy dead sound: {ex.Message}");
                enemyDeadSound = null;
            }

            bulletEffect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false
            };

            // Spawn enemies for the first maze now that the textures are available.
            SpawnEnemies();

            LoadFirstPersonWeapon();
            LoadMainThemes();
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
                shotSound = Content.Load<SoundEffect>("Sounds/S_shot");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading shot sound: {ex.Message}");
                shotSound = null;
            }
        }

        /// <summary>
        /// Loads the looping background themes S_main_theme1..4. Best effort: a
        /// missing/failed track is skipped, so only the available ones are kept.
        /// </summary>
        private void LoadMainThemes()
        {
            var list = new List<Song>();
            for (int i = 1; i <= 4; i++)
            {
                try
                {
                    list.Add(Content.Load<Song>($"Sounds/S_main_theme{i}"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading main theme {i}: {ex.Message}");
                }
            }
            mainThemes = list.ToArray();
        }

        /// <summary>
        /// Selects and starts the looping background music for the current level.
        /// Levels 1..N play their specific track; levels beyond the number of
        /// available tracks pick one at random. Plays at full (default) volume.
        /// </summary>
        private void PlayLevelMusic()
        {
            musicStarted = true;

            if (mainThemes == null || mainThemes.Length == 0)
                return;

            int idx;
            if (levelNumber >= 1 && levelNumber <= mainThemes.Length)
                idx = levelNumber - 1;                    // Level 1 -> track 1, etc.
            else
                idx = gameRandom.Next(mainThemes.Length); // Beyond available tracks -> random.

            Song theme = mainThemes[idx];

            try
            {
                MediaPlayer.IsRepeating = true;
                MediaPlayer.Volume = 1f;
                MediaPlayer.Play(theme);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing main theme: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts the level music on the first user interaction. Browsers only
        /// unlock the audio context on a user gesture, so the first mouse look,
        /// key press or shot is used to (re)kick the looping music.
        /// </summary>
        private void KickMusic()
        {
            if (musicStarted)
                return;

            PlayLevelMusic();
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

            UpdateEnemyShooting();
            UpdateBullets(deltaTime);

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

            KickMusic(); // First mouse look (after the pointer-lock click) unlocks audio.
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

            KickMusic(); // A left click is a user gesture that unlocks audio.
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
                            shotSound.Play(1f, 0f, 0f); // One-shot; overlaps other sounds.
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error playing shot sound: {ex.Message}");
                    }

                    // Instant hitscan: a cell-wide ray from the player's cell in the
                    // cardinal direction the camera faces. Damages the first enemy it
                    // reaches (or one in the player's own cell). Infinite ammo.
                    PlayerShoot();
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
                KickMusic(); // A key press is a user gesture that unlocks audio.
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

            // Projectiles (opaque gray cubes) drawn with depth, before the
            // alpha-blended enemies.
            if (bullets.Count > 0)
            {
                GraphicsDevice.BlendState = BlendState.Opaque;
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                foreach (Projectile bullet in bullets)
                    bullet.Draw(bulletEffect, camera.ViewMatrix, camera.ProjectionMatrix);
            }

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
            DrawHearts();

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
        /// Draws the hearts HUD overlay: one texture per remaining life count.
        /// </summary>
        private void DrawHearts()
        {
            if (heartsTextures == null)
                return;

            int idx = lives - 1;
            if (idx < 0 || idx >= heartsTextures.Length)
                return;

            Texture2D heart = heartsTextures[idx];
            if (heart == null)
                return;

            Viewport viewport = GraphicsDevice.Viewport;
            var destination = new Rectangle(0, 0, viewport.Width, viewport.Height);

            spriteBatch.Begin();
            spriteBatch.Draw(heart, destination, Color.White);
            spriteBatch.End();
        }

        /// <summary>
        /// Advances to the next level: a new maze is generated with the texture set
        /// for the new level number. Lives are kept (they only reset on death).
        /// </summary>
        private void RestartLevel()
        {
            levelNumber++;
            ApplyLevelTextures(levelNumber);
            RegenerateMaze();
        }

        /// <summary>
        /// Full reset after death: back to level 1 with the default textures and a
        /// fresh set of lives.
        /// </summary>
        private void ResetOnDeath()
        {
            levelNumber = 1;
            lives = MaxLives;
            ApplyLevelTextures(levelNumber);
            RegenerateMaze();
        }

        /// <summary>
        /// Generates a fresh maze, repositions the goal/player/camera and respawns
        /// enemies (which also clears active bullets).
        /// </summary>
        private void RegenerateMaze()
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
            PlayLevelMusic(); // Switch to the new level's looping track.
        }

        /// <summary>
        /// Applies the maze texture set for the given level number. Level 1 uses the
        /// default set; levels >= 2 cycle through the level 2/3/4 texture sets. From
        /// level 5 onward the floor is overridden with a random T_random_floor
        /// texture (walls and ceiling keep cycling as before).
        /// </summary>
        /// <param name="level">Current level number (1-based).</param>
        private void ApplyLevelTextures(int level)
        {
            if (level <= 1 || levelTextureSets.Count == 0)
            {
                mazeRenderer.SetTextures(defaultWallTexture, defaultFloorTexture, defaultCeilingTexture);
                return;
            }

            int idx = (level - 2) % levelTextureSets.Count;
            var set = levelTextureSets[idx];

            Texture2D floor = set.floor;
            // From level 5 on, fill the whole floor with a random floor texture.
            if (level >= 5 && randomFloorTextures.Count > 0)
                floor = randomFloorTextures[gameRandom.Next(randomFloorTextures.Count)];

            mazeRenderer.SetTextures(set.wall, floor, set.ceiling);
        }

        /// <summary>
        /// Loads the alternate maze texture sets for levels 2, 3 and 4. Best effort:
        /// a failing set is skipped and ApplyLevelTextures falls back to default.
        /// </summary>
        private void LoadLevelTextureSets()
        {
            for (int lvl = 2; lvl <= 4; lvl++)
            {
                try
                {
                    var wall = Content.Load<Texture2D>($"Textures/T_level{lvl}_wall");
                    var floor = Content.Load<Texture2D>($"Textures/T_level{lvl}_floor");
                    var ceiling = Content.Load<Texture2D>($"Textures/T_level{lvl}_ceiling");
                    levelTextureSets.Add((wall, floor, ceiling));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading level {lvl} textures: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Loads the random floor textures T_random_floor1..9 used from level 5 on.
        /// Best effort: a missing/failed texture is skipped.
        /// </summary>
        private void LoadRandomFloorTextures()
        {
            for (int i = 1; i <= 9; i++)
            {
                try
                {
                    randomFloorTextures.Add(Content.Load<Texture2D>($"Textures/T_random_floor{i}"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading random floor {i} texture: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Loads the hearts HUD overlays (T_1_hearts..T_3_hearts). Best effort.
        /// </summary>
        private void LoadHeartsTextures()
        {
            heartsTextures = new Texture2D[MaxLives];
            for (int i = 0; i < MaxLives; i++)
            {
                try
                {
                    heartsTextures[i] = Content.Load<Texture2D>($"Textures/T_{i + 1}_hearts");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading hearts {i + 1} texture: {ex.Message}");
                    heartsTextures[i] = null;
                }
            }
        }

        /// <summary>
        /// (Re)spawns the enemies for the current maze.
        ///
        /// All enemies spawn in random distinct open cells (no special-case
        /// placement). The number of enemies and their health scale with the current
        /// level:
        ///   count = level + 2   (level 1 -> 3 enemies, level 2 -> 4, ...)
        ///   hp    = min(level, 5) (level 1 -> 1 hp, ..., level 5+ -> 5 hp)
        ///
        /// Open cells exclude the player start (value 2) and the finish (value 3),
        /// so enemies never overlap the spawn or the goal cube.
        /// </summary>
        private void SpawnEnemies()
        {
            enemies.Clear();
            bullets.Clear();
            if (enemyTexture == null)
                return;

            List<(int x, int z)> openCells = mazeData.GetOpenCells();
            if (openCells.Count == 0)
                return;

            float enemySize = 0.8f * mazeData.CellSize; // 0.8 of a cell, sprite is square
            int enemyHP = Math.Min(levelNumber, 5);     // HP scales with level, capped at 5.
            int desiredCount = levelNumber + 2;         // Level 1 -> 3, level 2 -> 4, ...
            int toSpawn = Math.Min(desiredCount, openCells.Count);

            // Creates an enemy at the given open cell and assigns it a straight
            // patrol path that begins at that cell (chosen at spawn, never changes).
            void SpawnAt(int cellIndex)
            {
                var (cx, cz) = openCells[cellIndex];
                Vector3 spawnWorld = mazeData.GridToWorld(cx, cz);
                var (fx, fz) = mazeData.GetStraightPathEnd(cx, cz, gameRandom);
                Vector3 pathEndWorld = mazeData.GridToWorld(fx, fz);
                enemies.Add(new Enemy(GraphicsDevice, enemyTexture, enemyShotTexture, spawnWorld, pathEndWorld, enemySize, enemyHP));
            }

            // Random distinct open cells until we reach the desired count.
            var used = new HashSet<int>();
            while (enemies.Count < toSpawn)
            {
                int idx = gameRandom.Next(openCells.Count);
                if (used.Add(idx))
                    SpawnAt(idx);
            }
        }

        /// <summary>
        /// Each enemy with line of sight to the player and an elapsed shot cooldown
        /// fires a projectile toward the player's exact world position (captured at
        /// the moment of firing), then enters its shot cooldown and shows the shot
        /// sprite. Shooting never slows the enemy down.
        /// </summary>
        private void UpdateEnemyShooting()
        {
            Vector3 playerPos = player.Position;

            foreach (Enemy enemy in enemies)
            {
                if (enemy.IsDead || !enemy.CanShoot)
                    continue;

                // Line of sight between the enemy's cell and the player's cell.
                var (ex, ez) = mazeData.WorldToGrid(enemy.Position);
                var (px, pz) = mazeData.WorldToGrid(playerPos);
                Vector3 fromCell = mazeData.GridToWorld(ex, ez);
                Vector3 toCell = mazeData.GridToWorld(px, pz);
                if (!mazeData.HasLineOfSight(fromCell, toCell))
                    continue;

                // Fire toward the player's exact captured position.
                Vector3 target = playerPos;
                enemy.TriggerShotSprite();
                enemy.ResetShootCooldown();

                Vector3 spawn = new Vector3(enemy.Position.X, target.Y, enemy.Position.Z);
                Vector3 dir = new Vector3(target.X - spawn.X, 0f, target.Z - spawn.Z);
                float fireDistance = dir.Length();
                if (fireDistance < 1e-6f)
                    continue;
                dir.Normalize();

                // Enemy shot sound: louder up close, fading with distance (in cells).
                PlayEnemyShotSound(fireDistance / mazeData.CellSize);

                // Precompute the despawn point: first wall along the travel direction.
                Vector3 despawn = mazeData.GetRayWallHit(spawn, dir);
                float despawnDist = Vector2.Distance(
                    new Vector2(spawn.X, spawn.Z),
                    new Vector2(despawn.X, despawn.Z));

                bullets.Add(new Projectile(GraphicsDevice, spawn, dir, ProjectileSpeed, despawnDist, ProjectileSize));
            }
        }

        /// <summary>
        /// Moves every active projectile, then removes those that hit the player
        /// (playing the hit sound) or reached their precomputed wall despawn point.
        /// </summary>
        private void UpdateBullets(float deltaTime)
        {
            if (bullets.Count == 0)
                return;

            Vector3 playerPos = player.Position;
            float hitRadius = BulletCollisionRadius + CollisionDetector.DefaultPlayerRadius;
            float hitRadiusSq = hitRadius * hitRadius;

            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                Projectile bullet = bullets[i];
                bullet.Update(deltaTime);

                bool remove = false;

                // 2D (XZ) overlap between bullet and player collision radii.
                float dx = bullet.Position.X - playerPos.X;
                float dz = bullet.Position.Z - playerPos.Z;
                if (dx * dx + dz * dz <= hitRadiusSq)
                {
                    PlayPlayerHitSound();
                    remove = true; // Despawn immediately on hit.

                    lives--;
                    if (lives <= 0)
                    {
                        // Out of lives: full reset to level 1.
                        // RegenerateMaze() (inside) clears active bullets, so stop iterating.
                        ResetOnDeath();
                        return;
                    }
                }
                else if (bullet.ReachedWall)
                {
                    remove = true;
                }

                if (remove)
                    bullets.RemoveAt(i);
            }
        }

        /// <summary>
        /// Instant hitscan shot fired by the player. A ray one cell wide leaves the
        /// player's cell in the cardinal direction the camera faces and steps cell by
        /// cell until it reaches a wall (or the maze edge) or the first cell that
        /// contains an enemy. The enemy in that cell takes 1 damage; if several
        /// enemies share the cell (or share the player's cell) a random one is hit.
        /// The ray stops at the first hit and never damages more than one enemy.
        /// </summary>
        private void PlayerShoot()
        {
            // Snap the camera's horizontal forward to one of the 4 cardinal axes.
            Vector3 forward = camera.GetForwardDirection();
            int dx = 0, dz = 0;
            if (Math.Abs(forward.X) >= Math.Abs(forward.Z))
                dx = Math.Sign(forward.X);
            else
                dz = Math.Sign(forward.Z);

            // A zero direction (degenerate forward) cannot shoot anywhere.
            if (dx == 0 && dz == 0)
                return;

            var (px, pz) = mazeData.WorldToGrid(player.Position);

            // First, an enemy in the player's own cell is hit instantly.
            Enemy target = FindRandomEnemyInCell(px, pz);
            int steps = 0;

            if (target == null)
            {
                int cx = px;
                int cz = pz;
                while (true)
                {
                    cx += dx;
                    cz += dz;
                    steps++;

                    // A wall (or out of bounds) stops the ray before this cell.
                    if (mazeData.IsWall(cx, cz))
                        return;

                    target = FindRandomEnemyInCell(cx, cz);
                    if (target != null)
                        break;
                }
            }

            // Distance in cells along the ray == number of cells stepped.
            DamageEnemy(target, steps);
        }

        /// <summary>
        /// Returns a random alive enemy currently occupying the given cell, or null
        /// if none is there. Cell membership follows <see cref="MazeData.WorldToGrid"/>.
        /// </summary>
        private Enemy FindRandomEnemyInCell(int cellX, int cellZ)
        {
            Enemy found = null;
            int count = 0;
            foreach (Enemy enemy in enemies)
            {
                if (enemy.IsDead)
                    continue;

                var (ex, ez) = mazeData.WorldToGrid(enemy.Position);
                if (ex == cellX && ez == cellZ)
                {
                    count++;
                    // Reservoir sampling: uniform random pick without a second list.
                    if (gameRandom.Next(count) == 0)
                        found = enemy;
                }
            }
            return found;
        }

        /// <summary>
        /// Applies 1 damage to an enemy, plays the hit sound, and on death plays the
        /// death sound and removes the enemy (despawn: no longer rendered, moved or
        /// able to shoot). Both sounds fade with the player-to-enemy distance.
        /// </summary>
        /// <param name="enemy">The enemy to damage.</param>
        /// <param name="distanceInCells">Player-to-enemy distance in cell units.</param>
        private void DamageEnemy(Enemy enemy, int distanceInCells)
        {
            enemy.TakeDamage(1);
            PlayEnemyGotShotSound(distanceInCells);

            if (enemy.IsDead)
            {
                PlayEnemyDeadSound(distanceInCells);
                enemies.Remove(enemy);
            }
        }

        /// <summary>
        /// Plays the "enemy got shot" sound with distance-based volume.
        /// </summary>
        /// <param name="distanceInCells">Player-to-enemy distance in cell units.</param>
        private void PlayEnemyGotShotSound(float distanceInCells)
        {
            if (enemyGotShotSound == null)
                return;

            float volumePercent = ShotVolumeFromDistance(distanceInCells);
            if (volumePercent <= 0f)
                return;

            try
            {
                enemyGotShotSound.Play(volumePercent / 100f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing enemy got shot sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the "enemy dead" sound with distance-based volume.
        /// </summary>
        /// <param name="distanceInCells">Player-to-enemy distance in cell units.</param>
        private void PlayEnemyDeadSound(float distanceInCells)
        {
            if (enemyDeadSound == null)
                return;

            float volumePercent = ShotVolumeFromDistance(distanceInCells);
            if (volumePercent <= 0f)
                return;

            try
            {
                enemyDeadSound.Play(volumePercent / 100f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing enemy dead sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the "player got shot" sound (best effort).
        /// </summary>
        private void PlayPlayerHitSound()
        {
            if (playerHitSound == null)
                return;

            try
            {
                if (playerHitSound != null)
                    playerHitSound.Play(1f, 0f, 0f); // One-shot; overlaps other sounds.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing player hit sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the enemy shot sound with a volume that depends on the enemy-to-player
        /// distance at the moment of firing. <paramref name="distanceInCells"/> is the
        /// distance in cell units (world distance / CellSize):
        ///   f(x) = 100         if 0 <= x <= 2  (full volume up to 2 cells)
        ///   f(x) = -25x + 150  if 2 < x <= 6   (linear fade)
        ///   f(x) = 0           if x > 6          (silent)
        /// </summary>
        /// <param name="distanceInCells">Enemy-to-player distance in cell units.</param>
        private void PlayEnemyShotSound(float distanceInCells)
        {
            if (enemyShotSound == null)
                return;

            float volumePercent = ShotVolumeFromDistance(distanceInCells);
            if (volumePercent <= 0f)
                return;

            try
            {
                // One-shot instance with per-play volume; overlaps other sounds
                // (no global MediaPlayer volume to leak).
                enemyShotSound.Play(volumePercent / 100f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing enemy shot sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Distance-based enemy shot volume (0..100) per the design spec.
        /// </summary>
        /// <param name="x">Distance in cells.</param>
        /// <returns>Volume percentage 0..100.</returns>
        private static float ShotVolumeFromDistance(float x)
        {
            if (x <= 2f)
                return 100f;
            if (x <= 6f)
                return -25f * x + 150f;
            return 0f;
        }
    }
}
