using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vector2 = System.Numerics.Vector2;

namespace RayTracing
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

        private readonly Dictionary<Keys, ImGuiKey> _keyMap = new();

        public ImGuiController(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;

            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
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

        public void Update(GameWindow window, float deltaSeconds)
        {
            SetPerFrameImGuiData(deltaSeconds);
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
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_vertexBuffer);
            GL.DeleteBuffer(_indexBuffer);
            GL.DeleteVertexArray(_vertexArray);
            GL.DeleteTexture(_fontTexture);
            GL.DeleteProgram(_shader);
            ImGui.DestroyContext();
        }

        private void SetKeyMappings()
        {
            _keyMap[Keys.Tab] = ImGuiKey.Tab;
            _keyMap[Keys.Left] = ImGuiKey.LeftArrow;
            _keyMap[Keys.Right] = ImGuiKey.RightArrow;
            _keyMap[Keys.Up] = ImGuiKey.UpArrow;
            _keyMap[Keys.Down] = ImGuiKey.DownArrow;
            _keyMap[Keys.PageUp] = ImGuiKey.PageUp;
            _keyMap[Keys.PageDown] = ImGuiKey.PageDown;
            _keyMap[Keys.Home] = ImGuiKey.Home;
            _keyMap[Keys.End] = ImGuiKey.End;
            _keyMap[Keys.Delete] = ImGuiKey.Delete;
            _keyMap[Keys.Backspace] = ImGuiKey.Backspace;
            _keyMap[Keys.Enter] = ImGuiKey.Enter;
            _keyMap[Keys.Escape] = ImGuiKey.Escape;
            _keyMap[Keys.Space] = ImGuiKey.Space;
            _keyMap[Keys.A] = ImGuiKey.A;
            _keyMap[Keys.C] = ImGuiKey.C;
            _keyMap[Keys.V] = ImGuiKey.V;
            _keyMap[Keys.X] = ImGuiKey.X;
            _keyMap[Keys.Y] = ImGuiKey.Y;
            _keyMap[Keys.Z] = ImGuiKey.Z;
        }

        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_windowWidth, _windowHeight);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : 1f / 60f;
        }

        private void UpdateInput(GameWindow window)
        {
            var io = ImGui.GetIO();
            var mouse = window.MouseState;
            var keyboard = window.KeyboardState;

            io.AddMousePosEvent(mouse.X, mouse.Y);
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

        private void CreateDeviceResources()
        {
            _vertexBufferSize = 10000;
            _indexBufferSize = 2000;

            _vertexArray = GL.GenVertexArray();
            _vertexBuffer = GL.GenBuffer();
            _indexBuffer = GL.GenBuffer();

            GL.BindVertexArray(_vertexArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.StreamDraw);

            CreateShader();
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

        private void CreateFontTexture()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

            _fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            io.Fonts.SetTexID((IntPtr)_fontTexture);
            io.Fonts.ClearTexData();
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

            drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

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
                -1.0f,
                1.0f);
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

            int vtxOffset = 0;
            int idxOffset = 0;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdListsRange[n];
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

                    var clip = pcmd.ClipRect;
                    GL.Scissor((int)clip.X, (int)(fbHeight - clip.W), (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));

                    GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                    GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)pcmd.ElemCount,
                        DrawElementsType.UnsignedShort, (IntPtr)((idxOffset + pcmd.IdxOffset) * sizeof(ushort)),
                        (int)(vtxOffset + pcmd.VtxOffset));
                }

                vtxOffset += cmdList.VtxBuffer.Size;
                idxOffset += cmdList.IdxBuffer.Size;
            }

            GL.Disable(EnableCap.ScissorTest);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }
}
