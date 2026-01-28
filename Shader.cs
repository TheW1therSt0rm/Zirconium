using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Zirconium.Main
{
    public sealed class Shader : IDisposable
    {
        public int Handle { get; private set; }
        private readonly Dictionary<string, int> _uniformCache = [];

        public enum Kind
        {
            Graphics, // .vert + .frag
            Compute   // .comp
        }

        public Kind ProgramKind { get; }

        public Shader(string shaderBaseName, Kind kind)
        {
            ProgramKind = kind;

            Handle = GL.CreateProgram();

            if (kind == Kind.Graphics)
            {
                string vertPath = $"{shaderBaseName}.vert";
                string fragPath = $"{shaderBaseName}.frag";

                if (!File.Exists(vertPath)) throw new FileNotFoundException(vertPath);
                if (!File.Exists(fragPath)) throw new FileNotFoundException(fragPath);

                int vs = Compile(ShaderType.VertexShader, vertPath);
                int fs = Compile(ShaderType.FragmentShader, fragPath);

                GL.AttachShader(Handle, vs);
                GL.AttachShader(Handle, fs);
                LinkOrThrow(Handle, shaderBaseName);

                GL.DetachShader(Handle, vs);
                GL.DetachShader(Handle, fs);
                GL.DeleteShader(vs);
                GL.DeleteShader(fs);
            }
            else // Compute
            {
                string compPath = $"{shaderBaseName}.comp";
                if (!File.Exists(compPath)) throw new FileNotFoundException(compPath);

                int cs = Compile(ShaderType.ComputeShader, compPath);

                GL.AttachShader(Handle, cs);
                LinkOrThrow(Handle, shaderBaseName);

                GL.DetachShader(Handle, cs);
                GL.DeleteShader(cs);
            }
        }

        private static void LinkOrThrow(int program, string name)
        {
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                throw new Exception($"Program link log ({name}):\n{GL.GetProgramInfoLog(program)}");
        }

        private static string ReadTextNoBom(string path)
        {
            string s = File.ReadAllText(path);
            if (s.Length > 0 && s[0] == '\uFEFF') s = s[1..]; // UTF-8 BOM
            return s;
        }

        private static int Compile(ShaderType type, string path)
        {
            string src = ReadTextNoBom(path);

            // Print numbered source like your original (super useful for “unexpected EOF”)
            Console.WriteLine("===== " + path + " =====");
            var lines = src.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
                Console.WriteLine($"{i + 1:0000}: {lines[i]}");

            int sh = GL.CreateShader(type);
            GL.ShaderSource(sh, src);
            GL.CompileShader(sh);

            GL.GetShader(sh, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                throw new Exception($"{type} compile log ({path}):\n{GL.GetShaderInfoLog(sh)}");

            return sh;
        }

        public void Use() => GL.UseProgram(Handle);

        private int Loc(string name)
        {
            if (_uniformCache.TryGetValue(name, out int loc)) return loc;
            loc = GL.GetUniformLocation(Handle, name);
            _uniformCache[name] = loc;
            return loc;
        }

        // ----- Uniform setters -----
        public void Set(string name, int v) { int l = Loc(name); if (l >= 0) GL.Uniform1(l, v); }
        public void Set(string name, float v) { int l = Loc(name); if (l >= 0) GL.Uniform1(l, v); }
        public void Set(string name, Vector2 v) { int l = Loc(name); if (l >= 0) GL.Uniform2(l, v); }
        public void Set(string name, Vector2i v) { int l = Loc(name); if (l >= 0) GL.Uniform2(l, v.X, v.Y); }
        public void Set(string name, Vector3 v) { int l = Loc(name); if (l >= 0) GL.Uniform3(l, v); }
        public void Set(string name, Vector4 v) { int l = Loc(name); if (l >= 0) GL.Uniform4(l, v); }

        public void Set(string name, Matrix4 m, bool transpose = false)
        {
            int l = Loc(name);
            if (l >= 0) GL.UniformMatrix4(l, transpose, ref m);
        }

        public void Dispose()
        {
            if (Handle != 0)
            {
                GL.DeleteProgram(Handle);
                Handle = 0;
            }
        }
    }
}
