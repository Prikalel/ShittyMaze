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
    /// Main game class coordinating all subsystems for the3D maze game.
    /// </summary>
    public class Game1 : Game
    {
        private readonly GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;

        private Camera camera;
        private Player player;
        private MazeData mazeData;
        private MazeRenderer mazeRenderer;
        private CollisionDetector collisionDetector;

        private MouseState previousMouseState;
        private bool isMouseCentered = false;
        private Point windowCenter;

        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = false;
        }

        protected override void Initialize()
        {
            // Set window dimensions
            graphics.PreferredBackBufferWidth = 1280;
            graphics.PreferredBackBufferHeight = 720;
            graphics.ApplyChanges();

            Window.Title = "3D First-Person Maze Game";

            // Create maze data
            mazeData = new MazeData();

            // Create collision detector
            collisionDetector = new CollisionDetector(mazeData);

            // Create player at starting position (find first open cell)
            var startPos = mazeData.GetStartPosition();
            player = new Player(new Vector3(startPos.X, 0.7f, startPos.Z));

            // Create camera
            camera = new Camera(player.Position);

            // Create maze renderer
            mazeRenderer = new MazeRenderer(GraphicsDevice, mazeData);

            // Initialize mouse state
            windowCenter = new Point(graphics.PreferredBackBufferWidth / 2, graphics.PreferredBackBufferHeight / 2);
            Mouse.SetPosition(windowCenter.X, windowCenter.Y);
            previousMouseState = Mouse.GetState();
            isMouseCentered = true;

            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            mazeRenderer.LoadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            // Handle exit
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // Get delta time
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Handle mouse look
            HandleMouseLook();

            // Handle movement input
            HandleMovement(deltaTime);

            // Sync camera position with player
            camera.Position = player.Position;

            // Update camera matrices
            camera.UpdateMatrices();

            base.Update(gameTime);
        }

        private void HandleMouseLook()
        {
            if (!isMouseCentered)
            {
                Mouse.SetPosition(windowCenter.X, windowCenter.Y);
                previousMouseState = Mouse.GetState();
                isMouseCentered = true;
                return;
            }

            MouseState currentMouseState = Mouse.GetState();

            // Calculate mouse delta from center
            float deltaX = currentMouseState.X - windowCenter.X;
            float deltaY = currentMouseState.Y - windowCenter.Y;

            // Apply mouse look to camera
            camera.ApplyMouseLook(deltaX, deltaY);

            // Reset mouse to center
            Mouse.SetPosition(windowCenter.X, windowCenter.Y);
            previousMouseState = currentMouseState;
        }

        private void HandleMovement(float deltaTime)
        {
            var keyboardState = Keyboard.GetState();

            // Get movement direction from camera
            Vector3 forward = camera.GetForwardDirection();
            Vector3 right = camera.GetRightDirection();

            // Build movement vector
            Vector3 movement = Vector3.Zero;

            if (keyboardState.IsKeyDown(Keys.W))
                movement += forward;
            if (keyboardState.IsKeyDown(Keys.S))
                movement -= forward;
            if (keyboardState.IsKeyDown(Keys.D))
                movement += right;
            if (keyboardState.IsKeyDown(Keys.A))
                movement -= right;

            // Normalize to prevent faster diagonal movement
            if (movement.LengthSquared() > 0)
            {
                movement.Normalize();
                movement *= player.Speed * deltaTime;

                // Calculate new position
                Vector3 newPosition = player.Position + movement;

                // Check collision
                if (!collisionDetector.CheckCollision(newPosition))
                {
                    player.Position = newPosition;
                }
                else
                {
                    // Try sliding along walls (X and Z separately)
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

            // Enable depth buffer
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            // Set rasterizer state
            RasterizerState rasterizerState = new RasterizerState
            {
                CullMode = CullMode.CullCounterClockwiseFace,
                FillMode = FillMode.Solid
            };
            GraphicsDevice.RasterizerState = rasterizerState;

            // Draw maze
            mazeRenderer.Draw(camera.ViewMatrix, camera.ProjectionMatrix);

            base.Draw(gameTime);
        }

        protected override void OnActivated(object sender, EventArgs args)
        {
            base.OnActivated(sender, args);
            isMouseCentered = false;
        }
    }
}
