using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Maze3D.Core;

namespace ShittyMaze.Web.Pages
{
    public partial class Index
    {
        Game1 _game;
        private static string _origin;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            if (firstRender)
            {
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
            }
        }

        [JSInvokable]
        public static void SetOrigin(string origin)
        {
            _origin = origin;
        }

        [JSInvokable]
        public void TickDotNet()
        {
            // init game
            if (_game == null)
            {
                _game = new Game1(_origin);
                _game.Run();
            }
            // run gameloop
            _game.Tick();
        }

        /// <summary>
        /// Receives mouse movement delta from JavaScript when pointer is locked.
        /// </summary>
        /// <param name="deltaX">Horizontal mouse movement in pixels.</param>
        /// <param name="deltaY">Vertical mouse movement in pixels.</param>
        [JSInvokable]
        public void OnMouseMove(float deltaX, float deltaY)
        {
            _game?.ApplyMouseDelta(deltaX, deltaY);
        }
    }
}
