using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.XR;
using Maze3D.Entities;
using Maze3D.Maze;
using Maze3D.Graphics;
using Maze3D.Physics;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// VR port of the Maze3D gameplay (Maze3D/Core/Game1.cs) for PICO 4 style
    /// OpenXR standalone headsets.
    ///
    /// What changed relative to the web game (see VR_ADAPTATION_PLAN.md 3.5):
    ///  - XR lifecycle per the proven ShittyMazeVr reference (XRDevice,
    ///    BeginSessionAsync(VR), BeginFrame -> per-eye Draw -> EndFrame);
    ///  - head-ROTATION-only stereo camera (<see cref="VrCameraRig"/>): real
    ///    head translation never moves the player or camera;
    ///  - locomotion via the right thumbstick relative to head yaw (the W-key
    ///    auto-run latch is gone); collision/slide reuses CollisionDetector;
    ///  - shooting via the right trigger: continuous-direction hitscan from
    ///    the 3D weapon's muzzle (no cardinal snapping), S_shot sound and a
    ///    short vibration pulse on the right controller; infinite ammo;
    ///  - the weapon is the 3D TT-33 (<see cref="VrWeapon"/>) rigidly mounted
    ///    on the right controller; HP (max 3) is shown as weapon redness;
    ///    the 2D fullscreen weapon/hearts overlays are not ported;
    ///  - the level number is a fading in-world billboard (<see cref="LevelBanner"/>)
    ///    drawn on top of all geometry; the permanent 2D HUD counter is gone.
    ///
    /// All gameplay subsystem classes (MazeData, MazeGenerator, MazeRenderer,
    /// CollisionDetector, Player, Enemy, Projectile, GoalObject) are reused
    /// unchanged - they are compiled into this project from the Maze3D sources.
    /// </summary>
    public class VrMazeGame : Game
    {
        private readonly GraphicsDeviceManager graphics;
        private readonly XRDevice xrDevice;

        // VR subsystems.
        private readonly VrCameraRig cameraRig = new VrCameraRig();
        private readonly XrInput input = new XrInput();
        private VrWeapon weapon;
        private LevelBanner banner;

        // Reused game state (ported from Game1).
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
        private SoundEffect enemyGotShotSound;
        private SoundEffect enemyDeadSound;
        private readonly List<Projectile> bullets = new List<Projectile>();
        private BasicEffect bulletEffect;

        private readonly Random gameRandom = new Random();

        private Song[] mainThemes;

        // Enemy projectile tuning (same as Game1).
        private const float ProjectileSpeed = Player.DefaultSpeed * 2f;
        private const float BulletCollisionRadius = 0.1f;
        private const float ProjectileSize = 0.12f;

        // Level progression + lives.
        private int levelNumber = 1;
        private int lives = 3;
        private const int MaxLives = 3;

        // Maze texture sets.
        private Texture2D defaultWallTexture;
        private Texture2D defaultFloorTexture;
        private Texture2D defaultCeilingTexture;
        private readonly List<(Texture2D wall, Texture2D floor, Texture2D ceiling)> levelTextureSets =
            new List<(Texture2D wall, Texture2D floor, Texture2D ceiling)>();
        private readonly List<Texture2D> randomFloorTextures = new List<Texture2D>();

        // HUD font (used only for the fading level banner).
        private SpriteFont hudFont;

        private bool contentLoaded;

        // Weapon shooting state.
        private SoundEffect shotSound;
        private const float ShotDuration = 1.0f; // Shot cooldown, seconds.
        private float shotTimer;

        // Stereo projection planes. Near 0.05 keeps the pistol (held ~0.2-0.5 m
        // from the eyes) from clipping into the near plane.
        private const float NearPlane = 0.05f;
        private const float FarPlane = 1000f;

        public VrMazeGame()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            graphics.IsFullScreen = true;
            graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;

            // OXR requires at least feature level 9.3.
            graphics.GraphicsProfile = GraphicsProfile.HiDef;

            // Synchronize to the headset refresh rate.
            graphics.SynchronizeWithVerticalRetrace = true;
            IsFixedTimeStep = false;
            // 72Hz frame rate for oculus.
            TargetElapsedTime = TimeSpan.FromTicks(138888);

            // We don't care if the main window is focused or not
            // because we render on the XR rendertargets.
            InactiveSleepTime = TimeSpan.FromSeconds(0);

            xrDevice = new XRDevice("ShittyMazeVR", this.Services);
            Services.AddService<XRDevice>(xrDevice);
        }

        protected override void Initialize()
        {
            mazeData = new MazeData();
            collisionDetector = new CollisionDetector(mazeData);

            var startPos = mazeData.GetStartPosition();
            // Y is irrelevant to XZ collision; use the VR eye height for cleanliness.
            player = new Player(new Vector3(startPos.X, VrCameraRig.EyeHeight, startPos.Z));

            mazeRenderer = new MazeRenderer(GraphicsDevice, mazeData);
            goalObject = new GoalObject(GraphicsDevice, mazeData.GetFinishPosition());

            base.Initialize();

            // Initialize XR device and select VR mode.
            xrDevice.BeginSessionAsync(XRSessionMode.VR);
            try
            {
                // Pico runtimes may not support the LocalFloor reference space.
                xrDevice.TrackFloorLevelAsync(true);
            }
            catch (NotImplementedException) { /* LocalFloor unsupported; continue without floor tracking. */ }
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

            try
            {
                hudFont = Content.Load<SpriteFont>("Fonts/Hud");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading HUD font: {ex.Message}");
                hudFont = null;
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
                enemyShotTexture = Content.Load<Texture2D>("Textures/T_Enemy_shot_sprite");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading enemy textures: {ex.Message}");
                enemyTexture = null;
                enemyShotTexture = null;
            }

            playerHitSound = LoadSound("Sounds/S_player_got_shot");
            enemyShotSound = LoadSound("Sounds/S_enemy_shot");
            enemyGotShotSound = LoadSound("Sounds/S_enemy_got_shot");
            enemyDeadSound = LoadSound("Sounds/S_enemy_dead");
            shotSound = LoadSound("Sounds/S_shot");

            bulletEffect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false
            };

            // Spawn enemies for the first maze now that the textures are available.
            SpawnEnemies();

            // 3D TT-33 from the raw glTF AndroidAssets (best effort: a load
            // failure disables the visible weapon but not the game).
            try
            {
                weapon = new VrWeapon(GraphicsDevice);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading weapon model: {ex.Message}. Stack trace: {ex.StackTrace}");
                weapon = null;
            }

            banner = new LevelBanner(GraphicsDevice, hudFont);
            banner.Show($"Level {levelNumber}");

            LoadMainThemes();
            PlayLevelMusic();
        }

        private SoundEffect LoadSound(string assetName)
        {
            try
            {
                return Content.Load<SoundEffect>(assetName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading sound '{assetName}': {ex.Message}");
                return null;
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
        /// available tracks pick one at random.
        /// </summary>
        private void PlayLevelMusic()
        {
            if (mainThemes == null || mainThemes.Length == 0)
                return;

            int idx;
            if (levelNumber >= 1 && levelNumber <= mainThemes.Length)
                idx = levelNumber - 1;                    // Level 1 -> track 1, etc.
            else
                idx = gameRandom.Next(mainThemes.Length); // Beyond available tracks -> random.

            try
            {
                MediaPlayer.IsRepeating = true;
                MediaPlayer.Volume = 1f;
                MediaPlayer.Play(mainThemes[idx]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing main theme: {ex.Message}");
            }
        }

        protected override void Update(GameTime gameTime)
        {
            // Exit on Back (Android back button arrives as GamePad.Back / Keys.Back).
            KeyboardState keyboardState = Keyboard.GetState();
            GamePadState gamePadState = GamePad.GetState(PlayerIndex.One);
            if (keyboardState.IsKeyDown(Keys.Escape) ||
                keyboardState.IsKeyDown(Keys.Back) ||
                gamePadState.Buttons.Back == ButtonState.Pressed)
            {
                try { Exit(); }
                catch (PlatformNotSupportedException) { /* ignore */ }
            }

            if (!contentLoaded)
            {
                base.Update(gameTime);
                return;
            }

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            input.Update();
            HandleMovement(deltaTime);
            HandleShooting(deltaTime);

            weapon?.UpdateTint(lives, deltaTime);

            goalObject?.Update(gameTime);
            foreach (Enemy enemy in enemies)
                enemy.Update(gameTime);

            UpdateEnemyShooting();
            UpdateBullets(deltaTime);

            banner?.Update(deltaTime);

            if (goalObject != null && goalObject.CheckCollision(player.Position))
            {
                RestartLevel();
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// Right-thumbstick locomotion relative to the head yaw, replacing the
        /// web auto-run. The movement vector is head-forward * stick.Y +
        /// head-right * stick.X scaled by the player speed; wall sliding
        /// (direct move, then X-only, then Z-only) is identical to Game1.
        /// Physical head translation is never a factor - the head pose
        /// translation is discarded by <see cref="VrCameraRig"/>.
        /// </summary>
        private void HandleMovement(float deltaTime)
        {
            Vector2 stick = input.RightStick;
            if (stick == Vector2.Zero)
                return;

            Vector3 forward = cameraRig.HeadForwardXZ;
            Vector3 right = cameraRig.HeadRightXZ;

            Vector3 movement = (forward * stick.Y + right * stick.X) * (player.Speed * deltaTime);
            if (movement.LengthSquared() <= 0f)
                return;

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

        /// <summary>
        /// Right-trigger shooting: trigger edge (XrInput hysteresis) subject to
        /// the shot cooldown. A shot plays S_shot, fires a 0.6-amplitude haptic
        /// pulse on the right controller and performs the hitscan ray from the
        /// weapon muzzle. No visual overlay/muzzle flash (VR requirement).
        /// </summary>
        private void HandleShooting(float deltaTime)
        {
            if (shotTimer > 0f)
            {
                shotTimer -= deltaTime;
                if (shotTimer < 0f)
                    shotTimer = 0f;
            }

            if (!input.RightTriggerPressed)
                return;

            if (shotTimer > 0f)
                return;

            shotTimer = ShotDuration;

            try
            {
                shotSound?.Play(1f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing shot sound: {ex.Message}");
            }

            input.Vibrate(0.6f);

            // Hitscan from the weapon muzzle; when the controller is not
            // tracked, shoot straight ahead from the virtual head instead so
            // the game stays playable.
            if (weapon != null && weapon.Tracked)
                PlayerShoot(weapon.MuzzleOrigin, weapon.MuzzleDirection);
            else
                PlayerShoot(cameraRig.EyeAnchor, cameraRig.HeadForward3D);
        }

        /// <summary>
        /// Instant hitscan shot along an arbitrary direction (generalizes the
        /// web's cardinal-snap PlayerShoot). The ray leaves the muzzle and
        /// steps through the maze cells (sampling DDA in the XZ plane) until a
        /// wall stops it or the first cell containing an enemy is reached.
        /// The enemy in that cell takes 1 damage (random pick if several share
        /// the cell, including the shooter's own cell); the ray stops at the
        /// first hit and never damages more than one enemy.
        /// </summary>
        private void PlayerShoot(Vector3 origin, Vector3 direction)
        {
            Vector2 dir = new Vector2(direction.X, direction.Z);
            if (dir.LengthSquared() < 1e-6f)
                return; // Vertical ray cannot hit anything on the XZ grid.
            dir.Normalize();

            float cellSize = mazeData.CellSize;
            const float stepLength = 0.5f; // Samples per quarter cell - plenty for 2 m cells.
            const float maxDistance = 100f;

            var (px, pz) = mazeData.WorldToGrid(origin);

            // First, an enemy in the player's own cell is hit instantly.
            Enemy target = FindRandomEnemyInCell(px, pz);
            int steps = 0;

            if (target == null)
            {
                for (float d = 0f; d < maxDistance; d += stepLength)
                {
                    Vector3 sample = origin + new Vector3(dir.X, 0f, dir.Y) * d;
                    var (cx, cz) = mazeData.WorldToGrid(sample);

                    if (cx != px || cz != pz)
                    {
                        // A wall (or out of bounds) stops the ray before this cell.
                        if (mazeData.IsWall(cx, cz))
                            return;
                        steps++;
                        px = cx;
                        pz = cz;
                    }

                    target = FindRandomEnemyInCell(cx, cz);
                    if (target != null)
                        break;
                }
            }

            if (target != null)
                DamageEnemy(target, steps);
        }

        protected override void Draw(GameTime gameTime)
        {
            if (!contentLoaded)
            {
                GraphicsDevice.SetRenderTarget(null);
                GraphicsDevice.Clear(Color.CornflowerBlue);
                base.Draw(gameTime);
                return;
            }

            if (xrDevice.DeviceState == XRDeviceState.Enabled)
            {
                // Draw on the XR headset.
                int beginResult = xrDevice.BeginFrame();
                if (beginResult >= 0)
                {
                    try
                    {
                        HeadsetState headset = xrDevice.GetHeadsetState();
                        HandsState hands = xrDevice.GetHandsState();

                        // Rotation-only virtual camera + weapon pose rebasing.
                        cameraRig.Update(headset, player.Position);
                        weapon?.UpdatePose(headset, hands, cameraRig);

                        foreach (XREye eye in xrDevice.GetEyes())
                        {
                            RenderTarget2D rt = xrDevice.GetEyeRenderTarget(eye);
                            GraphicsDevice.SetRenderTarget(rt);
                            GraphicsDevice.Clear(Color.CornflowerBlue);

                            Matrix view = cameraRig.GetEyeView(eye);
                            Matrix projection = xrDevice.CreateProjection(eye, NearPlane, FarPlane);

                            DrawScene(view, projection, cameraRig.GetEyePosition(eye));
                            weapon?.Draw(view, projection);
                            banner?.Draw(view, projection, cameraRig);

                            // Resolve eye rendertarget.
                            GraphicsDevice.SetRenderTarget(null);
                            // Submit eye rendertarget.
                            xrDevice.CommitRenderTarget(eye, rt);
                        }
                    }
                    finally
                    {
                        // Submit XR frame.
                        xrDevice.EndFrame();
                    }

                    return;
                }
            }

            // Fallback: plain 2D window while no XR session is active.
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.CornflowerBlue);
        }

        /// <summary>Draws the maze, goal, projectiles and enemies for one eye.</summary>
        private void DrawScene(Matrix view, Matrix projection, Vector3 eyePosition)
        {
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            mazeRenderer.Draw(view, projection);
            goalObject?.Draw(goalEffect, view, projection);

            // Projectiles (opaque gray cubes) drawn with depth, before the
            // alpha-blended enemies.
            if (bullets.Count > 0)
            {
                GraphicsDevice.BlendState = BlendState.Opaque;
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                foreach (Projectile bullet in bullets)
                    bullet.Draw(bulletEffect, view, projection);
            }

            // Enemies are transparent billboards: alpha-blend so transparent
            // pixels are cut out, and no back-face culling so the textured
            // face is always visible regardless of winding conventions.
            if (enemies.Count > 0)
            {
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;

                // Billboards face the virtual eye of THIS eye's view.
                foreach (Enemy enemy in enemies)
                    enemy.Draw(view, projection, eyePosition);
            }
        }

        /// <summary>
        /// Advances to the next level: a new maze is generated with the texture
        /// set for the new level number. Lives are kept (they only reset on death).
        /// </summary>
        private void RestartLevel()
        {
            levelNumber++;
            ApplyLevelTextures(levelNumber);
            RegenerateMaze();
        }

        /// <summary>
        /// Full reset after death: back to level 1 with the default textures and
        /// a fresh set of lives (the "0 = death" requirement maps to this
        /// existing reset flow).
        /// </summary>
        private void ResetOnDeath()
        {
            levelNumber = 1;
            lives = MaxLives;
            ApplyLevelTextures(levelNumber);
            RegenerateMaze();
        }

        /// <summary>
        /// Generates a fresh maze, repositions the goal/player and respawns
        /// enemies (which also clears active bullets), shows the level banner
        /// and switches the music track.
        /// </summary>
        private void RegenerateMaze()
        {
            int[][] newGrid = MazeGenerator.Generate();
            mazeData.UpdateGrid(newGrid);

            goalObject.UpdatePosition(mazeData.GetFinishPosition());
            mazeRenderer.RebuildGeometry();

            Vector3 startPos = mazeData.GetStartPosition();
            player.Position = new Vector3(startPos.X, VrCameraRig.EyeHeight, startPos.Z);

            SpawnEnemies();
            banner?.Show($"Level {levelNumber}");
            PlayLevelMusic();
        }

        /// <summary>
        /// Applies the maze texture set for the given level number (same rules
        /// as the web game: levels 2-4 cycle their sets; level 5+ overrides the
        /// floor with a random T_random_floor texture).
        /// </summary>
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
            if (level >= 5 && randomFloorTextures.Count > 0)
                floor = randomFloorTextures[gameRandom.Next(randomFloorTextures.Count)];

            mazeRenderer.SetTextures(set.wall, floor, set.ceiling);
        }

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
        /// (Re)spawns the enemies for the current maze (same rules as the web
        /// game): random distinct open cells, count = level + 2, hp = min(level, 5).
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

            float enemySize = 0.8f * mazeData.CellSize;
            int enemyHP = Math.Min(levelNumber, 5);
            int desiredCount = levelNumber + 2;
            int toSpawn = Math.Min(desiredCount, openCells.Count);

            void SpawnAt(int cellIndex)
            {
                var (cx, cz) = openCells[cellIndex];
                Vector3 spawnWorld = mazeData.GridToWorld(cx, cz);
                var (fx, fz) = mazeData.GetStraightPathEnd(cx, cz, gameRandom);
                Vector3 pathEndWorld = mazeData.GridToWorld(fx, fz);
                enemies.Add(new Enemy(GraphicsDevice, enemyTexture, enemyShotTexture, spawnWorld, pathEndWorld, enemySize, enemyHP));
            }

            var used = new HashSet<int>();
            while (enemies.Count < toSpawn)
            {
                int idx = gameRandom.Next(openCells.Count);
                if (used.Add(idx))
                    SpawnAt(idx);
            }
        }

        /// <summary>
        /// Each enemy with line of sight to the player and an elapsed shot
        /// cooldown fires a projectile toward the player's exact world position
        /// (captured at the moment of firing), then enters its shot cooldown
        /// and shows the shot sprite (same as the web game).
        /// </summary>
        private void UpdateEnemyShooting()
        {
            Vector3 playerPos = player.Position;

            foreach (Enemy enemy in enemies)
            {
                if (enemy.IsDead || !enemy.CanShoot)
                    continue;

                var (ex, ez) = mazeData.WorldToGrid(enemy.Position);
                var (px, pz) = mazeData.WorldToGrid(playerPos);
                Vector3 fromCell = mazeData.GridToWorld(ex, ez);
                Vector3 toCell = mazeData.GridToWorld(px, pz);
                if (!mazeData.HasLineOfSight(fromCell, toCell))
                    continue;

                Vector3 target = playerPos;
                enemy.TriggerShotSprite();
                enemy.ResetShootCooldown();

                Vector3 spawn = new Vector3(enemy.Position.X, target.Y, enemy.Position.Z);
                Vector3 dir = new Vector3(target.X - spawn.X, 0f, target.Z - spawn.Z);
                float fireDistance = dir.Length();
                if (fireDistance < 1e-6f)
                    continue;
                dir.Normalize();

                PlayEnemyShotSound(fireDistance / mazeData.CellSize);

                Vector3 despawn = mazeData.GetRayWallHit(spawn, dir);
                float despawnDist = Vector2.Distance(
                    new Vector2(spawn.X, spawn.Z),
                    new Vector2(despawn.X, despawn.Z));

                bullets.Add(new Projectile(GraphicsDevice, spawn, dir, ProjectileSpeed, despawnDist, ProjectileSize));
            }
        }

        /// <summary>
        /// Moves every active projectile, then removes those that hit the player
        /// (playing the hit sound, losing a life) or reached their precomputed
        /// wall despawn point.
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

                float dx = bullet.Position.X - playerPos.X;
                float dz = bullet.Position.Z - playerPos.Z;
                if (dx * dx + dz * dz <= hitRadiusSq)
                {
                    PlayPlayerHitSound();
                    remove = true; // Despawn immediately on hit.

                    lives--;
                    if (lives <= 0)
                    {
                        // Out of lives: full reset to level 1 (player death).
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
        /// Returns a random alive enemy currently occupying the given cell, or
        /// null if none is there (reservoir sampling, same as Game1).
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
                    if (gameRandom.Next(count) == 0)
                        found = enemy;
                }
            }
            return found;
        }

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

        private void PlayPlayerHitSound()
        {
            if (playerHitSound == null)
                return;

            try
            {
                playerHitSound.Play(1f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing player hit sound: {ex.Message}");
            }
        }

        private void PlayEnemyShotSound(float distanceInCells)
        {
            if (enemyShotSound == null)
                return;

            float volumePercent = ShotVolumeFromDistance(distanceInCells);
            if (volumePercent <= 0f)
                return;

            try
            {
                enemyShotSound.Play(volumePercent / 100f, 0f, 0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing enemy shot sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Distance-based enemy shot volume (0..100) per the design spec:
        /// full up to 2 cells, linear fade to 6 cells, silent beyond.
        /// </summary>
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
