using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace RayTracing
{
    public sealed class Window : GameWindow
    {
        public Engine Engine { get; private set; } = null!;
        public bool MouseCaptured { get; private set; } = true;

        // Better defaults + less boilerplate in Program.Main
        public Window(GameWindowSettings gws, NativeWindowSettings nws)
            : base(gws, nws)
        {
            // Smooth resize behaviour (optional)
            VSync = VSyncMode.Off; // path tracing usually wants max throughput
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            // Helpful GL info (one-time)
            Console.WriteLine($"GL Vendor  : {GL.GetString(StringName.Vendor)}");
            Console.WriteLine($"GL Renderer: {GL.GetString(StringName.Renderer)}");
            Console.WriteLine($"GL Version : {GL.GetString(StringName.Version)}");

            Engine = new Engine(this);
            Engine.Init();

            CaptureMouse(true);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            if (Engine != null)
                Engine.Resize(Math.Max(1, Size.X), Math.Max(1, Size.Y));

            // Reset accumulation on resize is usually desired; let Engine decide.
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            // Fixed: Escape toggles capture ONLY, doesn't close
            if (KeyboardState.IsKeyPressed(Keys.Escape))
                CaptureMouse(!MouseCaptured);

            // Close window (Ctrl+W or Alt+F4 still works via OS)
            if (KeyboardState.IsKeyPressed(Keys.Q) && KeyboardState.IsKeyDown(Keys.LeftControl))
                Close();

            // Fullscreen toggle (real fullscreen), not just border
            if (KeyboardState.IsKeyPressed(Keys.F11))
                ToggleFullscreen();

            // Optional: pause updates when unfocused to avoid weird input deltas
            if (!IsFocused) return;

            Engine.Update();
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            // If unfocused you might still want to render; keep it on for now.
            Engine.PumpMainThreadActions();
            Engine.Render();
            SwapBuffers();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            // Click to recapture
            if (!MouseCaptured && e.Button == MouseButton.Left)
                CaptureMouse(true);
        }

        protected override void OnFocusedChanged(FocusedChangedEventArgs e)
        {
            base.OnFocusedChanged(e);

            // When alt-tab out, release mouse
            if (!IsFocused && MouseCaptured)
                CaptureMouse(false);
        }

        // -------- Helpers --------

        private void CaptureMouse(bool capture)
        {
            MouseCaptured = capture;
            CursorState = capture ? CursorState.Grabbed : CursorState.Normal;

            // In grab mode, OpenTK keeps cursor centered; delta is still valid.
        }

        // True fullscreen toggle (stores/restores previous windowed state)
        private Vector2i _windowedSize;
        private Vector2i _windowedPos;
        private WindowState _windowedState = WindowState.Normal;
        private bool _haveWindowedSnapshot;

        private void ToggleFullscreen()
        {
            if (WindowState == WindowState.Fullscreen)
            {
                WindowState = _windowedState;

                if (_haveWindowedSnapshot)
                {
                    Size = _windowedSize;
                    MousePosition = _windowedPos;
                }
                else
                {
                    WindowState = WindowState.Normal;
                }

                // Optional: bring border back
                WindowBorder = WindowBorder.Resizable;
            }
            else
            {
                // Snapshot windowed state once
                if (WindowState != WindowState.Fullscreen)
                {
                    _windowedState = WindowState;
                    _windowedSize = Size;
                    _windowedPos = (Vector2i)MousePosition;
                    _haveWindowedSnapshot = true;
                }

                // Optional: hide border for nicer fullscreen transition
                WindowBorder = WindowBorder.Hidden;
                WindowState = WindowState.Fullscreen;
            }

            // Fullscreen toggle should reset accumulation (camera jitter + resize)
            // We can't call Engine.ResetAccumulation() unless you expose it.
            // But resizing usually triggers it anyway; if not, press your reset key.
        }

        protected override void OnUnload()
        {
            Engine?.Cleanup();
            base.OnUnload();
        }
    }
}
