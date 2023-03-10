using Godot;
using System;
using System.Collections.Generic;

public class CubeMesh : Spatial {
    [NodeRef("Grid")] private MeshInstance nGrid = null;
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
    public bool EdgesVisible { get; set; }
    public bool DebugLeavesVisible { get; set; }
    public bool ShowInvisible { get; set; } = true;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        GetNode<MeshInstance>("MeshInstance").Mesh = mesh;
        nGrid.Mesh = mesh;
        GetNode<MeshInstance>("ShadowFront").Mesh = shadowMesh;
        GetNode<MeshInstance>("ShadowBack").Mesh = shadowMesh;
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
        Arr4<Cube> rootCubes4 = (vLeaf, vLeaf, vLeaf, root);
        Arr8<Cube> rootCubes8 = (vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, vLeaf, root);

        var matSurfs = new Dictionary<Guid, SurfaceTool>();
        var singleTris = new List<Vector3>();
        var doubleTris = new List<Vector3>();
        for (int axis = 0; axis < 3; axis++) {
            ForEachFace(vLeaf, root, pos, size, axis, (min, max, pos, size) => {
                if (FacesVisible)
                    stats.quads += BuildFaceMesh(min, max, pos, size, axis, matSurfs);
                BuildFaceCollision(min, max, pos, size, axis, singleTris, doubleTris);
            });
        }

        singleShape.Data = singleTris.ToArray();
        doubleShape.Data = doubleTris.ToArray();
        if (FacesVisible) {
            int surfI = 0;
            foreach (var item in matSurfs) {
                item.Value.GenerateTangents(); // TODO necessary?
                item.Value.Index();
                // TODO: this is slow in Godot 3! https://github.com/godotengine/godot/issues/56524
                item.Value.Commit(mesh);
                if (materials.TryGetValue(item.Key, out var mat))
                    mesh.SurfaceSetMaterial(surfI++, mat);
            }
        }

        if (EdgeShadowsVisible) {
            var shadowSurf = new SurfaceTool();
            shadowSurf.Begin(Mesh.PrimitiveType.Triangles);
            for (int axis = 0; axis < 3; axis++) {
                ForEachEdge(rootCubes4, pos, size, axis, (leaves, pos, size) => {
                    stats.quads += BuildShadowEdge(leaves, pos, size, axis, shadowSurf);
                });
            }
            ForEachVertex(rootCubes8, pos, size, (leaves, pos) => {
                stats.quads += BuildShadowVertex(leaves, pos, shadowSurf);
            });
            shadowSurf.Index();
            shadowSurf.Commit(shadowMesh);
        }

        if (EdgesVisible) {
            var edgeSurf = new SurfaceTool();
            edgeSurf.Begin(Mesh.PrimitiveType.Lines);
            for (int axis = 0; axis < 3; axis++) {
                ForEachEdge(rootCubes4, pos, size, axis, (leaves, pos, size) => {
                    if (HasEdge(leaves)) {
                        edgeSurf.AddVertex(pos);
                        edgeSurf.AddVertex(pos + CubeUtil.IndexVector(1 << axis) * size);
                    }
                });
            }

            var vertSurf = new SurfaceTool();
            vertSurf.Begin(Mesh.PrimitiveType.Points);
            ForEachVertex(rootCubes8, pos, size, (leaves, pos) => {
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

    private int BuildFaceMesh(Cube.LeafImmut min, Cube.LeafImmut max,
            Vector3 pos, float size, int axis, Dictionary<Guid, SurfaceTool> surfs) {
        if (min.Val.volume == max.Val.volume)
            return 0;
        var face = max.face(axis).Val;
        int numQuads = 0;
        if (min.Val.volume == CubeVolume.SOLID || max.Val.volume == CubeVolume.SOLID) {
            bool dir = min.Val.volume == CubeVolume.SOLID;
            AddQuad(pos, size, axis, dir, face.base_, surfs);
            numQuads = 1;
            if (face.overlay.material != CubeMaterial.INVISIBLE) {
                AddQuad(pos, size, axis, dir, face.overlay, surfs);
                numQuads = 2;
            }
        } else if (face.overlay.material != CubeMaterial.INVISIBLE || ShowInvisible) {
            AddQuad(pos, size, axis, true, face.overlay, surfs);
            AddQuad(pos, size, axis, false, face.overlay, surfs);
            numQuads = 2;
        }
        return numQuads;
    }

    private static (Vector3, Vector3) FaceTangents(int axis, bool dir, float size) {
        var vS = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 1)) * size;
        var vT = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 2)) * size;
        return dir ? (vS, vT) : (vT, vS);
    }

    private static void AddQuad(Vector3 pos, float size, int axis, bool dir, Cube.Layer layer,
            Dictionary<Guid, SurfaceTool> surfs) {
        if (!surfs.TryGetValue(layer.material, out SurfaceTool st)) {
            surfs[layer.material] = st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
        }
        var (vS, vT) = FaceTangents(axis, dir, size);
        var verts = new Vector3[] { pos, pos + vT, pos + vS + vT, pos + vS };
        st.AddNormal(CubeUtil.IndexVector(1 << axis) * (dir ? 1 : -1));
        var orient = layer.orient;
        var uvs = new Vector2[4];
        for (int i = 0; i < 4; i++) {
            Vector3 cycled = CubeUtil.CycleVector(verts[i], 5 - axis);
            (float u, float v) = (axis == 0) ? (-cycled.y, -cycled.x) : (cycled.x, -cycled.y);
            if (!dir) u = -u;
            u += CubeModel.SCALE * layer.uOffset;
            v += CubeModel.SCALE * layer.vOffset;
            if ((orient & Cube.Orient.SWAP_UV) != 0) (u, v) = (v, u);
            if ((orient & Cube.Orient.FLIP_U) != 0) u = -u;
            if ((orient & Cube.Orient.FLIP_V) != 0) v = -v;
            uvs[i] = new Vector2(u, v);
        }
        st.AddTriangleFan(verts, uvs);
    }

    private void BuildFaceCollision(Cube.LeafImmut min, Cube.LeafImmut max,
            Vector3 pos, float size, int axis, List<Vector3> singleTris, List<Vector3> doubleTris) {
        if (min.Val.volume == max.Val.volume)
            return;
        bool solidBoundary = min.Val.volume == CubeVolume.SOLID
            || max.Val.volume == CubeVolume.SOLID;
        var tris = solidBoundary ? singleTris : doubleTris;
        var (vS, vT) = FaceTangents(axis, max.Val.volume != CubeVolume.SOLID, size);
        tris.AddRange(new Vector3[] { pos, pos + vT, pos + vS + vT, pos, pos + vS + vT, pos + vS });
    }

    private static readonly Vector2[] EDGE_SHADOW_UVS1 =
        new Vector2[] { Vector2.Zero, Vector2.Zero, Vector2.Right, Vector2.Right };
    private static readonly Vector2[] EDGE_SHADOW_UVS2 =
        new Vector2[] { Vector2.Zero, Vector2.Right, Vector2.Right, Vector2.Zero };
    private static readonly Vector2[] VERTEX_SHADOW_UVS =
        new Vector2[] { Vector2.Zero, Vector2.Right, new Vector2(1.414f, 0), Vector2.Right };

    private int BuildShadowEdge(Arr4<Cube.LeafImmut> leaves,
            Vector3 pos, float size, int axis, SurfaceTool surf) {
        int numQuads = 0;
        var lightMat = CubeMaterial.LIGHT;
        for (int i = 0; i < 4; i++) {
            int adj1 = i ^ 1;
            int adj2 = i ^ 2;
            if (leaves[i].Val.volume != CubeVolume.SOLID
                    && leaves[adj1].Val.volume == CubeVolume.SOLID
                    && leaves[adj2].Val.volume == CubeVolume.SOLID
                    && leaves[i | 1].face((axis + 1) % 3).Val.base_.material != lightMat
                    && leaves[i | 2].face((axis + 2) % 3).Val.base_.material != lightMat) {
                var vEdge = CubeUtil.IndexVector(1 << axis) * size;
                var vS = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 1)) * EdgeShadowSize;
                if ((i & 1) == 0) vS = -vS;
                var vT = CubeUtil.IndexVector(CubeUtil.CycleIndex(1, axis + 2)) * EdgeShadowSize;
                if ((i & 2) == 0) vT = -vT;
                if (((i & 1) ^ ((i >> 1) & 1)) == 0) (vS, vT) = (vT, vS); // TODO jank
                Vector2 dark = new Vector2(0, 0), light = new Vector2(1, 0);
                surf.AddNormal(vS.Cross(vEdge).Normalized()); // TODO use IndexVector
                surf.AddTriangleFan(new Vector3[] { pos, pos + vEdge, pos + vEdge + vS, pos + vS },
                    uvs: EDGE_SHADOW_UVS1);
                surf.AddNormal(vEdge.Cross(vT).Normalized());
                surf.AddTriangleFan(new Vector3[] { pos, pos + vT, pos + vEdge + vT, pos + vEdge },
                    uvs: EDGE_SHADOW_UVS2);
                numQuads += 2;
            }
        }
        return numQuads;
    }

    private int BuildShadowVertex(Arr8<Cube.LeafImmut> leaves, Vector3 pos, SurfaceTool surf) {
        int numQuads = 0;
        var lightMat = CubeMaterial.LIGHT;
        for (int i = 0; i < 8; i++) {
            if (leaves[i].Val.volume == CubeVolume.SOLID
                    && leaves[i ^ 7].Val.volume == CubeVolume.SOLID) {
                for (int axis = 0; axis < 3; axis++) {
                    int sAxis = (axis + 1) % 3, tAxis = (axis + 2) % 3;
                    int p = 1 << axis, s = 1 << sAxis, t = 1 << tAxis;
                    if (leaves[i ^ p].Val.volume != CubeVolume.SOLID
                            && leaves[i ^ p ^ s].Val.volume != CubeVolume.SOLID
                            && leaves[i ^ p ^ t].Val.volume != CubeVolume.SOLID
                            && leaves[i | p].face(axis).Val.base_.material != lightMat
                            && leaves[(i ^ p ^ s) | t].face(tAxis).Val.base_.material != lightMat
                            && leaves[(i ^ p ^ t) | s].face(sAxis).Val.base_.material != lightMat) {
                        var vS = CubeUtil.IndexVector(s) * EdgeShadowSize;
                        if ((i & s) == 0) vS = -vS;
                        var vT = CubeUtil.IndexVector(t) * EdgeShadowSize;
                        if ((i & t) == 0) vT = -vT;
                        if (((i ^ (i >> 1) ^ (i >> 2)) & 1) != 0)
                            (vS, vT) = (vT, vS);
                        surf.AddNormal(vS.Cross(vT).Normalized()); // TODO use IndexVector
                        surf.AddTriangleFan(
                            new Vector3[] { pos, pos + vT, pos + vT + vS, pos + vS },
                            uvs: VERTEX_SHADOW_UVS);
                        numQuads++;
                    }
                }
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
