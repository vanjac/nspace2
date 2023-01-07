using Godot;
using System;

public static class CubeUtil {
    /// <summary>
    /// Rotate a 3-bit branch child index left. Each bit corresponds to an axis (1 = positive).
    /// </summary>
    /// <param name="index">The 3-bit index.</param>
    /// <param name="count">Number of bits left to rotate.</param>
    /// <returns>The index, with its bits rearranged.</returns>
    public static int CycleIndex(int index, int count) {
        return ((index & 1) << (count % 3))
            | (((index >> 1) & 1) << ((count + 1) % 3))
            | (((index >> 2) & 1) << ((count + 2) % 3));
    }

    /// <summary>
    /// Rearrange the components of a vector in the same way that CycleIndex() rearranges bits.
    /// Eg. cycling a vector by 1 will move the X component to Y, Y to Z, and Z to X.
    /// </summary>
    /// <param name="vec">Vector to cycle.</param>
    /// <param name="count">Number of positions to move each component.</param>
    /// <returns>The vector with its components rearranged.</returns>
    public static Vector3 CycleVector(Vector3 vec, int count) {
        Vector3 newVec = new Vector3();
        newVec[count % 3] = vec.x;
        newVec[(count + 1) % 3] = vec.y;
        newVec[(count + 2) % 3] = vec.z;
        return newVec;
    }

    /// <summary>
    /// Construct a vector with each component determined by a bit in the index (0 or 1).
    /// Used to determine the position of a child within its parent branch.
    /// </summary>
    /// <param name="index">3-bit child index whose bits are used to build the vector.</param>
    /// <returns>A vector (bit 0, bit 1, bit 2).</returns>
    public static Vector3 IndexVector(int index) {
        return new Vector3(index & 1, (index >> 1) & 1, (index >> 2) & 1);
    }

    /// <summary>
    /// Find the size of a cube at the given depth in an octree.
    /// </summary>
    /// <param name="depth">Depth of the cube in the tree.</param>
    /// <param name="rootSize">Size of the root node of the tree.</param>
    /// <returns></returns>
    public static float CubeSize(int depth, float rootSize = 1.0f) {
        if (depth >= 0)
            return rootSize / (1 << depth);
        else
            return rootSize * (1 << -depth);
    }

    /// <summary>
    /// Map a world-space vector to the range (0,0,0) -> (1,1,1), corresponding to the minimum and
    /// maximum bounds of the world (world.rootPos is the origin). Vectors outside the bounds of the
    /// world will be outside this range.
    /// </summary>
    /// <param name="pos">Vector in world coordinates.</param>
    /// <param name="world">The world whose root position/size is used to map the vector.</param>
    /// <returns>Vector representing a fraction of the root cube of the world.</returns>
    public static Vector3 ToUnitPos(Vector3 pos, CubeWorld world) {
        return (pos - world.rootPos) / world.rootSize;
    }

    /// <summary>
    /// The reverse of ToUnitPos(). Map the range (0,0,0) -> (1,1,1) to the minimum and maximum
    /// bounds of the world.
    /// </summary>
    /// <param name="unit">Vector representing a fraction of the root cube of the world.</param>
    /// <param name="world">The world whose root position/size is used to map the vector.</param>
    /// <returns>Vector in world coordinates.</returns>
    public static Vector3 ToWorldPos(Vector3 unit, CubeWorld world) {
        return unit * world.rootSize + world.rootPos;
    }

    /// <summary>
    /// Determine the cube face selected by the user.
    /// </summary>
    /// <param name="pos">Raycast collision point (unit position).</param>
    /// <param name="normal">Raycast collision normal.</param>
    /// <param name="depth">Depth in tree of cubes to select.</param>
    /// <param name="axis">Axis of the face that was selected.</param>
    /// <param name="dir">
    /// True if the selected face's normal is in the positive direction, false if negative.
    /// </param>
    /// <returns>A point inside the cube that was selected.</returns>
    public static Vector3 PickFace(Vector3 pos, Vector3 normal, int depth,
            out int axis, out bool dir) {
        var absNormal = normal.Abs();
        axis = (int)absNormal.MaxAxis();
        dir = normal[axis] >= 0;
        // TODO selecting half size cube??
        return pos + absNormal.Round() * CubeUtil.CubeSize(depth + 1);
    }

    /// <summary>
    /// Transform point in the space of a descendant cube to the space of its ancestor.
    /// </summary>
    /// <param name="p">Point in descendant's coordinates.</param>
    /// <param name="descPos">Position of the descendant cube within the ancestor.</param>
    /// <param name="depth">Depth of the descendant cube.</param>
    /// <returns>Point in ancestor's coordinates.</returns>
    public static Vector3 ToAncestorPos(Vector3 p, CubePos descPos, int depth) {
        return p / (1 << depth) + descPos.ToUnitPos();
    }
}

/// <summary>
/// A 3D point inside a cube (always within its bounds).
/// Each coordinate is a 32-bit unsigned integer. The entire range is used, so 0 is the minimum
/// coordinate and 2^32 (not representable) is the maximum.
/// </summary>
public struct CubePos {
    private const float UNIT = 4294967296.0f; // 2 ^ 32
    private uint x, y, z;

    public CubePos(uint x, uint y, uint z) {
        this.x = x; this.y = y; this.z = z;
    }

    public CubePos(Vector3 v) {
        this.x = (uint)(long)v.x; this.y = (uint)(long)v.y; this.z = (uint)(long)v.z;
    }

    public uint this[int i] {
        readonly get {
            switch (i % 3) {
                case 0: return x;
                case 1: return y;
                case 2: return z;
                default: throw new IndexOutOfRangeException();
            }
        }
        set {
            switch (i % 3) {
                case 0: x = value; break;
                case 1: y = value; break;
                case 2: z = value; break;
                default: throw new IndexOutOfRangeException();
            }
        }
    }

    /// <summary>
    /// Get the size of a cube in the range of CubePos coordinates.
    /// </summary>
    /// <param name="depth">Depth of the cube relative to root.</param>
    /// <returns>Size of the cube relative to root.</returns>
    public static uint CubeSize(int depth) {
        return 1u << (32 - depth);
    }

    /// <summary>
    /// Build a CubePos by setting a single axis to a value, and all others to 0.
    /// </summary>
    /// <param name="axis">Axis index to be set.</param>
    /// <param name="len">Axis value to set.</param>
    /// <param name="dir">If false, value will be made negative (2's complement).</param>
    /// <returns>A CubePos with a single axis set.</returns>
    public static CubePos Axis(int axis, uint len = 1, bool dir = true) {
        CubePos pos = new CubePos(0, 0, 0);
        pos[axis] = dir ? len : (uint)-len;
        return pos;
    }

    /// <summary>
    /// Build a CubePos from a vector in unit coordinates (0 to 1).
    /// </summary>
    /// <param name="v">Vector between (0,0,0) and (1,1,1).</param>
    /// <returns>The vector scaled to the range of CubePos coordinates.</returns>
    public static CubePos FromUnitPos(Vector3 v) {
        return new CubePos(v * UNIT);
    }

    /// <summary>
    /// Convert to a vector with coordinates in the range 0 to 1.
    /// </summary>
    /// <returns>Vector between (0,0,0) and (1,1,1).</returns>
    public readonly Vector3 ToUnitPos() {
        return new Vector3(x, y, z) / UNIT;
    }

    /// <summary>
    /// Round each coordinate down to intervals the size of a descendant cube.
    /// </summary>
    /// <param name="depth">Depth of the descendent cube.</param>
    /// <returns>CubePos where each coordinate is some multiple of CubeSize(depth).</returns>
    public readonly CubePos Floor(int depth) {
        uint mask = ~0u << (32 - depth);
        return new CubePos(x & mask, y & mask, z & mask);
    }

    /// <summary>
    /// Get the index of the child of a branch that would contain this CubePos.
    /// </summary>
    /// <returns>Child index (0-7).</returns>
    public readonly int ChildIndex() {
        return (int)(((x >> 31) & 1) | ((y >> 30) & 2) | ((z >> 29) & 4));
    }

    /// <summary>
    /// Transform point in the space of a parent cube to the space of one of its children
    /// (whichever child contains the point, given by ChildIndex()).
    /// </summary>
    /// <returns>CubePos in child's coordinates.</returns>
    public readonly CubePos ToChild() {
        return ToDescendant(1);
    }

    /// <summary>
    /// Transform point in the space of an ancestor cube to the space of one of its descendants.
    /// </summary>
    /// <param name="depth">Depth of the descendant cube.</param>
    /// <returns>CubePos in descendant's coordinates.</returns>
    public readonly CubePos ToDescendant(int depth) {
        return new CubePos(x << depth, y << depth, z << depth);
    }

    /// <summary>
    /// Transform point in the space of a child cube to the space of its parent.
    /// </summary>
    /// <param name="childI">Index of the child in the parent branch.</param>
    /// <returns>CubePos in parent's coordinates.</returns>
    public readonly CubePos ToParent(int childI) {
        return new CubePos(
            (x >> 1) | (((uint)childI & 1) << 31),
            (y >> 1) | (((uint)childI & 2) << 30),
            (z >> 1) | (((uint)childI & 4) << 29));
    }

    /// <summary>
    /// Transform point in the space of a descendant cube to the space of its ancenstor.
    /// See also CubeUtil.ToAncestorPos().
    /// </summary>
    /// <param name="descPos">Position of the descendant cube within the ancestor.</param>
    /// <param name="depth">Depth of the descendant cube.</param>
    /// <returns>CubePos in ancestor's coordinates.</returns>
    public readonly CubePos ToAncestor(CubePos descPos, int depth) {
        return new CubePos(x >> depth, y >> depth, z >> depth) + descPos;
    }

    public readonly override string ToString() {
        return $"<{x:X8}, {y:X8}, {z:X8}>";
    }

    public static CubePos operator +(CubePos a, CubePos b)
        => new CubePos(a.x + b.x, a.y + b.y, a.z + b.z);
    public static CubePos operator -(CubePos a, CubePos b)
        => new CubePos(a.x - b.x, a.y - b.y, a.z - b.z);
}
