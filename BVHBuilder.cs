using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using TriangleGPU = Zirconium.Main.Engine.TriangleGPU;

namespace Zirconium.Optimizations
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BVHNodeGPU
    {
        public Vector4 BMin;   // xyz min
        public Vector4 BMax;   // xyz max
        public Vector4i Meta;  // x=left, y=right, z=firstTri, w=triCount (w>0 => leaf)
    }

    public static class BVHBuilder
    {
        // Same signature as yours (extra optional knobs at end).
        public static (BVHNodeGPU[] nodes, TriangleGPU[] trisReordered) Build(
            TriangleGPU[] tris,
            int leafSize = 4,
            int binCount = 12,
            int maxDepth = 64)
        {
            if (tris == null) throw new ArgumentNullException(nameof(tris));
            int n = tris.Length;
            if (n == 0) return (Array.Empty<BVHNodeGPU>(), Array.Empty<TriangleGPU>());

            leafSize = Math.Max(1, leafSize);
            binCount = Math.Clamp(binCount, 4, 64);
            maxDepth = Math.Clamp(maxDepth, 1, 512);

            // permutation of triangle indices
            int[] ids = new int[n];
            for (int i = 0; i < n; i++) ids[i] = i;

            // precompute per-triangle bounds + centroid (AABB center)
            var triMin = new Vector3[n];
            var triMax = new Vector3[n];
            var cen = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 v0 = tris[i].V0.Xyz;
                Vector3 v1 = tris[i].V1.Xyz;
                Vector3 v2 = tris[i].V2.Xyz;

                Vector3 mn = Vector3.ComponentMin(v0, Vector3.ComponentMin(v1, v2));
                Vector3 mx = Vector3.ComponentMax(v0, Vector3.ComponentMax(v1, v2));

                triMin[i] = mn;
                triMax[i] = mx;
                cen[i] = (mn + mx) * 0.5f; // AABB center is stable for binning
            }

            // outputs
            var nodes = new List<BVHNodeGPU>(Math.Max(2, 2 * n));
            var outTris = new List<TriangleGPU>(n);

            static float SurfaceArea(in Vector3 mn, in Vector3 mx)
            {
                Vector3 e = mx - mn;
                return 2f * (e.X * e.Y + e.Y * e.Z + e.Z * e.X);
            }

            int BuildNode(int start, int count, int depth)
            {
                int nodeIndex = nodes.Count;
                nodes.Add(default);

                int end = start + count;

                // node bounds + centroid bounds
                Vector3 nodeMin = new(float.PositiveInfinity);
                Vector3 nodeMax = new(float.NegativeInfinity);
                Vector3 cMin = new(float.PositiveInfinity);
                Vector3 cMax = new(float.NegativeInfinity);

                for (int i = start; i < end; i++)
                {
                    int id = ids[i];
                    nodeMin = Vector3.ComponentMin(nodeMin, triMin[id]);
                    nodeMax = Vector3.ComponentMax(nodeMax, triMax[id]);
                    cMin = Vector3.ComponentMin(cMin, cen[id]);
                    cMax = Vector3.ComponentMax(cMax, cen[id]);
                }

                // leaf / stop conditions
                const float EXT_EPS = 1e-6f;
                Vector3 extent = cMax - cMin;

                bool forceLeaf =
                    count <= leafSize ||
                    depth >= maxDepth ||
                    (extent.X < EXT_EPS && extent.Y < EXT_EPS && extent.Z < EXT_EPS);

                if (forceLeaf)
                {
                    int firstTri = outTris.Count;
                    for (int i = start; i < end; i++)
                        outTris.Add(tris[ids[i]]);

                    nodes[nodeIndex] = new BVHNodeGPU
                    {
                        BMin = new Vector4(nodeMin, 0f),
                        BMax = new Vector4(nodeMax, 0f),
                        Meta = new Vector4i(-1, -1, firstTri, count)
                    };
                    return nodeIndex;
                }

                float leafCost = SurfaceArea(nodeMin, nodeMax) * count;
                float bestCost = float.PositiveInfinity;
                int bestAxis = -1;
                int bestSplit = -1;

                // Allocate reusable buffers outside the loop
                Span<int> bCount = stackalloc int[binCount];
                Span<Vector3> bMin = stackalloc Vector3[binCount];
                Span<Vector3> bMax = stackalloc Vector3[binCount];
                Span<int> leftCount = stackalloc int[binCount];
                Span<Vector3> leftMin = stackalloc Vector3[binCount];
                Span<Vector3> leftMax = stackalloc Vector3[binCount];
                Span<int> rightCount = stackalloc int[binCount];
                Span<Vector3> rightMin = stackalloc Vector3[binCount];
                Span<Vector3> rightMax = stackalloc Vector3[binCount];

                // SAH binning (no heap allocs)
                for (int axis = 0; axis < 3; axis++)
                {
                    float axisExtent = axis == 0 ? extent.X : axis == 1 ? extent.Y : extent.Z;
                    if (axisExtent < EXT_EPS) continue;

                    float axisMin = axis == 0 ? cMin.X : axis == 1 ? cMin.Y : cMin.Z;
                    float invExtent = binCount / axisExtent;

                    for (int b = 0; b < binCount; b++)
                    {
                        bCount[b] = 0;
                        bMin[b] = new Vector3(float.PositiveInfinity);
                        bMax[b] = new Vector3(float.NegativeInfinity);
                    }

                    for (int i = start; i < end; i++)
                    {
                        int id = ids[i];
                        float v = axis == 0 ? cen[id].X : axis == 1 ? cen[id].Y : cen[id].Z;

                        int b = (int)((v - axisMin) * invExtent);
                        if (b < 0) b = 0;
                        else if (b >= binCount) b = binCount - 1;

                        bCount[b]++;
                        bMin[b] = Vector3.ComponentMin(bMin[b], triMin[id]);
                        bMax[b] = Vector3.ComponentMax(bMax[b], triMax[id]);
                    }

                    // prefix
                    Vector3 rMin = new(float.PositiveInfinity);
                    Vector3 rMax = new(float.NegativeInfinity);
                    int rCnt = 0;
                    for (int b = 0; b < binCount; b++)
                    {
                        if (bCount[b] > 0)
                        {
                            rMin = Vector3.ComponentMin(rMin, bMin[b]);
                            rMax = Vector3.ComponentMax(rMax, bMax[b]);
                        }
                        rCnt += bCount[b];
                        leftMin[b] = rMin;
                        leftMax[b] = rMax;
                        leftCount[b] = rCnt;
                    }

                    // suffix
                    rMin = new Vector3(float.PositiveInfinity);
                    rMax = new Vector3(float.NegativeInfinity);
                    rCnt = 0;
                    for (int b = binCount - 1; b >= 0; b--)
                    {
                        if (bCount[b] > 0)
                        {
                            rMin = Vector3.ComponentMin(rMin, bMin[b]);
                            rMax = Vector3.ComponentMax(rMax, bMax[b]);
                        }
                        rCnt += bCount[b];
                        rightMin[b] = rMin;
                        rightMax[b] = rMax;
                        rightCount[b] = rCnt;
                    }

                    for (int split = 1; split < binCount; split++)
                    {
                        int lc = leftCount[split - 1];
                        int rc = rightCount[split];
                        if (lc == 0 || rc == 0) continue;

                        float cost =
                            SurfaceArea(leftMin[split - 1], leftMax[split - 1]) * lc +
                            SurfaceArea(rightMin[split], rightMax[split]) * rc;

                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestAxis = axis;
                            bestSplit = split;
                        }
                    }
                }

                // if SAH didn't beat leaf, just make leaf
                if (bestAxis < 0 || bestCost >= leafCost)
                {
                    int firstTri = outTris.Count;
                    for (int i = start; i < end; i++)
                        outTris.Add(tris[ids[i]]);

                    nodes[nodeIndex] = new BVHNodeGPU
                    {
                        BMin = new Vector4(nodeMin, 0f),
                        BMax = new Vector4(nodeMax, 0f),
                        Meta = new Vector4i(-1, -1, firstTri, count)
                    };
                    return nodeIndex;
                }

                // compute split position at bin boundary
                float axisMinC = bestAxis == 0 ? cMin.X : bestAxis == 1 ? cMin.Y : cMin.Z;
                float axisExtentC = bestAxis == 0 ? extent.X : bestAxis == 1 ? extent.Y : extent.Z;
                float splitPos = axisMinC + axisExtentC * (bestSplit / (float)binCount);

                // partition ids in-place (two-pointer)
                int iL = start;
                int iR = end - 1;
                while (iL <= iR)
                {
                    int id = ids[iL];
                    float v = bestAxis == 0 ? cen[id].X : bestAxis == 1 ? cen[id].Y : cen[id].Z;

                    if (v < splitPos)
                    {
                        iL++;
                    }
                    else
                    {
                        (ids[iL], ids[iR]) = (ids[iR], ids[iL]);
                        iR--;
                    }
                }

                int mid = iL;

                // degenerate split -> median split
                if (mid == start || mid == end)
                {
                    Array.Sort(ids, start, count, Comparer<int>.Create((a, b) =>
                    {
                        float ca = bestAxis == 0 ? cen[a].X : bestAxis == 1 ? cen[a].Y : cen[a].Z;
                        float cb = bestAxis == 0 ? cen[b].X : bestAxis == 1 ? cen[b].Y : cen[b].Z;
                        return ca.CompareTo(cb);
                    }));
                    mid = start + (count / 2);
                }

                int left = BuildNode(start, mid - start, depth + 1);
                int right = BuildNode(mid, end - mid, depth + 1);

                nodes[nodeIndex] = new BVHNodeGPU
                {
                    BMin = new Vector4(nodeMin, 0f),
                    BMax = new Vector4(nodeMax, 0f),
                    Meta = new Vector4i(left, right, 0, 0) // triCount=0 => internal
                };

                return nodeIndex;
            }

            int root = BuildNode(0, n, 0);
            if (root != 0) throw new Exception("BVH root not 0 (unexpected).");

            return (nodes.ToArray(), outTris.ToArray());
        }
    }
}
