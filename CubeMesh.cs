using Godot;

public class CubeMesh : Spatial {
    private ArrayMesh mesh = new ArrayMesh();
    private ConcavePolygonShape shape = new ConcavePolygonShape();

    private class CubeStats {
        // leaves = branches * 7 + 1
        public int branches, boundQuads;
    }

    public override void _Ready() {
        GetNode<MeshInstance>("MeshInstance").Mesh = mesh;
        GetNode<CollisionShape>("StaticBody/CollisionShape").Shape = shape;
    }

    public void UpdateMesh(Cube root, Vector3 pos, float size, Cube.Volume? voidVolume) {
        ulong startTick = Time.GetTicksMsec();
        mesh.ClearSurfaces();
        var st = new SurfaceTool(); // TODO better approach?
        st.Begin(Mesh.PrimitiveType.Triangles);
        var stats = new CubeStats();

        BuildCube(st, root, pos, size, stats);
        if (voidVolume is Cube.Volume vol) {
            var voidLeaf = new Cube.Leaf { volume = vol }.Immut();
            for (int axis = 0; axis < 3; axis++) {
                BuildBoundary(st, voidLeaf, root, pos, size, axis, stats);
            }
        }
        GD.Print($"Generating mesh took {Time.GetTicksMsec() - startTick}ms"
            + $" with {stats.branches} branches, {stats.boundQuads} quads");

        startTick = Time.GetTicksMsec();
        // TODO probably inefficient! throwing away a lot of work
        var triangles = (Vector3[])st.CommitToArrays()[(int)Mesh.ArrayType.Vertex];
        if (triangles != null) {
            shape.Data = triangles;
            st.GenerateTangents();
            st.Index();
        } else {
            shape.Data = new Vector3[0];
        }
        // TODO: this is slow in Godot 3! https://github.com/godotengine/godot/issues/56524
        st.Commit(mesh);
        GD.Print($"Updating mesh took {Time.GetTicksMsec() - startTick}ms");
    }

    // TODO would the recursive structure in OptimizeCube work better here?
    private void BuildCube(SurfaceTool st, Cube cube, Vector3 pos, float size, CubeStats stats) {
        if (cube is Cube.BranchImmut branch) {
            stats.branches++;

            for (int axis = 0; axis < 3; axis++) {
                for (int i = 0; i < 4; i++) {
                    int minI = CubeUtil.CycleIndex(i, axis + 1);
                    int maxI = minI | (1 << axis);
                    Vector3 boundPos = pos + CubeUtil.IndexVector(maxI) * (size / 2);
                    BuildBoundary(st, branch.child(minI), branch.child(maxI),
                        boundPos, size / 2, axis, stats);
                }
            }

            for (int i = 0; i < 8; i++) {
                var childPos = pos + CubeUtil.IndexVector(i) * (size / 2);
                BuildCube(st, branch.child(i), childPos, size / 2, stats);
            }
        }
    }

    private void BuildBoundary(SurfaceTool st, Cube cubeMin, Cube cubeMax,
            Vector3 pos, float size, int axis, CubeStats stats) {
        if (cubeMin is Cube.LeafImmut leafMin && cubeMax is Cube.LeafImmut leafMax) {
            if (leafMin.Val.volume.Equals(leafMax.Val.volume))
                return;
            stats.boundQuads++;
            var vS = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 1)) * size;
            var vT = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 2)) * size;
            Vector3[] verts;
            if (leafMax.Val.volume == Cube.Volume.Empty) {
                verts = new Vector3[] { pos, pos + vT, pos + vS + vT, pos + vS };
                st.AddNormal(CubeUtil.IndexVector(1 << axis));
            } else {
                verts = new Vector3[] { pos, pos + vS, pos + vS + vT, pos + vT };
                st.AddNormal(-CubeUtil.IndexVector(1 << axis));
            }
            st.AddColor(leafMax.face(axis).color);
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++) {
                Vector3 cycleVert = CubeUtil.CycleVector(verts[i], 5 - axis);
                uvs[i] = new Vector2(cycleVert.x, cycleVert.y);
            }
            st.AddTriangleFan(verts, uvs);
        } else {
            for (int i = 0; i < 4; i++) {
                int maxI = CubeUtil.CycleIndex(i, axis + 1);
                Cube childMin = cubeMin, childMax = cubeMax;
                if (cubeMin is Cube.BranchImmut branchMin)
                    childMin = branchMin.child(maxI | (1 << axis));
                if (cubeMax is Cube.BranchImmut branchMax)
                    childMax = branchMax.child(maxI);
                var childPos = pos + CubeUtil.IndexVector(maxI) * (size / 2);
                BuildBoundary(st, childMin, childMax, childPos, size / 2, axis, stats);
            }
        }
    }
}
