using System;

/// <summary>
/// An immutable node in a voxel octree.
/// May be either a branch (Cube.BranchImmut) or a leaf (Cube.LeafImmut).
/// </summary>
public interface Cube {
    public class BranchImmut : Immut<Branch>, Cube {
        public BranchImmut(Branch val) : base(val) { CubeDebug.allocCount++; }
        public Cube child(int i) => Val.children[i];
    }
    public class LeafImmut : Immut<Leaf>, Cube {
        public LeafImmut(Leaf val) : base(val) { CubeDebug.allocCount++; }
        public Immut<Face> face(int i) => Val.faces[i];
    }

    public struct Branch {
        /// <summary>
        /// Branches are divided into 8 equally-sized child cubes along the 3 axis-aligned planes.
        /// Children are ordered based on which side of these planes they are on: negative first,
        /// then positive, with the Z plane taking highest precedence, then Y, then X.
        /// (-Z-Y-X, -Z-Y+X, -Z+Y-X, -Z+Y+X, +Z-Y-X, +Z-Y+X, +Z+Y-X, +Z+Y+X)
        /// </summary>
        [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.RootHidden)]
        public Arr8<Cube> children;

        public Branch(Cube all) {
            children = (all, all, all, all, all, all, all, all);
        }

        public BranchImmut Immut() => new BranchImmut(this);
    }

    public struct Leaf {
        public Guid volume;

        /// <summary>
        /// 3 faces of the cube on the negative X, negative Y, and negative Z sides.
        /// The other 3 sides are stored in adjacent cubes.
        /// </summary>
        public Arr3<Immut<Face>> faces;

        /// <summary>
        /// Splits the leaf into 2 volumes along some non-axis-aligned boundary.
        /// Used to create slopes and other geometry that can't be represented as cubes.
        /// Normally null.
        /// </summary>
        public Immut<SplitLeaf> split;

        public Leaf(Guid volume) {
            this.volume = volume;
            faces = (Face.DEFAULT, Face.DEFAULT, Face.DEFAULT);
            split = null;
        }

        public LeafImmut Immut() => new LeafImmut(this);
    }

    public struct SplitLeaf {
        public Guid volume2;
        public Shape shape;
        public byte orientation; // see Godot.GridMap
        public Face splitFace;
    }

    public struct Face {
        public static readonly Immut<Face> DEFAULT = Immut.Create(new Face {
            base_ = new Layer { material = CubeMaterial.DEFAULT_BASE },
            overlay = new Layer { material = CubeMaterial.DEFAULT_OVERLAY }
        });

        public Layer base_, overlay;
    }

    public struct Layer {
        public Guid material;
        public byte orientation;
        public byte uOffset, vOffset;
    }

    public enum Shape {
        Cube
    }
}

public static class CubeMaterial {
    public static readonly Guid DEFAULT_BASE = new Guid("ee705545-80fe-4e67-a2b5-bf099e40f5e3");
    public static readonly Guid DEFAULT_OVERLAY = new Guid("0bbe0d1b-b713-424b-9fe8-1061b9f71d0e");
}

public static class Volume {
    public static readonly Guid EMPTY = new Guid("e6f63db0-ae21-4237-a8a0-96ce77ef5e75");
    public static readonly Guid SOLID = new Guid("963d3459-35c5-4460-a674-7779799366dc");
}

public static class CubeDebug {
    public static int allocCount;
}
