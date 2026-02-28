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
        /// Accumulated mouse movement delta Y (vertical) to be applied to pitch.
        /// </summary>
        private float mouseDeltaY;

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
            if (mouseDeltaX != 0f || mouseDeltaY != 0f)
            {
                float deltaYaw = -mouseDeltaX * MouseSensitivity;
                float deltaPitch = -mouseDeltaY * MouseSensitivity;

                camera.ApplyRotation(deltaYaw, deltaPitch);

                // Reset deltas after applying
                mouseDeltaX = 0f;
                mouseDeltaY = 0f;
            }
        }

        private void HandleMovement(float deltaTime)
        {
            var keyboardState = Keyboard.GetState();

            Vector3 forward = camera.GetForwardDirection();
            Vector3 right = camera.GetRightDirection();
            Vector3 movement = Vector3.Zero;

            if (keyboardState.IsKeyDown(Keys.W))
                movement += forward;
            if (keyboardState.IsKeyDown(Keys.S))
                movement -= forward;
            if (keyboardState.IsKeyDown(Keys.D))
                movement += right;
            if (keyboardState.IsKeyDown(Keys.A))
                movement -= right;

            if (movement.LengthSquared() > 0)
            {
                movement.Normalize();
                movement *= player.Speed * deltaTime;

                Vector3 newPosition = player.Position + movement;

                if (!collisionDetector.CheckCollision(newPosition))
                {
                    player.Position = newPosition;
                }
                else
                {
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

            camera.Position = player.Position;
            camera.Reset();
        }
    }
}
