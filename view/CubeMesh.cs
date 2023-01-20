using Godot;
using System;
using System.Collections.Generic;

public class CubeMesh : Spatial {
    [NodeRef("Grid")] private MeshInstance nGrid;
    private ArrayMesh mesh = new ArrayMesh();
    private ArrayMesh shadowMesh = new ArrayMesh();
    private ArrayMesh edgeMesh = new ArrayMesh();
    private ConcavePolygonShape singleShape = new ConcavePolygonShape();
    private ConcavePolygonShape doubleShape = new ConcavePolygonShape();


    public class CubeStats {
        // leaves = branches * 7 + 1
        public int branches, quads;
    }

    public bool GridVisible {
        get => nGrid.Visible;
        set => nGrid.Visible = value;
    }

    public float GridSize {
        get => 1 / ((SpatialMaterial)nGrid.MaterialOverride).Uv1Scale.x;
        set => ((SpatialMaterial)nGrid.MaterialOverride).Uv1Scale = Vector3.One / value;
    }

    public bool FacesVisible { get; set; } = true;
    public bool EdgeShadowsVisible { get; set; } = true;
    public float EdgeShadowSize { get; set; } = 0.5f;
    public Color EdgeShadowColor { get; set; } = new Color(.5f, .5f, .5f);
    public bool EdgesVisible { get; set; }
    public bool DebugLeavesVisible { get; set; }

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        GetNode<MeshInstance>("MeshInstance").Mesh = mesh;
        nGrid.Mesh = mesh;
        GetNode<MeshInstance>("Shadow").Mesh = shadowMesh;
        GetNode<MeshInstance>("Edges").Mesh = edgeMesh;
        GetNode<CollisionShape>("SingleSided/CollisionShape").Shape = singleShape;
        GetNode<CollisionShape>("DoubleSided/CollisionShape").Shape = doubleShape;
    }

    public CubeStats UpdateMesh(Cube root, Vector3 pos, float size, Guid voidVolume,
            Dictionary<Guid, Material> materials) {
        mesh.ClearSurfaces();
        shadowMesh.ClearSurfaces();
        edgeMesh.ClearSurfaces();
        var stats = new CubeStats();
        var vLeaf = new Cube.Leaf(voidVolume).Immut();

        var matSurfs = new Dictionary<Guid, SurfaceTool>();
        var singleTris = new List<Vector3>();
        var doubleTris = new List<Vector3>();
        ulong startTick = Time.GetTicksMsec();
        for (int axis = 0; axis < 3; axis++) {
            ForEachFace(vLeaf, root, pos, size, axis, (min, max, pos, size) => {
                if (FacesVisible)
                    stats.quads += BuildFaceMesh(min, max, pos, size, axis, matSurfs);
                BuildFaceCollision(min, max, pos, size, axis, singleTris, doubleTris);
            });
        }
        GD.Print($"Generating mesh took {Time.GetTicksMsec() - startTick}ms");

        startTick = Time.GetTicksMsec();
        singleShape.Data = singleTris.ToArray();
        doubleShape.Data = doubleTris.ToArray();
        if (FacesVisible) {
            int surfI = 0;
            foreach (var item in matSurfs) {
                item.Value.GenerateTangents(); // TODO necessary?
                item.Value.Index();
                // TODO: this is slow in Godot 3! https://github.com/godotengine/godot/issues/56524
                item.Value.Commit(mesh);
                if (materials.TryGetValue(item.Key, out Material mat))
                    mesh.SurfaceSetMaterial(surfI++, mat);
            }
        }
        GD.Print($"Updating mesh took {Time.GetTicksMsec() - startTick}ms");

        if (EdgeShadowsVisible) {
            var shadowSurf = new SurfaceTool();
            shadowSurf.Begin(Mesh.PrimitiveType.Triangles);
            for (int axis = 0; axis < 3; axis++) {
                ForEachEdge((vLeaf, vLeaf, vLeaf, root), pos, size, axis, (leaves, pos, size) => {
                    stats.quads += BuildShadowEdge(leaves, pos, size, axis, shadowSurf,
                        EdgeShadowSize, EdgeShadowColor);
                });
            }
            shadowSurf.Index();
            shadowSurf.Commit(shadowMesh);
        }

        if (EdgesVisible) {
            var edgeSurf = new SurfaceTool();
            edgeSurf.Begin(Mesh.PrimitiveType.Lines);
            for (int axis = 0; axis < 3; axis++) {
                ForEachEdge((vLeaf, vLeaf, vLeaf, root), pos, size, axis, (leaves, pos, size) => {
                    if (HasEdge(leaves)) {
                        edgeSurf.AddVertex(pos);
                        edgeSurf.AddVertex(pos + CubeUtil.IndexVector(1 << axis) * size);
                    }
                });
            }

            var vertSurf = new SurfaceTool();
            vertSurf.Begin(Mesh.PrimitiveType.Points);
            ForEachVertex((vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, root), pos, size,
                (leaves, pos) => {
                    if (HasVertex(leaves))
                        vertSurf.AddVertex(pos);
                });

            edgeSurf.Index();
            edgeSurf.Commit(edgeMesh);
            vertSurf.Commit(edgeMesh);
        }

        if (DebugLeavesVisible) {
            var leafSurf = new SurfaceTool();
            leafSurf.Begin(Mesh.PrimitiveType.Lines);
            ForEachLeaf(root, pos, size, (leaf, pos, size) => {
                for (int axis = 0; axis < 3; axis++) {
                    var axisVec = CubeUtil.IndexVector(1 << axis) * size;
                    for (int i = 0; i < 4; i++) {
                        var v = pos + CubeUtil.IndexVector(CubeUtil.CycleIndex(i, axis + 1)) * size;
                        leafSurf.AddVertex(v);
                        leafSurf.AddVertex(v + axisVec);
                    }
                }
            });
            leafSurf.Index();
            leafSurf.Commit(edgeMesh);
        }

        int numLeaves = 0;
        ForEachLeaf(root, pos, size, (leaf, pos, size) => numLeaves++);
        stats.branches = (numLeaves - 1) / 7;

        return stats;
    }

    private static int BuildFaceMesh(Cube.LeafImmut min, Cube.LeafImmut max,
            Vector3 pos, float size, int axis, Dictionary<Guid, SurfaceTool> surfs) {
        if (min.Val.volume == max.Val.volume)
            return 0;
        bool solidBoundary = min.Val.volume == Volume.SOLID || max.Val.volume == Volume.SOLID;
        Cube.Face face = max.face(axis).Val;
        Guid material = (solidBoundary ? face.base_ : face.overlay).material;
        if (!surfs.TryGetValue(material, out SurfaceTool st)) {
            surfs[material] = st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
        }
        int numQuads = 0;
        if (max.Val.volume != Volume.SOLID) {
            AddQuad(st, pos, size, axis, true);
            numQuads++;
        }
        if (min.Val.volume != Volume.SOLID) {
            AddQuad(st, pos, size, axis, false);
            numQuads++;
        }
        return numQuads;
    }

    private static (Vector3, Vector3) FaceTangents(int axis, bool dir, float size) {
        var vS = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 1)) * size;
        var vT = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 2)) * size;
        return dir ? (vS, vT) : (vT, vS);
    }

    private static void AddQuad(SurfaceTool st, Vector3 pos, float size, int axis, bool dir) {
        var (vS, vT) = FaceTangents(axis, dir, size);
        var verts = new Vector3[] { pos, pos + vT, pos + vS + vT, pos + vS };
        st.AddNormal(CubeUtil.IndexVector(1 << axis) * (dir ? 1 : -1));
        var uvs = new Vector2[4];
        for (int i = 0; i < 4; i++) {
            Vector3 cycled = CubeUtil.CycleVector(verts[i], 5 - axis);
            (float u, float v) = (axis == 0) ? (-cycled.y, cycled.x) : (cycled.x, cycled.y);
            uvs[i] = new Vector2(dir ? u : -u, -v);
        }
        st.AddTriangleFan(verts, uvs);
    }

    private static void BuildFaceCollision(Cube.LeafImmut min, Cube.LeafImmut max,
            Vector3 pos, float size, int axis, List<Vector3> singleTris, List<Vector3> doubleTris) {
        if (min.Val.volume == max.Val.volume)
            return;
        bool solidBoundary = min.Val.volume == Volume.SOLID || max.Val.volume == Volume.SOLID;
        var tris = solidBoundary ? singleTris : doubleTris;
        var (vS, vT) = FaceTangents(axis, max.Val.volume != Volume.SOLID, size);
        tris.AddRange(new Vector3[] { pos, pos + vT, pos + vS + vT, pos, pos + vS + vT, pos + vS });
    }

    private static int BuildShadowEdge(Arr4<Cube.LeafImmut> leaves,
            Vector3 pos, float size, int axis, SurfaceTool surf, float width, Color color) {
        int numQuads = 0;
        for (int i = 0; i < 4; i++) {
            int adj1 = i ^ 1;
            int adj2 = i ^ 2;
            if (leaves[i].Val.volume != Volume.SOLID
                    && leaves[adj1].Val.volume == Volume.SOLID
                    && leaves[adj2].Val.volume == Volume.SOLID) {
                var vEdge = CubeUtil.IndexVector(1 << axis) * size;
                var vS = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 1)) * width;
                if ((i & 1) == 0) vS = -vS;
                var vT = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 2)) * width;
                if ((i & 2) == 0) vT = -vT;
                if (((i & 1) ^ ((i >> 1) & 1)) == 0) (vS, vT) = (vT, vS); // TODO jank
                surf.AddNormal(vS.Cross(vEdge).Normalized()); // TODO use IndexVector
                surf.AddTriangleFan(new Vector3[] { pos, pos + vEdge, pos + vEdge + vS, pos + vS },
                    colors: new Color[] {color, color, Colors.White, Colors.White });
                surf.AddNormal(vEdge.Cross(vT).Normalized());
                surf.AddTriangleFan(new Vector3[] { pos, pos + vT, pos + vEdge + vT, pos + vEdge },
                    colors: new Color[] {color, Colors.White, Colors.White, color });
                numQuads += 2;
            }
        }
        return numQuads;
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

    private delegate void FaceCallback(Cube.LeafImmut min, Cube.LeafImmut max,
            Vector3 pos, float size);

    private static void ForEachFace(Cube cubeMin, Cube cubeMax, Vector3 pos, float size, int axis,
            FaceCallback callback) {
        if (cubeMin is Cube.LeafImmut leafMin && cubeMax is Cube.LeafImmut leafMax) {
            callback(leafMin, leafMax, pos, size);
        } else {
            for (int i = 0; i < 4; i++) {
                int childI = CubeUtil.CycleIndex(i, axis + 1);
                var childPos = pos + CubeUtil.IndexVector(childI) * (size / 2);
                Cube childMin = cubeMin, childMax = cubeMax;
                if (cubeMin is Cube.BranchImmut branchMin)
                    childMin = branchMin.child(childI | (1 << axis));
                if (cubeMax is Cube.BranchImmut branchMax) {
                    childMax = branchMax.child(childI);
                    var childPosMax = childPos + CubeUtil.IndexVector(1 << axis) * (size / 2);
                    ForEachFace(childMax, branchMax.child(childI | (1 << axis)),
                        childPosMax, size / 2, axis, callback);
                }
                ForEachFace(childMin, childMax, childPos, size / 2, axis, callback);
            }
        }
    }

    private delegate void EdgeCallback(Arr4<Cube.LeafImmut> leaves, Vector3 pos, float size);

    private static void ForEachEdge(Arr4<Cube> cubes, Vector3 pos, float size, int axis,
            EdgeCallback callback) {
        if (AllLeaves(cubes, out Arr4<Cube.LeafImmut> leaves)) {
            callback(leaves, pos, size);
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
                    ForEachEdge(adjacent, childPos, size / 2, axis, callback);
                }
            }
        }
    }

    private delegate void VertexCallback(Arr8<Cube.LeafImmut> leaves, Vector3 pos);

    private static void ForEachVertex(Arr8<Cube> cubes, Vector3 pos, float size,
            VertexCallback callback) {
        if (AllLeaves(cubes, out Arr8<Cube.LeafImmut> leaves)) {
            callback(leaves, pos);
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
                    ForEachVertex(adjacent, childPos, size / 2, callback);
                }
            }
        }
    }

    private delegate void LeafCallback(Cube.LeafImmut leaf, Vector3 pos, float size);

    private static void ForEachLeaf(Cube cube, Vector3 pos, float size, LeafCallback callback) {
        if (cube is Cube.LeafImmut leaf) {
            callback(leaf, pos, size);
        } else {
            var branch = cube as Cube.BranchImmut;
            for (int i = 0; i < 8; i++) {
                var childPos = pos + CubeUtil.IndexVector(i) * (size / 2);
                ForEachLeaf(branch.child(i), childPos, size / 2, callback);
            }
        }
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
}
