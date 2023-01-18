using Godot;
using System;
using System.Collections.Generic;

public class CubeMesh : Spatial {
    [NodeRef("Grid")] private MeshInstance nGrid;
    private ArrayMesh mesh = new ArrayMesh();
    private ArrayMesh edgeMesh = new ArrayMesh();
    private ConcavePolygonShape singleShape = new ConcavePolygonShape();
    private ConcavePolygonShape doubleShape = new ConcavePolygonShape();

    private struct MeshData {
        public Dictionary<Guid, SurfaceTool> surfs;
        public SurfaceTool edgeSurf;
        public List<Vector3> singleTris, doubleTris;
    }

    public class CubeStats {
        // leaves = branches * 7 + 1
        public int branches, boundQuads;
    }

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        GetNode<MeshInstance>("MeshInstance").Mesh = mesh;
        nGrid.Mesh = mesh;
        GetNode<MeshInstance>("Edges").Mesh = edgeMesh;
        GetNode<CollisionShape>("SingleSided/CollisionShape").Shape = singleShape;
        GetNode<CollisionShape>("DoubleSided/CollisionShape").Shape = doubleShape;
    }

    public bool GridVisible {
        get => nGrid.Visible;
        set => nGrid.Visible = value;
    }

    public float GridSize {
        get => 1 / ((SpatialMaterial)nGrid.MaterialOverride).Uv1Scale.x;
        set => ((SpatialMaterial)nGrid.MaterialOverride).Uv1Scale = Vector3.One / value;
    }

    public CubeStats UpdateMesh(Cube root, Vector3 pos, float size, Guid voidVolume,
            Dictionary<Guid, Material> materials) {
        ulong startTick = Time.GetTicksMsec();
        mesh.ClearSurfaces();
        edgeMesh.ClearSurfaces();
        var data = new MeshData();
        data.surfs = new Dictionary<Guid, SurfaceTool>();
        data.edgeSurf = new SurfaceTool();
        data.edgeSurf.Begin(Mesh.PrimitiveType.Points);
        data.singleTris = new List<Vector3>();
        data.doubleTris = new List<Vector3>();
        var stats = new CubeStats();

        BuildCube(data, root, pos, size, stats);
        var vLeaf = new Cube.Leaf(voidVolume).Immut();
        for (int axis = 0; axis < 3; axis++) {
            BuildBoundary(data, vLeaf, root, pos, size, axis, stats);
        }
        BuildVertices(data, (vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, root), pos, size);
        GD.Print($"Generating mesh took {Time.GetTicksMsec() - startTick}ms");

        startTick = Time.GetTicksMsec();
        int surfI = 0;
        foreach (var item in data.surfs) {
            item.Value.GenerateTangents(); // TODO necessary?
            item.Value.Index();
            // TODO: this is slow in Godot 3! https://github.com/godotengine/godot/issues/56524
            item.Value.Commit(mesh);
            if (materials.TryGetValue(item.Key, out Material mat))
                mesh.SurfaceSetMaterial(surfI++, mat);
        }
        data.edgeSurf.Index();
        data.edgeSurf.Commit(edgeMesh);
        singleShape.Data = data.singleTris.ToArray();
        doubleShape.Data = data.doubleTris.ToArray();
        GD.Print($"Updating mesh took {Time.GetTicksMsec() - startTick}ms");
        return stats;
    }

    // TODO would the recursive structure in SimplifyCube work better here?
    private static void BuildCube(MeshData data, Cube cube, Vector3 pos, float size,
            CubeStats stats) {
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

    private static void BuildBoundary(MeshData data, Cube cubeMin, Cube cubeMax,
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

    /// <summary>
    /// Add vertices within a cube to the "edges" mesh.
    /// </summary>
    /// <param name="data">Contains the edges mesh to be modified</param>
    /// <param name="cubes">
    /// 8 adjacent cubes in the same order as Cube.Branch. The 7th cube is the one containing the
    /// vertices to be added! Surrounding cubes are required for context.
    /// </param>
    /// <param name="pos">Origin position of the 7th cube.</param>
    /// <param name="size">Size of one of the cubes in the array.</param>
    private static void BuildVertices(MeshData data, Arr8<Cube> cubes, Vector3 pos, float size) {
        if (AllLeaves(cubes, out Arr8<Cube.LeafImmut> leaves) && HasVertex(leaves))
            data.edgeSurf.AddVertex(pos);

        for (int i = 0; i < 8; i++) {
            Arr8<Cube> context = (null, null, null, null, null, null, null, null);
            bool anyBranch = false;
            for (int j = 0; j < 8; j++) {
                Cube cube = cubes[i | j];
                if (cube is Cube.BranchImmut branch) {
                    anyBranch = true;
                    context[j] = branch.child(i ^ j ^ 7);
                } else {
                    context[j] = cube;
                }
            }
            if (anyBranch) {
                var childPos = pos + CubeUtil.IndexVector(i) * (size / 2);
                BuildVertices(data, context, childPos, size / 2);
            }
        }
    }

    private static bool HasVertex(Arr8<Cube.LeafImmut> leaves) {
        bool hasEdgeOneAxis = false;
        for (int axis = 0; axis < 3; axis++) {
            int aBit = 1 << axis;
            // check if there's an edge anywhere along this axis...
            for (int i = 0; i < 4; i++) {
                int here = CubeUtil.CycleIndex(i,     axis + 1);
                int adj1 = CubeUtil.CycleIndex(i ^ 1, axis + 1);
                int adj2 = CubeUtil.CycleIndex(i ^ 2, axis + 1);
                bool minEdge = leaves[here].Val.volume != leaves[adj2].Val.volume
                    && leaves[here].Val.volume != leaves[adj1].Val.volume;
                bool maxEdge = leaves[here | aBit].Val.volume != leaves[adj2 | aBit].Val.volume
                    && leaves[here | aBit].Val.volume != leaves[adj1 | aBit].Val.volume;
                if (minEdge || maxEdge) { // there is!
                    if (hasEdgeOneAxis)
                        return true; // a different axis also had an edge, so there's a corner
                    hasEdgeOneAxis = true;
                    break;
                }
            }
        }
        return false;
    }

    private static bool AllLeaves(Arr8<Cube> cubes, out Arr8<Cube.LeafImmut> leaves) {
        leaves = (null, null, null, null, null, null, null, null);
        for (int i = 0; i < 8; i++) {
            if (cubes[i] is Cube.LeafImmut leaf)
                leaves[i] = leaf;
            else
                return false;
        }
        return true;
    }

    private static void AddQuad(SurfaceTool st, List<Vector3> tris,
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
            Vector3 cycled = CubeUtil.CycleVector(verts[i], 5 - axis);
            (float u, float v) = (axis == 0) ? (-cycled.y, cycled.x) : (cycled.x, cycled.y);
            uvs[i] = new Vector2(dir ? u : -u, -v);
        }
        st.AddTriangleFan(verts, uvs);
        tris.AddRange(new Vector3[] { verts[0], verts[1], verts[2], verts[0], verts[2], verts[3] });
    }
}
