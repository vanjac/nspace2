using Godot;

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
        public Arr8<Cube> children;

        public Branch(Cube all) {
            children = (all, all, all, all, all, all, all, all);
        }

        public BranchImmut Immut() => new BranchImmut(this);
    }

    public struct Leaf {
        public Volume volume;

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

        public Leaf(Volume volume) {
            this.volume = volume;
            faces = (Face.DEFAULT, Face.DEFAULT, Face.DEFAULT);
            split = null;
        }

        public LeafImmut Immut() => new LeafImmut(this);
    }

    public struct SplitLeaf {
        public Volume volume2;
        public Shape shape;
        public byte orientation; // see Godot.GridMap
        public Face splitFace;
    }

    public struct Face {
        public static readonly Immut<Face> DEFAULT = Immut.Create(new Face { material = 0 });
        public int material;
    }

    public enum Volume {
        Empty, Solid, Fluid
    }

    public enum Shape {
        Cube
    }
}

public static class CubeDebug {
    public static int allocCount;
    private static ulong startTick;

    public static void BeginOperation() {
        allocCount = 0;
        startTick = Time.GetTicksMsec();
    }

    public static void EndOperation(string name) {
        GD.Print($"{name} took {Time.GetTicksMsec() - startTick}ms"
            + $" and created {allocCount} cubes");
    }
}
