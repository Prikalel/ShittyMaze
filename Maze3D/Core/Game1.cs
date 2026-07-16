using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        private bool contentLoaded = false;

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
        }

        protected override void Update(GameTime gameTime)
        {
            if (!contentLoaded)
                return;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            HandleMovement(deltaTime);
            HandleMouseRotation();

            camera.Position = player.Position;
            camera.UpdateMatrices();
            goalObject?.Update(gameTime);

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

            base.Draw(gameTime);
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
        }
    }
}
