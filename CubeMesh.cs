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
        data.singleTris = new List<Vector3>();
        data.doubleTris = new List<Vector3>();
        var edgeSurf = new SurfaceTool();
        edgeSurf.Begin(Mesh.PrimitiveType.Lines);
        var vertSurf = new SurfaceTool();
        vertSurf.Begin(Mesh.PrimitiveType.Points);
        var stats = new CubeStats();

        var vLeaf = new Cube.Leaf(voidVolume).Immut();
        for (int axis = 0; axis < 3; axis++) {
            stats.boundQuads += BuildFaces(data, vLeaf, root, pos, size, axis);
            BuildEdges(edgeSurf, (vLeaf, vLeaf, vLeaf, root), pos, size, axis);
        }
        BuildVertices(vertSurf, (vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, root), pos, size);
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
        edgeSurf.Index();
        edgeSurf.Commit(edgeMesh);
        vertSurf.Commit(edgeMesh);
        singleShape.Data = data.singleTris.ToArray();
        doubleShape.Data = data.doubleTris.ToArray();
        GD.Print($"Updating mesh took {Time.GetTicksMsec() - startTick}ms");
        return stats;
    }

    private static int BuildFaces(MeshData data, Cube cubeMin, Cube cubeMax,
            Vector3 pos, float size, int axis) {
        int numQuads = 0;
        if (cubeMin is Cube.LeafImmut leafMin && cubeMax is Cube.LeafImmut leafMax) {
            if (leafMin.Val.volume == leafMax.Val.volume)
                return numQuads;
            Cube.Face face = leafMax.face(axis).Val;
            bool solidBoundary = leafMin.Val.volume == Volume.SOLID
                || leafMax.Val.volume == Volume.SOLID;
            Guid material = (solidBoundary ? face.base_ : face.overlay).material;
            if (!data.surfs.TryGetValue(material, out SurfaceTool st)) {
                data.surfs[material] = st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
            }
            var tris = solidBoundary ? data.singleTris : data.doubleTris;
            if (leafMax.Val.volume != Volume.SOLID) {
                AddQuad(st, tris, pos, size, axis, true);
                numQuads++;
            }
            if (leafMin.Val.volume != Volume.SOLID) {
                AddQuad(st, tris, pos, size, axis, false);
                numQuads++;
            }
        } else {
            for (int i = 0; i < 4; i++) {
                int childI = CubeUtil.CycleIndex(i, axis + 1);
                var childPos = pos + CubeUtil.IndexVector(childI) * (size / 2);
                Cube childMin = cubeMin, childMax = cubeMax;
                if (cubeMin is Cube.BranchImmut branchMin)
                    childMin = branchMin.child(childI | (1 << axis));
                if (cubeMax is Cube.BranchImmut branchMax) {
                    childMax = branchMax.child(childI);
                    numQuads += BuildFaces(data, childMax, branchMax.child(childI | (1 << axis)),
                        childPos + CubeUtil.IndexVector(1 << axis) * (size / 2), size / 2, axis);
                }
                numQuads += BuildFaces(data, childMin, childMax, childPos, size / 2, axis);
            }
        }
        return numQuads;
    }

    private static void BuildEdges(SurfaceTool surf, Arr4<Cube> cubes,
            Vector3 pos, float size, int axis) {
        if (AllLeaves(cubes, out Arr4<Cube.LeafImmut> leaves)) {
            if (HasEdge(leaves)) {
                surf.AddVertex(pos);
                surf.AddVertex(pos + CubeUtil.IndexVector(1 << axis) * size);
            }
        } else {
            var aBit = 1 << axis;
            for (int i = 0; i < 8; i++) {
                int childI = CubeUtil.CycleIndex(i, axis + 1); // child of cube[7]
                var adjacent = new Arr4<Cube>();
                bool anyBranch = false;
                for (int j = 0; j < 4; j++) {
                    Cube cube = cubes[(i & 3) | j];
                    if (cube is Cube.BranchImmut branch) {
                        anyBranch = true;
                        int adjI = childI ^ 7 ^ aBit ^ CubeUtil.CycleIndex(j, axis + 1);
                        adjacent[j] = branch.child(adjI);
                    } else {
                        adjacent[j] = cube;
                    }
                }
                if (anyBranch) {
                    var childPos = pos + CubeUtil.IndexVector(childI) * (size / 2);
                    BuildEdges(surf, adjacent, childPos, size / 2, axis);
                }
            }
        }
    }

    /// <summary>
    /// Add vertices within a cube to the surface.
    /// </summary>
    /// <param name="surf">Surface to be modified.</param>
    /// <param name="cubes">
    /// 8 adjacent cubes in the same order as Cube.Branch. The 7th cube is the one containing the
    /// vertices to be added! Surrounding cubes are required for context.
    /// </param>
    /// <param name="pos">Origin position of the 7th cube.</param>
    /// <param name="size">Size of one of the cubes in the array.</param>
    private static void BuildVertices(SurfaceTool surf, Arr8<Cube> cubes, Vector3 pos, float size) {
        if (AllLeaves(cubes, out Arr8<Cube.LeafImmut> leaves)) {
            if (HasVertex(leaves))
                surf.AddVertex(pos);
        } else {
            for (int i = 0; i < 8; i++) { // index into cubes[7]
                var adjacent = new Arr8<Cube>();
                bool anyBranch = false;
                for (int j = 0; j < 8; j++) {
                    Cube cube = cubes[i | j];
                    if (cube is Cube.BranchImmut branch) {
                        anyBranch = true;
                        adjacent[j] = branch.child(i ^ j ^ 7);
                    } else {
                        adjacent[j] = cube;
                    }
                }
                if (anyBranch) {
                    var childPos = pos + CubeUtil.IndexVector(i) * (size / 2);
                    BuildVertices(surf, adjacent, childPos, size / 2);
                }
            }
        }
    }

    private static bool HasEdge(Arr4<Cube.LeafImmut> leaves) {
        for (int i = 0; i < 4; i++) {
            int adj1 = i ^ 1;
            int adj2 = i ^ 2;
            if (leaves[i].Val.volume != leaves[adj1].Val.volume
                    && leaves[i].Val.volume != leaves[adj2].Val.volume)
                return true;
        }
        return false;
    }

    private static bool HasVertex(Arr8<Cube.LeafImmut> leaves) {
        bool hasEdgeOneAxis = false;
        for (int axis = 0; axis < 3; axis++) {
            var minLeaves = new Arr4<Cube.LeafImmut>();
            var maxLeaves = new Arr4<Cube.LeafImmut>();
            for (int i = 0; i < 4; i++) {
                int leafI = CubeUtil.CycleIndex(i, axis + 1);
                minLeaves[i] = leaves[leafI];
                maxLeaves[i] = leaves[leafI | (1 << axis)];
            }
            if (HasEdge(minLeaves) || HasEdge(maxLeaves)) {
                if (hasEdgeOneAxis)
                    return true; // a different axis also had an edge, so there's a corner
                hasEdgeOneAxis = true;
            }
        }
        return false;
    }

    private static bool AllLeaves(Arr4<Cube> cubes, out Arr4<Cube.LeafImmut> leaves) {
        leaves = new Arr4<Cube.LeafImmut>();
        for (int i = 0; i < 4; i++) {
            if (!(cubes[i] is Cube.LeafImmut leaf))
                return false;
            leaves[i] = leaf;
        }
        return true;
    }

    private static bool AllLeaves(Arr8<Cube> cubes, out Arr8<Cube.LeafImmut> leaves) {
        leaves = new Arr8<Cube.LeafImmut>();
        for (int i = 0; i < 8; i++) {
            if (!(cubes[i] is Cube.LeafImmut leaf))
                return false;
            leaves[i] = leaf;
        }
        return true;
    }

    private static void AddQuad(SurfaceTool st, List<Vector3> tris,
            Vector3 pos, float size, int axis, bool dir) {
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
