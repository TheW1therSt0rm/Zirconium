using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;

namespace RayTracing
{
    public sealed class Engine
    {
        private readonly Window _win;

        private Shader _rt = null!;
        private Shader _tonemap = null!;
        private ImGuiController? _imgui;
        private float _imguiDelta;

        // HDR accumulation ping-pong
        private int _accumA, _accumB;   // rgba16f
        private int _readTex, _writeTex;

        // Tonemapped output
        private int _ldrTex;            // rgba8
        private int _ldrFbo;            // attach _ldrTex, then BlitFramebuffer -> screen

        // FBO used only for clearing textures
        private int _clearFbo;

        // Scene buffers
        private int _spheresSSbo;        // std430 binding=0
        private int _cubesSSbo;        // std430 binding=0

        private int _triSsbo;           // std430 binding=1
        private int _bvhSsbo;           // std430 binding=2

        private int _numSpheres;
        private int _numCubes;
        private int _numTris;
        private int _numBvhNodes;

        private float oldTime = 0f;
        private Stopwatch clock = new();

        // CPU-side accumulated triangle list so multiple UploadMesh() calls append.
        private readonly List<TriangleGPU> _allTrisGpu = new();

        // progressive accumulation
        private int _frame;
        private bool _needsReset = true;
        private bool _accumalation = true;

        // camera
        private Vector3 _camPos = new(0f, 1.2f, -5.5f);
        private System.Numerics.Vector3 camPos = new(0f, 1.2f, -5.5f);
        private float _yaw = 79.2f;
        private float _pitch = 9.6f;
        private Vector3 _camForward;
        private Vector3 _camRight;
        private Vector3 _camUp;
        private float _lastYaw;
        private float _lastPitch;
        private bool _basisDirty = true;

        private float _lastTitleUpdate;
        private const float TitleUpdateInterval = 0.25f;

        private Vector3[] _bvhVerts = Array.Empty<Vector3>();
        private int[] _bvhIndices = Array.Empty<int>();

        private static readonly Vector3 SunDir = new Vector3(0.6f, 0.6f, 1.0f).Normalized();
        private static readonly Vector3 SunColor = new(0.76862745f, 0.69019608f, 0.16862745f);
        private const float SunIntensity = 30.0f;
        private const float SunAngularRad = 0.00465f;

        private static readonly int TriangleGpuSize = Marshal.SizeOf<TriangleGPU>();
        private static readonly int BvhNodeGpuSize = Marshal.SizeOf<BVHNodeGPU>();

        private List<Thread> threads = [];

        private const float MoveSpeed = 6f;
        private const float MouseSens = 0.12f;
        private const float FovDeg = 60f;
        private const int TargetRenderHeight = 180;

        private int _renderWidth;
        private int _renderHeight;
        private int _renderScale = 1;

        private readonly int _mainThreadId;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        public Engine(Window win)
        {
            _win = win;
            _mainThreadId = Environment.CurrentManagedThreadId;
            ThreadStart threadStarter1 = new(Init);
            threads.Add(new Thread(threadStarter1));
            ThreadStart threadStarter2 = new(Update);
            threads.Add(new Thread(threadStarter2));
            ThreadStart threadStarter3 = new(Render);
            threads.Add(new Thread(threadStarter3));
            ThreadStart threadStarter4 = new(ClearMeshes);
            threads.Add(new Thread(threadStarter4));
            ThreadStart threadStarter5 = new(Cleanup);
            threads.Add(new Thread(threadStarter5));
            ThreadStart threadStarter6 = new(UploadAllMeshesToGpu);
            threads.Add(new Thread(threadStarter6));
            ThreadStart threadStarter7 = new(UploadSpheres);
            threads.Add(new Thread(threadStarter7));
            ThreadStart threadStarter8 = new(UploadCubes);
            threads.Add(new Thread(threadStarter8));
        }

        public void EnqueueMainThread(Action action)
        {
            if (action == null) return;
            _mainThreadActions.Enqueue(action);
        }

        private bool IsMainThread => Environment.CurrentManagedThreadId == _mainThreadId;
        public bool IsImGuiMouseCaptured => _imgui?.WantsMouseCapture ?? false;

        public void PumpMainThreadActions(int maxActions = 64)
        {
            // Drain up to maxActions to avoid long stalls in one frame
            int count = 0;
            while (count < maxActions && _mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // Log or handle; don't kill the render loop.
                    Console.WriteLine(ex);
                }
                count++;
            }
        }

        public void Init()
        {
            _imgui = new ImGuiController(_win.Size.X, _win.Size.Y);

            ClearMeshes();
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.FramebufferSrgb);

            _ldrFbo = GL.GenFramebuffer();
            _clearFbo = GL.GenFramebuffer();

            _rt = new Shader("shader", Shader.Kind.Compute);
            _tonemap = new Shader("app", Shader.Kind.Compute);

            _rt.Use();
            _rt.Set("uSunDir", SunDir);
            _rt.Set("uSunColor", SunColor);
            _rt.Set("uSunIntensity", SunIntensity);
            _rt.Set("uSunAngularRad", SunAngularRad);

            _tonemap.Use();
            _tonemap.Set("uTex", 0); // sampler2D reads from texture unit 0

            // ---- SSBO: spheres & cubes (binding= 0 & 1) ----
            _spheresSSbo = GL.GenBuffer();
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, _spheresSSbo);
            _cubesSSbo = GL.GenBuffer();
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _cubesSSbo);

            // ---- SSBOs (bindings must match shader) ----
            _triSsbo = GL.GenBuffer();
            _bvhSsbo = GL.GenBuffer();
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, _triSsbo);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, _bvhSsbo);

            // Scene: spheres + cubes + demo meshes
            UploadSpheres();
            UploadCubes();

            ContstructMeshAsync("Pawn.obj", new(2f, 3f, 5f), Vector3.One, Vector3.Zero, new(0.5f, 0.25f, 0.6f), 0.25f, Vector3.Zero, 0f);
            ContstructMeshAsync("Monkey.obj", new(-3f, 2f, 10f), Vector3.One, Vector3.Zero, new(0.25f, 0.6f, 0.5f), 0.9f, Vector3.Zero, 0f);
            ContstructMeshAsync("Dragon.obj", new(2f, 6f, 10f), Vector3.One * 2f, Vector3.Zero, new(0.6f, 0.5f, 0.25f), 1.0f, Vector3.Zero, 0f);

            Resize(_win.Size.X, _win.Size.Y);
            _needsReset = true;
            clock.Start();
        }

        public void Resize(int w, int h)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            GL.Viewport(0, 0, w, h);
            _imgui?.WindowResized(w, h);

            DeleteTex(ref _accumA);
            DeleteTex(ref _accumB);
            DeleteTex(ref _ldrTex);

            _renderScale = ChooseIntegerScale(h, TargetRenderHeight);

            _renderWidth = Math.Max(1, w / _renderScale);
            _renderHeight = Math.Max(1, h / _renderScale);

            _accumA = CreateTexRgba16f(_renderWidth, _renderHeight);
            _accumB = CreateTexRgba16f(_renderWidth, _renderHeight);
            _ldrTex = CreateTexRgba8(w, h);

            _readTex = _accumA;
            _writeTex = _accumB;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ldrFbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                _ldrTex, 0);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new Exception("LDR FBO incomplete: " + status);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            ResetAccumulation();
        }

        public void Update()
        {
            float now = (float)clock.Elapsed.TotalSeconds;
            float dt = now - oldTime;
            oldTime = now;
            _imguiDelta = dt;

            if (_win.KeyboardState.IsKeyDown(Keys.F11))
                _needsReset = true;

            if (!_win.IsFocused) return;

            if (_win.CursorState == CursorState.Grabbed)
            {
                var delta = _win.MouseState.Delta;
                if (delta.LengthSquared > 0)
                {
                    _yaw += delta.X * MouseSens;
                    _pitch -= delta.Y * MouseSens;
                    _pitch = Math.Clamp(_pitch, -89.9f, 89.9f);
                    _needsReset = true;
                    _basisDirty = true;
                }
            }
            if (!_accumalation)
            {
                _needsReset = true;
            }

            UpdateBasisIfNeeded();
            var fwd = _camForward;
            var right = _camRight;
            var up = _camUp;

            Vector3 wish = Vector3.Zero;
            var kb = _win.KeyboardState;

            if (kb.IsKeyDown(Keys.W)) wish += fwd;
            if (kb.IsKeyDown(Keys.S)) wish -= fwd;
            if (kb.IsKeyDown(Keys.D)) wish += right;
            if (kb.IsKeyDown(Keys.A)) wish -= right;
            if (kb.IsKeyDown(Keys.E)) wish += up;
            if (kb.IsKeyDown(Keys.Q)) wish -= up;

            float speed = MoveSpeed * (kb.IsKeyDown(Keys.LeftShift) ? 3f : 1f);

            if (wish.LengthSquared > 0)
            {
                _camPos += wish.Normalized() * speed * dt;
                _needsReset = true;
            }

            if (now - _lastTitleUpdate >= TitleUpdateInterval)
            {
                _win.Title = $"RayTracing | FPS: {(dt > 0 ? (1f / dt) : 9999f):0.0}";
                _lastTitleUpdate = now;
            }
        }

        public void Render()
        {
            if (_needsReset) ResetAccumulation();

            int w = _renderWidth;
            int h = _renderHeight;
            int outW = _win.Size.X;
            int outH = _win.Size.Y;

            UpdateBasisIfNeeded();
            var fwd = _camForward;
            var right = _camRight;
            var up = _camUp;

            float aspect = w / (float)h;
            float halfH = MathF.Tan(MathHelper.DegreesToRadians(FovDeg) * 0.5f);
            float halfW = halfH * aspect;

            // PASS 1: path trace compute -> _writeTex (RGBA16F)
            _rt.Use();
            // Depth of field (start small)
            float aperture = 0.0f; // start at 0 (no blur), then try 0.01..0.05 depending on your world scale

            // Focus distance: distance from camera to the thing you want sharp
            Vector3 focusTarget = new(2f, 6f, 10f); // your cube position (example)
            float focusDist = (focusTarget - _camPos).Length;

            _rt.Set("uAperture", aperture);
            _rt.Set("uFocusDist", focusDist);
            _rt.Set("uFrame", _frame);
            _rt.Set("uSize", new Vector2i(w, h));
            _rt.Set("camForward", fwd);
            _rt.Set("camRight", right);
            _rt.Set("camUp", up);
            _rt.Set("viewParams", new Vector3(halfW, halfH, 10.0f));
            _rt.Set("camPos", _camPos);
            _rt.Set("uNumSpheres", _numSpheres);
            _rt.Set("uNumCubes", _numCubes);
            _rt.Set("uNumTris", _numTris);
            _rt.Set("uNumBvhNodes", _numBvhNodes);

            GL.BindImageTexture(0, _readTex, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba16f);
            GL.BindImageTexture(1, _writeTex, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

            GL.DispatchCompute((w + 7) / 8, (h + 7) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            // PASS 2: tonemap compute -> _ldrTex (RGBA8)
            _tonemap.Use();
            _tonemap.Set("uSize", new Vector2i(w, h));
            _tonemap.Set("uWinSize", new Vector2i(outW, outH));

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _writeTex);

            GL.BindImageTexture(1, _ldrTex, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

            GL.DispatchCompute((outW + 7) / 8, (outH + 7) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.FramebufferBarrierBit);

            // PASS 3: blit _ldrTex -> default framebuffer
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _ldrFbo);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GL.BlitFramebuffer(
                0, 0, outW, outH,
                0, 0, outW, outH,
                ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Nearest
            );

            if (_imgui != null)
            {
                _imgui.Update(_win, _imguiDelta);

                ImGui.Begin("Render");
                ImGui.Text($"Frame: {_frame}");
                ImGui.Checkbox("Accumulation", ref _accumalation);
                ImGui.End();
                
                ImGui.Begin("Camera Position");
                ImGui.InputFloat("X", ref _camPos.X);
                ImGui.InputFloat("Y", ref _camPos.Y);
                ImGui.InputFloat("Z", ref _camPos.Z);
                ImGui.End();

                _imgui.Render();
            }

            (_readTex, _writeTex) = (_writeTex, _readTex);
            _frame++;
        }

        public void Cleanup()
        {
            _imgui?.Dispose();
            _imgui = null;
            _rt?.Dispose();
            _tonemap?.Dispose();

            DeleteTex(ref _accumA);
            DeleteTex(ref _accumB);
            DeleteTex(ref _ldrTex);

            if (_ldrFbo != 0) { GL.DeleteFramebuffer(_ldrFbo); _ldrFbo = 0; }
            if (_clearFbo != 0) { GL.DeleteFramebuffer(_clearFbo); _clearFbo = 0; }

            if (_spheresSSbo != 0) { GL.DeleteBuffer(_spheresSSbo); _spheresSSbo = 0; }
            if (_triSsbo != 0) { GL.DeleteBuffer(_triSsbo); _triSsbo = 0; }
            if (_bvhSsbo != 0) { GL.DeleteBuffer(_bvhSsbo); _bvhSsbo = 0; }
        }

        // =========================================================
        // Mesh upload (append) + BVH build via BVH.cs
        // =========================================================
        public void UploadMesh(TriangleGPU[] triGpu)
        {
            if (!IsMainThread)
            {
                EnqueueMainThread(() => UploadMesh(triGpu));
                return;
            }

            if (triGpu == null || triGpu.Length == 0)
                return;

            _allTrisGpu.AddRange(triGpu);
            UploadAllMeshesToGpu();
        }

        public void ClearMeshes()
        {
            if (!IsMainThread)
            {
                EnqueueMainThread(ClearMeshes);
                return;
            }

            _allTrisGpu.Clear();
            UploadAllMeshesToGpu();
        }

        public void SetMeshes(TriangleGPU[] triGpu)
        {
            if (!IsMainThread)
            {
                EnqueueMainThread(() => SetMeshes(triGpu));
                return;
            }

            _allTrisGpu.Clear();
            if (triGpu != null && triGpu.Length > 0)
                _allTrisGpu.AddRange(triGpu);

            UploadAllMeshesToGpu();
        }

        private void UploadAllMeshesToGpu()
        {
            if (_allTrisGpu.Count == 0)
            {
                _numTris = 0;
                _numBvhNodes = 0;

                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _triSsbo);
                GL.BufferData(BufferTarget.ShaderStorageBuffer, 0, IntPtr.Zero, BufferUsageHint.StaticDraw);

                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _bvhSsbo);
                GL.BufferData(BufferTarget.ShaderStorageBuffer, 0, IntPtr.Zero, BufferUsageHint.StaticDraw);

                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

                _needsReset = true;
                return;
            }

            // Build BVH from triangles (expanded verts/indices, but with a stable tri-id mapping)
            var srcTris = CollectionsMarshal.AsSpan(_allTrisGpu);
            int triCount = srcTris.Length;

            // Expand to non-indexed geometry: 3 verts per tri, indices are 0..N-1
            int vertCount = triCount * 3;
            if (_bvhVerts.Length != vertCount) _bvhVerts = new Vector3[vertCount];
            if (_bvhIndices.Length != vertCount) _bvhIndices = new int[vertCount];
            var verts = _bvhVerts;
            var indices = _bvhIndices;

            for (int i = 0; i < triCount; i++)
            {
                Vector3 a = srcTris[i].V0.Xyz;
                Vector3 b = srcTris[i].V1.Xyz;
                Vector3 c = srcTris[i].V2.Xyz;

                int vBase = i * 3;
                verts[vBase + 0] = a;
                verts[vBase + 1] = b;
                verts[vBase + 2] = c;

                indices[vBase + 0] = vBase + 0;
                indices[vBase + 1] = vBase + 1;
                indices[vBase + 2] = vBase + 2;
            }

            // BVH build (Seb Lague-based class)
            BVH bvh = new(verts, indices, BVH.Quality.High);
            Console.WriteLine(bvh.stats.StringCreate());

            _numTris = bvh.Triangles.Length;
            _numBvhNodes = bvh.Nodes.Length;

            // Convert BVH nodes -> GPU nodes expected by your compute shader
            var gpuNodes = new BVHNodeGPU[_numBvhNodes];
            for (int i = 0; i < _numBvhNodes; i++)
            {
                var n = bvh.Nodes[i];

                // leaf if TriangleCount > 0
                if (n.TriangleCount > 0)
                {
                    gpuNodes[i] = new BVHNodeGPU
                    {
                        BMin = new Vector4(n.MinX, n.MinY, n.MinZ, 0f),
                        BMax = new Vector4(n.MaxX, n.MaxY, n.MaxZ, 0f),
                        Meta = new Vector4i(-1, -1, n.StartIndex, n.TriangleCount)
                    };
                }
                else
                {
                    // internal nodes: children are stored contiguously (left = StartIndex, right = StartIndex+1)
                    int left = n.StartIndex;
                    int right = n.StartIndex + 1;

                    gpuNodes[i] = new BVHNodeGPU
                    {
                        BMin = new Vector4(n.MinX, n.MinY, n.MinZ, 0f),
                        BMax = new Vector4(n.MaxX, n.MaxY, n.MaxZ, 0f),
                        Meta = new Vector4i(left, right, 0, 0)
                    };
                }
            }

            // Convert BVH triangles -> TriangleGPU, but KEEP original material per triangle using mapping
            var trisReordered = new TriangleGPU[_numTris];

            // If you added TriangleSourceIndices to BVH, use it:
            //   - each BVH triangle corresponds to an original triangle id (0..triCount-1)
            //   - that id tells us which TriangleGPU (materials) to copy from
            //
            // If you didn't add it, this falls back to "first triangle's material for all" (not great, but renders).
            bool hasMap = false;
            int[] map = [];

            // compile-time: only works if you added the field/property to BVH
            // (keep it simple: try/catch reflection-free by just referencing it if you added it)
            try
            {
                map = bvh.TriangleSourceIndices; // <-- requires you to add this to BVH.cs
                hasMap = map != null && map.Length == _numTris;
            }
            catch
            {
                hasMap = false;
            }

            TriangleGPU fallbackMat = srcTris[0];

            for (int i = 0; i < _numTris; i++)
            {
                var t = bvh.Triangles[i];

                TriangleGPU matSrc = fallbackMat;
                if (hasMap && map != null && i < map.Length)
                {
                    int srcId = map[i];
                    if ((uint)srcId < (uint)srcTris.Length)
                        matSrc = srcTris[srcId];
                }

                trisReordered[i] = new TriangleGPU
                {
                    V0 = new Vector4(t.A, 0f),
                    V1 = new Vector4(t.B, 0f),
                    V2 = new Vector4(t.C, 0f),
                    ColSmo = matSrc.ColSmo,
                    EmiEmi = matSrc.EmiEmi,
                    A = matSrc.A
                };
            }

            // Upload to SSBOs
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _triSsbo);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                _numTris * TriangleGpuSize,
                trisReordered,
                BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _bvhSsbo);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                _numBvhNodes * BvhNodeGpuSize,
                gpuNodes,
                BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

            _needsReset = true;
        }

        // =========================
        // Accum reset
        // =========================
        private void ResetAccumulation()
        {
            _frame = 0;
            _readTex = _accumA;
            _writeTex = _accumB;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _clearFbo);
            GL.ClearColor(0, 0, 0, 1);

            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _accumA, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _accumB, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            _needsReset = false;
        }

        // =========================
        // GL helpers
        // =========================
        private static void DeleteTex(ref int tex)
        {
            if (tex != 0)
            {
                GL.DeleteTexture(tex);
                tex = 0;
            }
        }

        private static int CreateTexRgba16f(int w, int h)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, w, h, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private static int CreateTexRgba8(int w, int h)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private static int ChooseIntegerScale(int height, int targetHeight)
        {
            return Math.Max(1, (int)MathF.Floor(height / (float)targetHeight));
        }

        private static void GetBasis(float yawDeg, float pitchDeg, out Vector3 forward, out Vector3 right, out Vector3 up)
        {
            float yaw = MathHelper.DegreesToRadians(yawDeg);
            float pitch = MathHelper.DegreesToRadians(pitchDeg);

            forward = new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw)
            ).Normalized();

            right = Vector3.Cross(forward, Vector3.UnitY).Normalized();
            up = Vector3.Cross(right, forward).Normalized();
        }

        private void UpdateBasisIfNeeded()
        {
            if (!_basisDirty && _yaw == _lastYaw && _pitch == _lastPitch)
                return;

            GetBasis(_yaw, _pitch, out _camForward, out _camRight, out _camUp);
            _lastYaw = _yaw;
            _lastPitch = _pitch;
            _basisDirty = false;
        }

        // =========================
        // Scene: spheres
        // =========================
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SphereCPU(Vector3 pos, float radius, Vector3 albedo, float smooth, Vector3 emission, float emissionStrength, float alpha, float ior = 1.0f, float absorb = 0.0f)
        {
            public readonly Vector4 PosRad = new(pos.X, pos.Y, pos.Z, radius);
            public readonly Vector4 ColSmo = new(albedo.X, albedo.Y, albedo.Z, smooth);
            public readonly Vector4 EmiEmi = new(emission.X, emission.Y, emission.Z, emissionStrength);
            public readonly Vector4 AlphaIorAbsorb = new(alpha, ior, absorb, 0f);
        }

        private void UploadSpheres()
        {
            if (!IsMainThread)
            {
                EnqueueMainThread(UploadSpheres);
                return;
            }

            var spheres = new SphereCPU[]
            {
                new(new Vector3(0,-1001,0), 1000f, new Vector3(0.82f,0.82f,0.82f), 0.02f, Vector3.Zero, 0f, 1f),
                new(new Vector3(-1.6f,0.7f,4.0f), 0.7f, Vector3.One, 1.0f, Vector3.Zero, 0f, 1f),
                new(new Vector3(1.3f,0.65f,3.2f), 0.65f, Vector3.One, 0.08f, Vector3.Zero, 0f, 1f),
                new(new Vector3(2.6f,1.3f,3.2f), 0.65f, Vector3.One, 1.0f, Vector3.Zero, 0f, 1.0f)/*,
                new(new Vector3(3f,12f,7f), 3.0f, Vector3.Zero, 0.0f, Vector3.One, 1f)*/
            };

            int sphereCpuSize = Marshal.SizeOf<SphereCPU>();

            _numSpheres = spheres.Length;

            
            // Upload to SSBOs
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _spheresSSbo);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                _numSpheres * sphereCpuSize,
                spheres,
                BufferUsageHint.StaticDraw);

            _needsReset = true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CubeGPU(Vector3 position, Vector3 size, Vector3 albedo, float smoothness, Vector3 emission, float emissionStrength, float alpha, float ior = 1.0f, float absorb = 0.0f)
        {
            public readonly Vector4 Pos = new(position.X, position.Y, position.Z, 0f);
            public readonly Vector4 Scale = new(size.X, size.Y, size.Z, 0f);
            public readonly Vector4 ColSmo = new(albedo.X, albedo.Y, albedo.Z, smoothness);
            public readonly Vector4 EmiEmi = new(emission.X, emission.Y, emission.Z, emissionStrength);
            public readonly Vector4 AlphaIorAbsorb = new(alpha, ior, absorb, 0f);
        }

        private void UploadCubes()
        {
            if (!IsMainThread)
            {
                EnqueueMainThread(UploadCubes);
                return;
            }

            var cubes = new CubeGPU[]
            {
                new(new Vector3(0f, 0.01f, 10f), Vector3.One, new Vector3(1.0f, 1.8f, 1.0f), 0.65f, Vector3.Zero, 0f, 0.1f)
            };

            int cubeGpuSize = Marshal.SizeOf<CubeGPU>();

            _numCubes = cubes.Length;

            
            // Upload to SSBOs
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _cubesSSbo);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                _numCubes * cubeGpuSize,
                cubes,
                BufferUsageHint.StaticDraw);

            _needsReset = true;
        }

        public void ContstructMesh(string path, Vector3 position, Vector3 size, Vector3 rotation, Vector3 colour, float smoothness, Vector3 emission, float emissionStrength, float alpha = 1f)
        {
            ObjLoader.Load(out List<TriangleGPU> tris, path, position, rotation, size, colour, smoothness, emission, emissionStrength, alpha);
            UploadMesh([.. tris]);
        }

        public void ContstructMeshAsync(string path, Vector3 position, Vector3 size, Vector3 rotation, Vector3 colour, float smoothness, Vector3 emission, float emissionStrength, float alpha = 1f)
        {
            var worker = new Thread(() =>
            {
                ObjLoader.Load(out List<TriangleGPU> tris, path, position, rotation, size, colour, smoothness, emission, emissionStrength, alpha);
                var triArray = tris.ToArray();
                EnqueueMainThread(() => UploadMesh(triArray));
            })
            {
                IsBackground = true
            };

            worker.Start();
        }
    }
}
