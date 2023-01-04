using Godot;
using System;
using System.Collections.Generic;

public class CubeMesh : Spatial {
    private ArrayMesh mesh = new ArrayMesh();
    private ConcavePolygonShape singleShape = new ConcavePolygonShape();
    private ConcavePolygonShape doubleShape = new ConcavePolygonShape();

    private struct MeshData {
        public Dictionary<Guid, SurfaceTool> surfs;
        public List<Vector3> singleTris, doubleTris;
    }

    private class CubeStats {
        // leaves = branches * 7 + 1
        public int branches, boundQuads;
    }

    public override void _Ready() {
        GetNode<MeshInstance>("MeshInstance").Mesh = mesh;
        GetNode<CollisionShape>("StaticBody/SingleSided").Shape = singleShape;
        GetNode<CollisionShape>("StaticBody/DoubleSided").Shape = doubleShape;
    }

    public void UpdateMesh(Cube root, Vector3 pos, float size, Guid? voidVolume,
            Dictionary<Guid, Material> materials) {
        ulong startTick = Time.GetTicksMsec();
        mesh.ClearSurfaces();
        var data = new MeshData();
        data.surfs = new Dictionary<Guid, SurfaceTool>();
        data.singleTris = new List<Vector3>();
        data.doubleTris = new List<Vector3>();
        var stats = new CubeStats();

        BuildCube(data, root, pos, size, stats);
        if (voidVolume is Guid vol) {
            var voidLeaf = new Cube.Leaf(vol).Immut();
            for (int axis = 0; axis < 3; axis++) {
                BuildBoundary(data, voidLeaf, root, pos, size, axis, stats);
            }
        }
        GD.Print($"Generating mesh took {Time.GetTicksMsec() - startTick}ms"
            + $" with {stats.branches} branches, {stats.boundQuads} quads");

        startTick = Time.GetTicksMsec();
        int surfI = 0;
        foreach (var item in data.surfs) {
            item.Value.GenerateTangents();
            item.Value.Index();
            // TODO: this is slow in Godot 3! https://github.com/godotengine/godot/issues/56524
            item.Value.Commit(mesh);
            mesh.SurfaceSetMaterial(surfI++, materials[item.Key]);
        }
        singleShape.Data = data.singleTris.ToArray();
        doubleShape.Data = data.doubleTris.ToArray();
        GD.Print($"Updating mesh took {Time.GetTicksMsec() - startTick}ms");
    }

    // TODO would the recursive structure in OptimizeCube work better here?
    private void BuildCube(MeshData data, Cube cube, Vector3 pos, float size, CubeStats stats) {
        if (cube is Cube.BranchImmut branch) {
            stats.branches++;

            for (int axis = 0; axis < 3; axis++) {
                for (int i = 0; i < 4; i++) {
                    int minI = CubeUtil.CycleIndex(i, axis + 1);
                    int maxI = minI | (1 << axis);
                    Vector3 boundPos = pos + CubeUtil.IndexVector(maxI) * (size / 2);
                    BuildBoundary(data, branch.child(minI), branch.child(maxI),
                        boundPos, size / 2, axis, stats);
                }
            }

            for (int i = 0; i < 8; i++) {
                var childPos = pos + CubeUtil.IndexVector(i) * (size / 2);
                BuildCube(data, branch.child(i), childPos, size / 2, stats);
            }
        }
    }

    private void BuildBoundary(MeshData data, Cube cubeMin, Cube cubeMax,
            Vector3 pos, float size, int axis, CubeStats stats) {
        if (cubeMin is Cube.LeafImmut leafMin && cubeMax is Cube.LeafImmut leafMax) {
            if (leafMin.Val.volume == leafMax.Val.volume)
                return;
            Cube.Face face = leafMax.face(axis).Val;
            bool solidBoundary = leafMin.Val.volume == Volume.SOLID
                || leafMax.Val.volume == Volume.SOLID;
            Guid material = (solidBoundary ? face.base_ : face.overlay).material;
            if (!data.surfs.TryGetValue(material, out SurfaceTool st)) {
                data.surfs[material] = st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
            }
            var tris = solidBoundary ? data.singleTris : data.doubleTris;
            if (leafMax.Val.volume != Volume.SOLID)
                AddQuad(st, tris, pos, size, axis, true, stats);
            if (leafMin.Val.volume != Volume.SOLID)
                AddQuad(st, tris, pos, size, axis, false, stats);
        } else {
            for (int i = 0; i < 4; i++) {
                int maxI = CubeUtil.CycleIndex(i, axis + 1);
                Cube childMin = cubeMin, childMax = cubeMax;
                if (cubeMin is Cube.BranchImmut branchMin)
                    childMin = branchMin.child(maxI | (1 << axis));
                if (cubeMax is Cube.BranchImmut branchMax)
                    childMax = branchMax.child(maxI);
                var childPos = pos + CubeUtil.IndexVector(maxI) * (size / 2);
                BuildBoundary(data, childMin, childMax, childPos, size / 2, axis, stats);
            }
        }
    }

    private void AddQuad(SurfaceTool st, List<Vector3> tris,
            Vector3 pos, float size, int axis, bool dir, CubeStats stats) {
        stats.boundQuads++;
        var vS = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 1)) * size;
        var vT = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 2)) * size;
        Vector3[] verts;
        if (dir) {
            verts = new Vector3[] { pos, pos + vT, pos + vS + vT, pos + vS };
            st.AddNormal(CubeUtil.IndexVector(1 << axis));
        } else {
            verts = new Vector3[] { pos, pos + vS, pos + vS + vT, pos + vT };
            st.AddNormal(-CubeUtil.IndexVector(1 << axis));
        }
        var uvs = new Vector2[4];
        for (int i = 0; i < 4; i++) {
            Vector3 cycleVert = CubeUtil.CycleVector(verts[i], 5 - axis);
            uvs[i] = new Vector2(cycleVert.x, cycleVert.y);
        }
        st.AddTriangleFan(verts, uvs);
        tris.AddRange(new Vector3[] { verts[0], verts[1], verts[2], verts[0], verts[2], verts[3] });
    }
}
