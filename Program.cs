using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Zirconium.Main;

namespace Zirconium
{
    internal static class Program
    {
        public static void Main()
        {
            var gws = new GameWindowSettings
            {
                UpdateFrequency = 0.0 // as fast as possible
            };

            var nws = new NativeWindowSettings
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "Zirconium",
                API = ContextAPI.OpenGL,
                APIVersion = new Version(4, 3),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible | ContextFlags.Debug,
                WindowBorder = WindowBorder.Resizable,
                StartVisible = true,
                StartFocused = true,
                NumberOfSamples = 0
            };

            using var win = new Window(gws, nws);
            win.Run();
        }
    }
}
