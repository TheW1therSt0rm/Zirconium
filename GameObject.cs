using Zirconium.Optimizations;
using Zirconium.OBJ;
using Zirconium.Types.Enums;
using Zirconium.Types.BluePrints;
using OpenTK.Mathematics;
using CubeGPU = Zirconium.Main.Engine.CubeGPU;
using SphereGPU = Zirconium.Main.Engine.SphereGPU;
using TriangleGPU = Zirconium.Main.Engine.TriangleGPU;

namespace Zirconium.Engine
{
    public class GameObject
    {
        public GameObjectType _type;
        public List<IGpuType> Send = [];

        public GameObject(GameObjectType type, string? path, Vector3 pos, Vector3 rot, Vector3 size, Vector3 albedo, float smoothness, Vector3 emission, float emissionStrength, float alpha, float ior = 1.0f, float absorb = 0.0f)
        {
            _type = type;
            switch (_type)
            {
                default:
                    break;
                case GameObjectType.Cube:
                    Send.Add(new CubeGPU(pos, size, albedo, smoothness, emission, emissionStrength, alpha, ior, absorb));
                    break;
                case GameObjectType.Sphere:
                    Send.Add(new SphereGPU());
                    break;
                case GameObjectType.Mesh:
                    List<TriangleGPU> tris = [];
                    if (path != null)
                    {
                        ObjLoader.Load(out tris, path, pos, rot, size, albedo, smoothness, emission, emissionStrength, alpha);
                    }
                    foreach (TriangleGPU tri in tris)
                    {
                        Send.Add(tri);
                    }
                    break;
            }
        }
    }
}