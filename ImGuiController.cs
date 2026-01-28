using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vector2 = System.Numerics.Vector2;

namespace Zirconium.UI
{
    internal sealed class ImGuiController : IDisposable
    {
        private int _vertexArray;
        private int _vertexBuffer;
        private int _indexBuffer;
        private int _vertexBufferSize;
        private int _indexBufferSize;

        private int _fontTexture;
        private int _shader;
        private int _attribLocationTex;
        private int _attribLocationProjMtx;

        private int _windowWidth;
        private int _windowHeight;
        private bool _frameBegun;

        private readonly Dictionary<Keys, ImGuiKey> _keyMap = [];

        // Optional: feed text input from your window to this (see method below)
        private readonly Queue<uint> _queuedInput = new();

        public ImGuiController(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;

            ImGui.CreateContext();

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;      // enable if you want docking
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;    // only enable if you also implement a platform backend

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            ImGui.StyleColorsDark();

            SetKeyMappings();
            CreateDeviceResources();

            _frameBegun = false;
        }

        public void WindowResized(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
        }

        /// <summary>
        /// Call this from your GameWindow/TextInput event if you want proper text entry:
        /// window.TextInput += e => controller.AddInputCharacter((uint)e.Unicode);
        /// </summary>
        public void AddInputCharacter(uint codepoint)
        {
            if (codepoint != 0)
                _queuedInput.Enqueue(codepoint);
        }

        public void Update(GameWindow window, float deltaSeconds)
        {
            SetPerFrameImGuiData(window, deltaSeconds);
            UpdateInput(window);

            ImGui.NewFrame();
            _frameBegun = true;
        }

        public void Render()
        {
            if (!_frameBegun)
                return;

            _frameBegun = false;

            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData());

            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
        }

        public void Dispose()
        {
            if (_vertexBuffer != 0) GL.DeleteBuffer(_vertexBuffer);
            if (_indexBuffer != 0) GL.DeleteBuffer(_indexBuffer);
            if (_vertexArray != 0) GL.DeleteVertexArray(_vertexArray);
            if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
            if (_shader != 0) GL.DeleteProgram(_shader);

            ImGui.DestroyContext();
        }

        private void SetKeyMappings()
        {
            // Navigation
            _keyMap[Keys.Tab] = ImGuiKey.Tab;
            _keyMap[Keys.Left] = ImGuiKey.LeftArrow;
            _keyMap[Keys.Right] = ImGuiKey.RightArrow;
            _keyMap[Keys.Up] = ImGuiKey.UpArrow;
            _keyMap[Keys.Down] = ImGuiKey.DownArrow;
            _keyMap[Keys.PageUp] = ImGuiKey.PageUp;
            _keyMap[Keys.PageDown] = ImGuiKey.PageDown;
            _keyMap[Keys.Home] = ImGuiKey.Home;
            _keyMap[Keys.End] = ImGuiKey.End;
            _keyMap[Keys.Insert] = ImGuiKey.Insert;
            _keyMap[Keys.Delete] = ImGuiKey.Delete;
            _keyMap[Keys.Backspace] = ImGuiKey.Backspace;
            _keyMap[Keys.Enter] = ImGuiKey.Enter;
            _keyMap[Keys.Escape] = ImGuiKey.Escape;
            _keyMap[Keys.Space] = ImGuiKey.Space;

            // Letters
            _keyMap[Keys.A] = ImGuiKey.A; _keyMap[Keys.B] = ImGuiKey.B; _keyMap[Keys.C] = ImGuiKey.C;
            _keyMap[Keys.D] = ImGuiKey.D; _keyMap[Keys.E] = ImGuiKey.E; _keyMap[Keys.F] = ImGuiKey.F;
            _keyMap[Keys.G] = ImGuiKey.G; _keyMap[Keys.H] = ImGuiKey.H; _keyMap[Keys.I] = ImGuiKey.I;
            _keyMap[Keys.J] = ImGuiKey.J; _keyMap[Keys.K] = ImGuiKey.K; _keyMap[Keys.L] = ImGuiKey.L;
            _keyMap[Keys.M] = ImGuiKey.M; _keyMap[Keys.N] = ImGuiKey.N; _keyMap[Keys.O] = ImGuiKey.O;
            _keyMap[Keys.P] = ImGuiKey.P; _keyMap[Keys.Q] = ImGuiKey.Q; _keyMap[Keys.R] = ImGuiKey.R;
            _keyMap[Keys.S] = ImGuiKey.S; _keyMap[Keys.T] = ImGuiKey.T; _keyMap[Keys.U] = ImGuiKey.U;
            _keyMap[Keys.V] = ImGuiKey.V; _keyMap[Keys.W] = ImGuiKey.W; _keyMap[Keys.X] = ImGuiKey.X;
            _keyMap[Keys.Y] = ImGuiKey.Y; _keyMap[Keys.Z] = ImGuiKey.Z;

            // Digits
            _keyMap[Keys.D0] = ImGuiKey._0;
            _keyMap[Keys.D1] = ImGuiKey._1;
            _keyMap[Keys.D2] = ImGuiKey._2;
            _keyMap[Keys.D3] = ImGuiKey._3;
            _keyMap[Keys.D4] = ImGuiKey._4;
            _keyMap[Keys.D5] = ImGuiKey._5;
            _keyMap[Keys.D6] = ImGuiKey._6;
            _keyMap[Keys.D7] = ImGuiKey._7;
            _keyMap[Keys.D8] = ImGuiKey._8;
            _keyMap[Keys.D9] = ImGuiKey._9;

            // Function keys
            _keyMap[Keys.F1] = ImGuiKey.F1;   _keyMap[Keys.F2] = ImGuiKey.F2;
            _keyMap[Keys.F3] = ImGuiKey.F3;   _keyMap[Keys.F4] = ImGuiKey.F4;
            _keyMap[Keys.F5] = ImGuiKey.F5;   _keyMap[Keys.F6] = ImGuiKey.F6;
            _keyMap[Keys.F7] = ImGuiKey.F7;   _keyMap[Keys.F8] = ImGuiKey.F8;
            _keyMap[Keys.F9] = ImGuiKey.F9;   _keyMap[Keys.F10] = ImGuiKey.F10;
            _keyMap[Keys.F11] = ImGuiKey.F11; _keyMap[Keys.F12] = ImGuiKey.F12;

            // Punctuation (handy for text boxes / shortcuts)
            _keyMap[Keys.Minus] = ImGuiKey.Minus;
            _keyMap[Keys.Equal] = ImGuiKey.Equal;
            _keyMap[Keys.LeftBracket] = ImGuiKey.LeftBracket;
            _keyMap[Keys.RightBracket] = ImGuiKey.RightBracket;
            _keyMap[Keys.Backslash] = ImGuiKey.Backslash;
            _keyMap[Keys.Semicolon] = ImGuiKey.Semicolon;
            _keyMap[Keys.Apostrophe] = ImGuiKey.Apostrophe;
            _keyMap[Keys.Comma] = ImGuiKey.Comma;
            _keyMap[Keys.Period] = ImGuiKey.Period;
            _keyMap[Keys.Slash] = ImGuiKey.Slash;
            _keyMap[Keys.GraveAccent] = ImGuiKey.GraveAccent;
        }

        private void SetPerFrameImGuiData(GameWindow window, float deltaSeconds)
        {
            var io = ImGui.GetIO();
            var clientSize = window.ClientSize;
            io.DisplaySize = new Vector2(clientSize.X, clientSize.Y);

            // Framebuffer scale (HiDPI)
            var windowSize = window.Size;
            float scaleX = clientSize.X > 0 ? windowSize.X / (float)clientSize.X : 1f;
            float scaleY = clientSize.Y > 0 ? windowSize.Y / (float)clientSize.Y : 1f;
            io.DisplayFramebufferScale = new Vector2(scaleX, scaleY);

            io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : 1f / 60f;
        }

        private void UpdateInput(GameWindow window)
        {
            var io = ImGui.GetIO();
            var mouse = window.MouseState;
            var keyboard = window.KeyboardState;

            // Feed text input (optional, but strongly recommended for actual typing)
            while (_queuedInput.Count > 0)
                io.AddInputCharacter(_queuedInput.Dequeue());

            // Mouse position in ImGui "display" coordinates.
            float scaleX = io.DisplayFramebufferScale.X;
            float scaleY = io.DisplayFramebufferScale.Y;

            float mouseX = (scaleX != 0f) ? (mouse.X / scaleX) : mouse.X;
            float mouseY = (scaleY != 0f) ? (mouse.Y / scaleY) : mouse.Y;
            float mouseYWithOffset = mouseY + 40f;

            if (mouseX < 0f || mouseYWithOffset < 0f || mouseX > window.Size.X || mouseYWithOffset > window.Size.Y)
                io.AddMousePosEvent(-float.MaxValue, -float.MaxValue);
            else
                io.AddMousePosEvent(mouseX, mouseYWithOffset);
            io.AddMouseButtonEvent(0, mouse.IsButtonDown(MouseButton.Left));
            io.AddMouseButtonEvent(1, mouse.IsButtonDown(MouseButton.Right));
            io.AddMouseButtonEvent(2, mouse.IsButtonDown(MouseButton.Middle));
            io.AddMouseWheelEvent(mouse.ScrollDelta.X, mouse.ScrollDelta.Y);

            foreach (var kvp in _keyMap)
                io.AddKeyEvent(kvp.Value, keyboard.IsKeyDown(kvp.Key));

            io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
            io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
            io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftSuper) || keyboard.IsKeyDown(Keys.RightSuper));
        }

        public bool WantsMouseCapture => ImGui.GetIO().WantCaptureMouse;
        public bool MouseInGuiWin() => ImGui.GetIO().WantCaptureMouse;
        public bool FrameBegun => _frameBegun;

        private void CreateDeviceResources()
        {
            _vertexBufferSize = 64 * 1024; // bytes (bigger default)
            _indexBufferSize = 16 * 1024;  // bytes

            _vertexArray = GL.GenVertexArray();
            _vertexBuffer = GL.GenBuffer();
            _indexBuffer = GL.GenBuffer();

            GL.BindVertexArray(_vertexArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.StreamDraw);

            CreateShader();
            ConfigureFonts();
            CreateFontTexture();

            int stride = Marshal.SizeOf<ImDrawVert>();
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, Marshal.OffsetOf<ImDrawVert>("pos"));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, Marshal.OffsetOf<ImDrawVert>("uv"));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, Marshal.OffsetOf<ImDrawVert>("col"));

            GL.BindVertexArray(0);
        }

        private unsafe void ConfigureFonts()
        {
            var io = ImGui.GetIO();

            io.Fonts.Clear();
            io.Fonts.AddFontDefault();

            const string emojiFontPath = @"C:\Windows\Fonts\seguisym.ttf";

            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            config.MergeMode = true;
            config.PixelSnapH = true;

            ushort[] symbolRanges =
            [
                0x2190, 0x21FF, // Arrows
                0x2300, 0x23FF, // Misc technical
                0x2500, 0x257F, // Box drawing
                0x2580, 0x259F, // Block elements
                0x25A0, 0x25FF, // Geometric shapes
                0x2600, 0x26FF, // Misc symbols
                0x2700, 0x27BF, // Dingbats
                0
            ];

            GCHandle rangesHandle = GCHandle.Alloc(symbolRanges, GCHandleType.Pinned);
            try
            {
                io.Fonts.AddFontFromFileTTF(emojiFontPath, 16.0f, config, rangesHandle.AddrOfPinnedObject());
                io.Fonts.Build();
            }
            finally
            {
                rangesHandle.Free();
            }

            ImGuiNative.ImFontConfig_destroy(config);
        }

        private void CreateFontTexture()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

            int prevTex;
            GL.GetInteger(GetPName.TextureBinding2D, out prevTex);

            int prevUnpack;
            GL.GetInteger(GetPName.UnpackAlignment, out prevUnpack);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            _fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            io.Fonts.SetTexID(_fontTexture);
            io.Fonts.ClearTexData();

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, prevUnpack);
            GL.BindTexture(TextureTarget.Texture2D, prevTex);
        }

        private void CreateShader()
        {
            const string vertexSource = @"#version 330 core
layout (location = 0) in vec2 in_position;
layout (location = 1) in vec2 in_texCoord;
layout (location = 2) in vec4 in_color;
uniform mat4 projection_matrix;
out vec2 frag_texCoord;
out vec4 frag_color;
void main()
{
    gl_Position = projection_matrix * vec4(in_position, 0, 1);
    frag_texCoord = in_texCoord;
    frag_color = in_color;
}";

            const string fragmentSource = @"#version 330 core
in vec2 frag_texCoord;
in vec4 frag_color;
uniform sampler2D in_texture;
out vec4 out_color;
void main()
{
    out_color = frag_color * texture(in_texture, frag_texCoord);
}";

            int vertex = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertex, vertexSource);
            GL.CompileShader(vertex);
            GL.GetShader(vertex, ShaderParameter.CompileStatus, out int vStatus);
            if (vStatus != (int)All.True)
                throw new Exception(GL.GetShaderInfoLog(vertex));

            int fragment = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragment, fragmentSource);
            GL.CompileShader(fragment);
            GL.GetShader(fragment, ShaderParameter.CompileStatus, out int fStatus);
            if (fStatus != (int)All.True)
                throw new Exception(GL.GetShaderInfoLog(fragment));

            _shader = GL.CreateProgram();
            GL.AttachShader(_shader, vertex);
            GL.AttachShader(_shader, fragment);
            GL.LinkProgram(_shader);
            GL.GetProgram(_shader, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus != (int)All.True)
                throw new Exception(GL.GetProgramInfoLog(_shader));

            GL.DetachShader(_shader, vertex);
            GL.DetachShader(_shader, fragment);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);

            _attribLocationTex = GL.GetUniformLocation(_shader, "in_texture");
            _attribLocationProjMtx = GL.GetUniformLocation(_shader, "projection_matrix");
        }

        private void RenderImDrawData(ImDrawDataPtr drawData)
        {
            int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
            int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
            if (fbWidth <= 0 || fbHeight <= 0)
                return;

            // --- Backup GL state (so ImGui doesn't break your renderer) ---
            int lastProgram, lastTexture, lastArrayBuffer, lastElementArrayBuffer, lastVertexArray;
            int lastBlendSrcRgb, lastBlendDstRgb, lastBlendSrcAlpha, lastBlendDstAlpha;
            int lastBlendEqRgb, lastBlendEqAlpha;
            int[] lastViewport = new int[4];
            int[] lastScissor = new int[4];

            bool lastBlend = GL.IsEnabled(EnableCap.Blend);
            bool lastCull = GL.IsEnabled(EnableCap.CullFace);
            bool lastDepth = GL.IsEnabled(EnableCap.DepthTest);
            bool lastScissorTest = GL.IsEnabled(EnableCap.ScissorTest);

            GL.GetInteger(GetPName.CurrentProgram, out lastProgram);
            GL.GetInteger(GetPName.TextureBinding2D, out lastTexture);
            GL.GetInteger(GetPName.ArrayBufferBinding, out lastArrayBuffer);
            GL.GetInteger(GetPName.ElementArrayBufferBinding, out lastElementArrayBuffer);
            GL.GetInteger(GetPName.VertexArrayBinding, out lastVertexArray);

            GL.GetInteger(GetPName.BlendSrcRgb, out lastBlendSrcRgb);
            GL.GetInteger(GetPName.BlendDstRgb, out lastBlendDstRgb);
            GL.GetInteger(GetPName.BlendSrcAlpha, out lastBlendSrcAlpha);
            GL.GetInteger(GetPName.BlendDstAlpha, out lastBlendDstAlpha);
            GL.GetInteger(GetPName.BlendEquationRgb, out lastBlendEqRgb);
            GL.GetInteger(GetPName.BlendEquationAlpha, out lastBlendEqAlpha);

            GL.GetInteger(GetPName.Viewport, lastViewport);
            GL.GetInteger(GetPName.ScissorBox, lastScissor);

            // --- Setup state for ImGui ---
            GL.Viewport(0, 0, fbWidth, fbHeight);
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);

            GL.UseProgram(_shader);
            GL.Uniform1(_attribLocationTex, 0);

            Matrix4 proj = Matrix4.CreateOrthographicOffCenter(
                drawData.DisplayPos.X,
                drawData.DisplayPos.X + drawData.DisplaySize.X,
                drawData.DisplayPos.Y + drawData.DisplaySize.Y,
                drawData.DisplayPos.Y,
                -1.0f, 1.0f);

            GL.UniformMatrix4(_attribLocationProjMtx, false, ref proj);

            GL.BindVertexArray(_vertexArray);

            int vertexSize = Marshal.SizeOf<ImDrawVert>();
            int totalVtxSize = drawData.TotalVtxCount * vertexSize;
            if (totalVtxSize > _vertexBufferSize)
            {
                _vertexBufferSize = (int)(totalVtxSize * 1.5f);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.StreamDraw);
            }

            int totalIdxSize = drawData.TotalIdxCount * sizeof(ushort);
            if (totalIdxSize > _indexBufferSize)
            {
                _indexBufferSize = (int)(totalIdxSize * 1.5f);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.StreamDraw);
            }

            // Clip rect math (correct for DisplayPos + FramebufferScale)
            Vector2 clipOff = new Vector2(drawData.DisplayPos.X, drawData.DisplayPos.Y);
            Vector2 clipScale = new Vector2(drawData.FramebufferScale.X, drawData.FramebufferScale.Y);

            int vtxOffset = 0;
            int idxOffset = 0;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];
                int vtxBufferSize = cmdList.VtxBuffer.Size * vertexSize;
                int idxBufferSize = cmdList.IdxBuffer.Size * sizeof(ushort);

                GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                GL.BufferSubData(BufferTarget.ArrayBuffer, (IntPtr)(vtxOffset * vertexSize), vtxBufferSize, cmdList.VtxBuffer.Data);

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferSubData(BufferTarget.ElementArrayBuffer, (IntPtr)(idxOffset * sizeof(ushort)), idxBufferSize, cmdList.IdxBuffer.Data);

                for (int cmd_i = 0; cmd_i < cmdList.CmdBuffer.Size; cmd_i++)
                {
                    var pcmd = cmdList.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != IntPtr.Zero)
                        continue;

                    // Transform to framebuffer space
                    var cr = pcmd.ClipRect;
                    float clipX1 = (cr.X - clipOff.X) * clipScale.X;
                    float clipY1 = (cr.Y - clipOff.Y) * clipScale.Y;
                    float clipX2 = (cr.Z - clipOff.X) * clipScale.X;
                    float clipY2 = (cr.W - clipOff.Y) * clipScale.Y;

                    if (clipX1 < fbWidth && clipY1 < fbHeight && clipX2 >= 0.0f && clipY2 >= 0.0f)
                    {
                        GL.Scissor(
                            (int)clipX1,
                            (int)(fbHeight - clipY2),
                            (int)(clipX2 - clipX1),
                            (int)(clipY2 - clipY1));

                        GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);

                        GL.DrawElementsBaseVertex(
                            PrimitiveType.Triangles,
                            (int)pcmd.ElemCount,
                            DrawElementsType.UnsignedShort,
                            (IntPtr)((idxOffset + pcmd.IdxOffset) * sizeof(ushort)),
                            (int)(vtxOffset + pcmd.VtxOffset));
                    }
                }

                vtxOffset += cmdList.VtxBuffer.Size;
                idxOffset += cmdList.IdxBuffer.Size;
            }

            // --- Restore GL state ---
            if (!lastBlend) GL.Disable(EnableCap.Blend); else GL.Enable(EnableCap.Blend);
            if (lastCull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            if (lastDepth) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (lastScissorTest) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);

            GL.BlendEquationSeparate((BlendEquationMode)lastBlendEqRgb, (BlendEquationMode)lastBlendEqAlpha);
            GL.BlendFuncSeparate((BlendingFactorSrc)lastBlendSrcRgb, (BlendingFactorDest)lastBlendDstRgb,
                                 (BlendingFactorSrc)lastBlendSrcAlpha, (BlendingFactorDest)lastBlendDstAlpha);

            GL.UseProgram(lastProgram);
            GL.BindTexture(TextureTarget.Texture2D, lastTexture);
            GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, lastElementArrayBuffer);
            GL.BindVertexArray(lastVertexArray);

            GL.Viewport(lastViewport[0], lastViewport[1], lastViewport[2], lastViewport[3]);
            GL.Scissor(lastScissor[0], lastScissor[1], lastScissor[2], lastScissor[3]);
        }
    }
}
