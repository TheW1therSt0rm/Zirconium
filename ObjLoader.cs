using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenTK.Mathematics;

namespace RayTracing
{
    internal static class ObjLoader
    {
        public static Matrix4 ModelMatrix(Vector3 position, Vector3 scale, Vector3 rotation)
        {
            Matrix4 rotationM = Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y) * Matrix4.CreateRotationZ(rotation.Z);
            return rotationM * Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(position);
        }

        public static void Load(
            out List<TriangleGPU> tris,
            string path,
            Vector3 pos,
            Vector3 rot,
            Vector3 scl,
            Vector3 colour,
            float smooth,
            Vector3 emission,
            float emissionStrength,
            float Alpha = 1f)
        {
            Matrix4 rotationM = Matrix4.CreateRotationX(rot.X) * Matrix4.CreateRotationY(rot.Y) * Matrix4.CreateRotationZ(rot.Z);
            List<Vector3> positions = [];
            List<Vector3> normals = [];
            tris = [];

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("v "))
                {
                    var sp = Split(line);
                    if (sp.Length < 4) continue;

                    float x = ParseF(sp[1]);
                    float y = ParseF(sp[2]);
                    float z = ParseF(sp[3]);
                    Vector4 posEdit = new(x, y, z, 1f);
                    posEdit *= ModelMatrix(pos, scl, rot);
                    positions.Add(new(posEdit.X / posEdit.W, posEdit.Y / posEdit.W, posEdit.Z / posEdit.W));
                }
                else if (line.StartsWith("vn "))
                {
                    var sp = Split(line);
                    if (sp.Length < 4) continue;

                    float x = ParseF(sp[1]);
                    float y = ParseF(sp[2]);
                    float z = ParseF(sp[3]);
                    Vector3 n = new(x, y, z);
                    if (Math.Abs(scl.X) > 1e-6f) n.X /= scl.X;
                    if (Math.Abs(scl.Y) > 1e-6f) n.Y /= scl.Y;
                    if (Math.Abs(scl.Z) > 1e-6f) n.Z /= scl.Z;
                    Vector4 n4 = new(n, 0f);
                    n4 *= rotationM;
                    n = n4.Xyz;
                    if (n.LengthSquared > 0)
                        n = n.Normalized();
                    normals.Add(n);
                }
                else if (line.StartsWith("f "))
                {
                    // f v1 v2 v3 ... where each vi can be: "p", "p/t", "p//n", "p/t/n"
                    var sp = Split(line);
                    if (sp.Length < 4) continue; // need at least 3 verts

                    // Collect vertex POSITION indices for this face (0-based into positions[])
                    var face = new List<int>(sp.Length - 1);
                    var faceNorm = new List<int>(sp.Length - 1);

                    for (int i = 1; i < sp.Length; i++) // start at 1 to skip the 'f'
                    {
                        string token = sp[i];

                        // Take the position index before the first slash
                        int slash = token.IndexOf('/');
                        string pStr = (slash >= 0) ? token.Substring(0, slash) : token;

                        if (pStr.Length == 0) continue;

                        int idx = ParseI(pStr);

                        // OBJ indices are 1-based; negative means relative to end
                        //  1  -> 0
                        // -1  -> positions.Count - 1
                        if (idx > 0) idx--;
                        else if (idx < 0) idx = positions.Count + idx; // idx is negative
                        else continue; // 0 is invalid in OBJ

                        if ((uint)idx >= (uint)positions.Count) continue;
                        face.Add(idx);

                        int nIdx = -1;
                        if (slash >= 0)
                        {
                            // token format: p, p/t, p//n, or p/t/n
                            string[] parts = token.Split('/');
                            if (parts.Length >= 3 && parts[2].Length > 0)
                            {
                                nIdx = ParseI(parts[2]);
                                if (nIdx > 0) nIdx--;
                                else if (nIdx < 0) nIdx = normals.Count + nIdx;
                                else nIdx = -1;
                            }
                        }
                        faceNorm.Add(nIdx);
                    }

                    if (face.Count < 3) continue;

                    // Fan triangulation: (0, i, i+1)
                    Vector4 colSmo = new(colour, smooth);
                    Vector4 emiEmi = new(emission, emissionStrength);
                    Vector4 alpha = new(Alpha, 0.0f, 0.0f, 0.0f);

                    int i0 = face[0];
                    Vector3 v0 = positions[i0];
                    int n0i = faceNorm[0];
                    Vector3 n0Base = (n0i >= 0 && n0i < normals.Count) ? normals[n0i] : Vector3.Zero;

                    for (int i = 1; i + 1 < face.Count; i++)
                    {
                        Vector3 v1 = positions[face[i]];
                        Vector3 v2 = positions[face[i + 1]];
                        int n1i = faceNorm[i];
                        int n2i = faceNorm[i + 1];
                        Vector3 n0 = n0Base;
                        Vector3 n1 = (n1i >= 0 && n1i < normals.Count) ? normals[n1i] : Vector3.Zero;
                        Vector3 n2 = (n2i >= 0 && n2i < normals.Count) ? normals[n2i] : Vector3.Zero;

                        if (n0.LengthSquared < 1e-12f || n1.LengthSquared < 1e-12f || n2.LengthSquared < 1e-12f)
                        {
                            Vector3 faceN = Vector3.Cross(v1 - v0, v2 - v0);
                            if (faceN.LengthSquared < 1e-12f) faceN = Vector3.UnitY;
                            else faceN = faceN.Normalized();
                            n0 = faceN;
                            n1 = faceN;
                            n2 = faceN;
                        }

                        tris.Add(new TriangleGPU
                        {
                            V0 = new Vector4(v0, 0f),
                            V1 = new Vector4(v1, 0f),
                            V2 = new Vector4(v2, 0f),
                            ColSmo = colSmo,
                            EmiEmi = emiEmi,
                            A = alpha
                        });
                    }
                }
            }

            Console.WriteLine($"{path} has: {positions.Count} vertices and: {tris.Count} triangles");
        }

        private static string[] Split(string s) =>
            s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        private static float ParseF(string s) =>
            float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static int ParseI(string s) =>
            int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
