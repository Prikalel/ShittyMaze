using System;
using Maze3D.Core;

namespace Maze3D
{
    /// <summary>
    /// Entry point for the3D First-Person Maze Game.
    /// </summary>
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var game = new Game1())
            {
                game.Run();
            }
        }
    }
}
