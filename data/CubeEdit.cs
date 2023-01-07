using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Operations on Cubes and CubeWorlds.
/// </summary>
public static class CubeEdit {
    /// <summary>
    /// Find the cube at the given position in the root, no deeper than the given depth.
    /// </summary>
    /// <param name="root">Root cube in which to search.</param>
    /// <param name="pos">Position of the cube to locate.</param>
    /// <param name="depth">
    /// Depth in tree at which to stop searching and return a branch.
    /// Otherwise keep searching until a leaf is found.
    /// </param>
    /// <returns>The located cube.</returns>
    private static Cube GetCube(Cube root, CubePos pos, int depth) {
        if (depth <= 0 || root is Cube.LeafImmut) {
            return root;
        } else {
            var branch = root as Cube.BranchImmut;
            return GetCube(branch.child(pos.ChildIndex()), pos.ToChild(), depth - 1);
        }
    }

    /// <summary>
    /// Apply a function to the cube at the given position in the root, with the given depth.
    /// Produce a new root cube with the function applied. Create branches if necessary.
    /// </summary>
    /// <param name="root">Root cube in which to search.</param>
    /// <param name="pos">Position of the cube to locate.</param>
    /// <param name="depth">
    /// Depth in tree of cube to be replaced. Any leaves will be split into branches to reach this
    /// depth (unless the function does not modify the cube).
    /// </param>
    /// <param name="func">Function to apply to the located cube.</param>
    /// <returns>A new root cube with the function applied to the specified cube.</returns>
    private static Cube CubeApply(Cube root, CubePos pos, int depth, Func<Cube, Cube> func) {
        if (depth <= 0)
            return func(root);

        Cube.Branch newBranch;
        if (root is Cube.BranchImmut branch) {
            newBranch = branch.Val;
        } else {
            newBranch = new Cube.Branch(root); // TODO: stop early if result is identical
        }
        int childI = pos.ChildIndex();
        bool modified = false;
        newBranch.children[childI] = Util.AssignChanged(newBranch.children[childI],
            CubeApply(newBranch.children[childI], pos.ToChild(), depth - 1, func), ref modified);
        if (!modified) return root; // avoid allocation
        return newBranch.Immut();
    }

    /// <summary>
    /// Place a cube at the given position in the root, with the given depth.
    /// Create branches if necessary.
    /// </summary>
    /// <param name="root">Root cube in which to place cube.</param>
    /// <param name="pos">Position of the cube to place.</param>
    /// <param name="depth">Depth in tree to place cube. Any leaves will be split into branches to
    /// reach this depth (unless the cube is identical to the existing leaf).</param>
    /// <param name="put">The cube to be placed in the root.</param>
    /// <returns>A new root cube with the target cube replaced.</returns>
    private static Cube PutCube(Cube root, CubePos pos, int depth, Cube put) {
        if (put is Cube.LeafImmut putLeaf) {
            return CubeApply(root, pos, depth, c => {
                if (c is Cube.LeafImmut curLeaf && curLeaf.Val.Equals(putLeaf.Val))
                    return c; // help CubeApply avoid allocation
                return put;
            });
        } else {
            return CubeApply(root, pos, depth, _ => put);
        }
    }

    /// <summary>
    /// Set all volumes within a cube located in the root.
    /// </summary>
    /// <param name="root">Root cube containting the cube to modify.</param>
    /// <param name="pos">Position of cube to modify.</param>
    /// <param name="depth">Depth of cube to modify in root.</param>
    /// <param name="volume">Volume to set within the cube.</param>
    /// <returns>The root with all volumes in one cube replaced.</returns>
    public static Cube PutVolumes(Cube root, CubePos pos, int depth, Guid volume) {
        return CubeApply(root, pos, depth, c => SetAllVolumes(c, volume));
    }

    /// <summary>
    /// Set all volumes in the cube.
    /// </summary>
    /// <param name="cube">Cube to modify.</param>
    /// <param name="volume">New volume.</param>
    /// <returns>The cube with all volumes replaced recursively.</returns>
    private static Cube SetAllVolumes(Cube cube, Guid volume) {
        // TODO this could optimize like OptimizeCube()
        if (cube is Cube.LeafImmut leaf) {
            if (leaf.Val.volume == volume) return cube; // avoid allocation
            var newLeaf = leaf.Val;
            newLeaf.volume = volume;
            return newLeaf.Immut();
        } else {
            var newBranch = (cube as Cube.BranchImmut).Val;
            bool modified = false;
            for (int i = 0; i < 8; i++) {
                newBranch.children[i] = Util.AssignChanged(newBranch.children[i],
                    SetAllVolumes(newBranch.children[i], volume), ref modified);
            }
            if (!modified) return cube; // avoid allocation
            return newBranch.Immut();
        }
    }

    /// <summary>
    /// Set all faces within a square aligned to the side of one cube.
    /// </summary>
    /// <param name="root">Root cube in which the square is located.</param>
    /// <param name="pos">
    /// Position of the cube whose side will form the square of faces to replace.
    /// </param>
    /// <param name="depth">Depth in tree of the cube.</param>
    /// <param name="axis">Normal axis of the square.</param>
    /// <param name="face">New value to replace existing faces.</param>
    /// <returns>The root cube with all faces within one square replaced.</returns>
    public static Cube PutFaces(Cube root, CubePos pos, int depth, int axis,
            Immut<Cube.Face> face) {
        return CubeApply(root, pos, depth, c => SetAllFaces(c, axis, face));
    }

    /// <summary>
    /// Set all faces coplanar with the negative side of the given axis, recursively.
    /// </summary>
    /// <param name="cube">Target cube containing faces to be set.</param>
    /// <param name="axis">Normal axis of the plane of faces to be replaced.</param>
    /// <param name="face">New value to replace existing faces.</param>
    /// <returns>The given cube with all faces along one side replaced.</returns>
    private static Cube SetAllFaces(Cube cube, int axis, Immut<Cube.Face> face) {
        if (cube is Cube.LeafImmut leaf) {
            if (leaf.face(axis).Val.Equals(face.Val)) return cube; // avoid allocation
            var newLeaf = leaf.Val;
            newLeaf.faces[axis] = face;
            return newLeaf.Immut();
        } else {
            var newBranch = (cube as Cube.BranchImmut).Val;
            bool modified = false;
            for (int i = 0; i < 4; i++) {
                int childI = CubeUtil.CycleIndex(i, axis + 1);
                newBranch.children[childI] = Util.AssignChanged(newBranch.children[childI],
                    SetAllFaces(newBranch.children[childI], axis, face), ref modified);
            }
            if (!modified) return cube; // avoid allocation
            return newBranch.Immut();
        }
    }

    /// <summary>
    /// Copy a cube from one location to another, along with surrounding faces.
    /// </summary>
    /// <param name="srcRoot">The root cube in which the cube to be copied exists.</param>
    /// <param name="srcPos">Location of the cube to be copied.</param>
    /// <param name="srcDepth">Depth of cube to be copied within the root.</param>
    /// <param name="dstRoot">The root which will be modified to place the copied cube.</param>
    /// <param name="dstPos">Location to copy the cube.</param>
    /// <param name="dstDepth">Depth in root to copy the cube.</param>
    /// <returns>dstRoot with the cube from srcRoot copied to its new location.</returns>
    public static Cube TransferCube(Cube srcRoot, CubePos srcPos, int srcDepth,
            Cube dstRoot, CubePos dstPos, int dstDepth) {
        Cube oldSrcCube = GetCube(srcRoot, srcPos, srcDepth);
        Cube copied = oldSrcCube;
        Cube oldDstCube = GetCube(dstRoot, dstPos, dstDepth);
        // TODO skip if adjacent selection
        for (int axis = 0; axis < 3; axis++) {
            CubePos srcAxisOff = CubePos.Axis(axis, CubePos.CubeSize(srcDepth));
            CubePos dstAxisOff = CubePos.Axis(axis, CubePos.CubeSize(dstDepth));
            // transfer min face from existing cube at dstPos (since it's overwritten with copied)
            copied = TransferFaces(GetCube(dstRoot, dstPos - dstAxisOff, dstDepth), oldDstCube,
                copied, axis);
            // transfer min face from cube at srcPos
            copied = TransferFaces(GetCube(srcRoot, srcPos - srcAxisOff, srcDepth), oldSrcCube,
                copied, axis);
            // transfer max face from cube at srcPos
            dstRoot = CubeApply(dstRoot, dstPos + dstAxisOff, dstDepth, c => {
                return TransferFaces(oldSrcCube, GetCube(srcRoot, srcPos + srcAxisOff, srcDepth),
                    c, axis);
            });
        }
        dstRoot = PutCube(dstRoot, dstPos, dstDepth, copied);
        return dstRoot;
    }

    /// <summary>
    /// Extrude the side of one cube into the adjacent cube, including any faces/volumes along that
    /// side.
    /// </summary>
    /// <param name="srcRoot">The root cube in which the side to be extruded exists.</param>
    /// <param name="dstRoot">
    /// The root cube which will be modified to add the extruded cube and faces.
    /// </param>
    /// <param name="pos">The position of the cube side to be extruded</param>
    /// <param name="depth">Depth of the extruded cube in the tree</param>
    /// <param name="axis">Axis of the side to be extruded (on the negative side)</param>
    /// <param name="dir">
    /// If true, side will be extruded in the positive direction along the axis; if false, negative.
    /// </param>
    /// <returns>dstRoot with the side from srcRoot extruded.</returns>
    public static Cube Extrude(Cube srcRoot, Cube dstRoot,
            CubePos pos, int depth, int axis, bool dir) {
        // TODO: split into smaller functions
        uint size = CubePos.CubeSize(depth);
        CubePos axisOff = CubePos.Axis(axis, size);
        CubePos minPos = pos - axisOff;
        CubePos fromPos = dir ? minPos : pos, toPos = dir ? pos : minPos;
        Cube minCube = GetCube(srcRoot, minPos, depth), maxCube = GetCube(srcRoot, pos, depth);
        Cube fromCube = dir ? minCube : maxCube, toCube = dir ? maxCube : minCube;

        Cube extruded = MakeExtruded(fromCube, axis, dir);
        if (dir) {
            dstRoot = CubeApply(dstRoot, toPos + axisOff, depth,
                c => TransferFaces(minCube, maxCube, c, axis));
        } else {
            // extruded cube completely replaces toCube, so transfer faces from that as well
            extruded = TransferFaces(GetCube(srcRoot, toPos - axisOff, depth), toCube,
                extruded, axis);
            extruded = TransferFaces(minCube, maxCube, extruded, axis);
        }
        // update other 4 faces
        // TODO skip if adjacent selection
        int sideChildI = dir ? (1 << axis) : 0;
        for (int i = 0; i < 2; i++) {
            int sideAxis = (axis + i + 1) % 3;
            CubePos sideAxisOff = CubePos.Axis(sideAxis, size);
            // transfer existing face on min side...
            extruded = TransferFaces(GetCube(srcRoot, toPos - sideAxisOff, depth), toCube,
                extruded, sideAxis);
            // min face, extrude front
            extruded = TransferExtendedEdge(minCube, maxCube, extruded,
                srcChildI: 0, srcFaceAxis: axis, dstFaceAxis: sideAxis, axis);
            // extrude side
            extruded = TransferExtendedEdge(
                GetCube(srcRoot, fromPos - sideAxisOff, depth), fromCube, extruded,
                srcChildI: sideChildI, srcFaceAxis: sideAxis, dstFaceAxis: sideAxis, axis);
            dstRoot = CubeApply(dstRoot, toPos + sideAxisOff, depth, c => {
                // max face, extrude front
                c = TransferExtendedEdge(minCube, maxCube, c,
                    srcChildI: 1 << sideAxis, srcFaceAxis: axis, dstFaceAxis: sideAxis, axis);
                // extrude side
                return TransferExtendedEdge(
                    fromCube, GetCube(srcRoot, fromPos + sideAxisOff, depth), c,
                    srcChildI: sideChildI, srcFaceAxis: sideAxis, dstFaceAxis: sideAxis, axis);
            });
        }
        dstRoot = PutCube(dstRoot, toPos, depth, extruded);
        return dstRoot;
    }

    /// <summary>
    /// Build a cube that is the result of extruding the given cube along one of its sides.
    /// The result is a cube whose leaves are identical along any line perpendicular to the axis.
    /// </summary>
    /// <param name="cube">Cube to be extruded</param>
    /// <param name="axis">Axis of the side to be extruded</param>
    /// <param name="dir">If true, the positive side is extruded; if false, negative.</param>
    /// <returns>The extruded cube.</returns>
    private static Cube MakeExtruded(Cube cube, int axis, bool dir) {
        if (cube is Cube.BranchImmut branch) {
            Cube.Branch extruded = new Cube.Branch();
            for (int i = 0; i < 4; i++) {
                int minI = CubeUtil.CycleIndex(i, axis + 1);
                int maxI = minI | (1 << axis);
                var child = MakeExtruded(branch.child(dir ? maxI : minI), axis, dir);
                extruded.children[minI] = extruded.children[maxI] = child;
            }
            return extruded.Immut(); // TODO avoid allocation when possible
        } else {
            return cube;
        }
    }

    /// <summary>
    /// Transfer any boundary faces between two adjacent cubes to a third cube. A boundary face is a
    /// face between two different volumes (faces between the same volume are hidden/ignored).
    /// </summary>
    /// <param name="srcMin">
    /// One of the adjacent cubes, toward the negative direction of the axis.
    /// </param>
    /// <param name="srcMax">The other adjacent cube, toward the positive direction.</param>
    /// <param name="dst">
    /// The destination cube whose faces will be modified (on the negative side).
    /// </param>
    /// <param name="axis">
    /// The axis on which the source cubes are adjacent, and of the side that is modified.
    /// </param>
    /// <returns>The destination cube, with boundary faces transferred to one side.</returns>
    private static Cube TransferFaces(Cube srcMin, Cube srcMax, Cube dst, int axis) {
        if (srcMin is Cube.LeafImmut leafMin && srcMax is Cube.LeafImmut leafMax) {
            if (leafMin.Val.volume == leafMax.Val.volume) {
                return dst;
            } else {
                return SetAllFaces(dst, axis, leafMax.face(axis));
            }
        } else {
            Cube.Branch newBranch;
            if (dst is Cube.BranchImmut branch) {
                newBranch = branch.Val;
            } else {
                newBranch = new Cube.Branch(dst);
            }
            bool modified = false;
            for (int i = 0; i < 4; i++) {
                int maxI = CubeUtil.CycleIndex(i, axis + 1);
                Cube childMin = srcMin, childMax = srcMax;
                if (srcMin is Cube.BranchImmut branchMin)
                    childMin = branchMin.child(maxI | (1 << axis));
                if (srcMax is Cube.BranchImmut branchMax)
                    childMax = branchMax.child(maxI);
                newBranch.children[maxI] = Util.AssignChanged(newBranch.children[maxI],
                    TransferFaces(childMin, childMax, newBranch.children[maxI], axis),
                    ref modified);
            }
            if (!modified) return dst; // avoid allocation
            return newBranch.Immut();
        }
    }

    /// <summary>
    /// Similar to TransferFaces(). Copy the boundary faces along the edge of one side between two
    /// cubes, to the side of another cube, extending them along the perpendicular axis such that
    /// the faces along any line on the plane in that direction are identical.
    /// </summary>
    /// <param name="srcMin">Adjacent source cube in the negative direction</param>
    /// <param name="srcMax">Adjacent source cube in the positive direction</param>
    /// <param name="dst">Destination cube whose faces will be modified (negative side)</param>
    /// <param name="srcChildI">
    /// The edge to be extended is defined by two adjacent child indices (0-7). This is the lower of
    /// the two.
    /// </param>
    /// <param name="srcFaceAxis">
    /// Side of the source cubes whose edge is extended, also the axis on which they're adjacent.
    /// </param>
    /// <param name="dstFaceAxis">
    /// Side of the destination cube where the faces should be transferred.
    /// </param>
    /// <param name="extAxis">
    /// Direction along the *destination* that the edge should be extended. The axis parallel to
    /// to the edge (for both source/destination) is perpendicular to dstFaceAxis and extAxis.
    /// </param>
    /// <returns>The destination cube, with boundary faces extended along one side.</returns>
    private static Cube TransferExtendedEdge(Cube srcMin, Cube srcMax, Cube dst,
            int srcChildI, int srcFaceAxis, int dstFaceAxis, int extAxis) {
        // TODO a lot of reused code from TransferFaces
        if (srcMin is Cube.LeafImmut leafMin && srcMax is Cube.LeafImmut leafMax) {
            if (leafMin.Val.volume == leafMax.Val.volume) {
                return dst;
            } else {
                return SetAllFaces(dst, dstFaceAxis, leafMax.face(srcFaceAxis));
            }
        } else {
            Cube.Branch newBranch;
            if (dst is Cube.BranchImmut branch) {
                newBranch = branch.Val;
            } else {
                newBranch = new Cube.Branch(dst);
            }
            int edgeI = 7 & ~(1 << dstFaceAxis) & ~(1 << extAxis);
            bool modified = false;
            for (int i = 0; i < 2; i++) {
                int srcMaxI = (edgeI * i) | srcChildI;
                Cube childMin = srcMin, childMax = srcMax;
                if (srcMin is Cube.BranchImmut branchMin)
                    childMin = branchMin.child(srcMaxI | (1 << srcFaceAxis));
                if (srcMax is Cube.BranchImmut branchMax)
                    childMax = branchMax.child(srcMaxI);
                int dstI1 = edgeI * i;
                int dstI2 = dstI1 | (1 << extAxis);
                // TODO seems inefficient
                newBranch.children[dstI1] = Util.AssignChanged(newBranch.children[dstI1],
                    TransferExtendedEdge(childMin, childMax, newBranch.children[dstI1],
                        srcChildI, srcFaceAxis, dstFaceAxis, extAxis), ref modified);
                newBranch.children[dstI2] = Util.AssignChanged(newBranch.children[dstI2],
                    TransferExtendedEdge(childMin, childMax, newBranch.children[dstI2],
                        srcChildI, srcFaceAxis, dstFaceAxis, extAxis), ref modified);
            }
            if (!modified) return dst; // avoid allocation
            return newBranch.Immut();
        }
    }

    /// <summary>
    /// If any of the given unit positions are outside the bounds of the world (ie. the unit cube),
    /// expand the root of the world to include those points, keeping existing cubes at the same
    /// position/size.
    /// </summary>
    /// <param name="world">The world which may be expanded.</param>
    /// <param name="points">Points to test, mapped so (0,0,0) corresponds to world.rootPos
    /// and (1,1,1) corresponds to world.rootPos + world.rootSize * (1,1,1).</param>
    /// <param name="oldRootPos">
    /// The position of the previous root of the world within the new root.
    /// Will be (0,0,0) if not expanded.
    /// </param>
    /// <param name="depth">
    /// The depth of the previous root of the world within the new root. Will be 0 if not expanded.
    /// </param>
    /// <returns>The expanded world.</returns>
    public static CubeWorld ExpandWorld(CubeWorld world, IEnumerable<Vector3> points,
            out CubePos oldRootPos, out int depth) {
        oldRootPos = new CubePos(0, 0, 0);
        depth = 0;
        AABB unitBox = new AABB(Vector3.Zero, Vector3.One);
        foreach (Vector3 point in points) {
            while (true) {
                Vector3 rootPt = CubeUtil.ToAncestorPos(point, oldRootPos, depth);
                if (unitBox.HasPoint(rootPt))
                    break;
                int rootChildI =
                    (rootPt.x < 0 ? 1 : 0) | (rootPt.y < 0 ? 2 : 0) | (rootPt.z < 0 ? 4 : 0);
                Cube.Branch newRoot = new Cube.Branch(new Cube.Leaf(world.voidVolume).Immut());
                newRoot.children[rootChildI] = world.root;

                world.root = newRoot.Immut();
                world.rootPos -= CubeUtil.IndexVector(rootChildI) * world.rootSize;
                world.rootSize *= 2;
                oldRootPos = oldRootPos.ToParent(rootChildI);
                depth += 1;
            }
        }
        return world;
    }

    /// <summary>
    /// Reduce the size of the root of the world as much as possible without affecting its contents.
    /// </summary>
    /// <param name="world">The world to shrink.</param>
    /// <param name="newRootPos">
    /// The position of the new root of the world within the previous root.
    /// Will be (0,0,0) if no change.
    /// </param>
    /// <param name="depth">
    /// The depth of the new root of the world within the previous root. Will be 0 if no change.
    /// </param>
    /// <returns>The world, possibly reduced in size.</returns>
    private static CubeWorld ShrinkWorld(CubeWorld world, out CubePos newRootPos, out int depth) {
        newRootPos = new CubePos(0, 0, 0);
        depth = 0;
        while (world.root is Cube.BranchImmut rootBranch) {
            int singleBranchI = -1;
            for (int i = 0; i < 8; i++) {
                if (rootBranch.child(i) is Cube.BranchImmut) {
                    if (singleBranchI != -1)
                        return world; // can't shrink
                    singleBranchI = i;
                } else {
                    if ((rootBranch.child(i) as Cube.LeafImmut).Val.volume != world.voidVolume)
                        return world; // can't shrink
                }
            }
            var child = rootBranch.child(singleBranchI);
            for (int axis = 0; axis < 3; axis++) {
                if ((singleBranchI & (1 << axis)) == 0) {
                    if (!MaxSideVolumeEqual(child, axis, world.voidVolume))
                        return world; // can't shrink
                }
            }

            world.root = child;
            world.rootSize /= 2;
            world.rootPos += CubeUtil.IndexVector(singleBranchI) * world.rootSize;
            newRootPos = newRootPos.ToParent(singleBranchI);
            depth += 1;
        }
        return world;
    }

    /// <summary>
    /// Check if the volume of all cubes bordering one side (positive direction) are equal.
    /// </summary>
    /// <param name="cube">Cube whose volumes will be checked recursively.</param>
    /// <param name="axis">Axis of the side of the cube to check, in the positive direction.</param>
    /// <param name="volume">Volume to check for.</param>
    /// <returns>
    /// True if all cubes along the given side match the given volume, false otherwise.
    /// </returns>
    private static bool MaxSideVolumeEqual(Cube cube, int axis, Guid volume) {
        if (cube is Cube.LeafImmut leaf) {
            return leaf.Val.volume == volume;
        } else {
            var branch = cube as Cube.BranchImmut;
            for (int i = 0; i < 4; i++) {
                int childI = CubeUtil.CycleIndex(i, axis + 1) | (1 << axis);
                if (!MaxSideVolumeEqual(branch.child(childI), axis, volume))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Remove as many branch cubes as possible from the world without changing the 3D structure.
    /// </summary>
    /// <param name="world">The world to be optimized.</param>
    /// <returns>Equivalent optimized version of the world.</returns>
    public static CubeWorld Optimize(CubeWorld world, out CubePos newRootPos, out int shrinkDepth) {
        var voidLeaf = new Cube.Leaf(world.voidVolume).Immut();
        world.root = OptimizeCube(world.root, (voidLeaf, voidLeaf, voidLeaf));
        world = ShrinkWorld(world, out newRootPos, out shrinkDepth);
        return world;
    }

    /// <summary>
    /// From the given cube, build an equivalent cube containing as few branches as possible.
    /// </summary>
    /// <param name="cube">The cube to be optimized.</param>
    /// <param name="minCubes">
    /// Cubes adjacent to the given cube along each axis, on the negative side.
    /// </param>
    /// <returns>Equivalent optimized cube.</returns>
    private static Cube OptimizeCube(Cube cube, Arr3<Cube> minCubes) {
        if (cube is Cube.BranchImmut branch) {
            var newBranch = branch.Val;
            bool modified = false;
            for (int i = 0; i < 8; i++) {
                Arr3<Cube> childMinCubes = new Arr3<Cube>();
                for (int axis = 0; axis < 3; axis++) {
                    if ((i & (1 << axis)) != 0) {
                        // use new child (always lower index, so already optimized!)
                        childMinCubes[axis] = newBranch.children[i & ~(1 << axis)];
                    } else if (minCubes[axis] is Cube.BranchImmut branchMin) {
                        childMinCubes[axis] = branchMin.child(i | (1 << axis));
                    } else {
                        childMinCubes[axis] = minCubes[axis];
                    }
                }
                newBranch.children[i] = Util.AssignChanged(newBranch.children[i],
                    OptimizeCube(branch.child(i), childMinCubes), ref modified);
            }
            Cube optimized = OptimizeShallow(newBranch, minCubes);
            if (optimized != null) return optimized;
            if (!modified) return cube; // avoid allocation
            return newBranch.Immut();
        } else {
            return cube;
        }
    }

    /// <summary>
    /// Reduce the given branch to an equivalent leaf if possible, without recursion.
    /// </summary>
    /// <param name="branch">Branch to be reduced.</param>
    /// <param name="minCubes">
    /// Cubes adjacent to the given branch along each axis, on the negative side.
    /// </param>
    /// <returns>An equivalent Cube.LeafImmut, or null if the branch can't be reduced.</returns>
    private static Cube OptimizeShallow(Cube.Branch branch, Arr3<Cube> minCubes) {
        if (!(branch.children[0] is Cube.LeafImmut leaf0))
            return null;
        Cube.Leaf optimized = leaf0.Val;
        for (int i = 1; i < 8; i++) { // skip 0!
            if (!(branch.children[i] is Cube.LeafImmut childLeaf)
                    || childLeaf.Val.volume != optimized.volume)
                return null;
        }
        bool modified = false;
        for (int axis = 0; axis < 3; axis++) {
            Cube.LeafImmut minLeaf = minCubes[axis] as Cube.LeafImmut;
            bool hasBoundary = false;
            for (int i = 0; i < 4; i++) {
                var childLeaf = branch.children[CubeUtil.CycleIndex(i, axis + 1)]
                    as Cube.LeafImmut;
                if (minLeaf != null && childLeaf.Val.volume == minLeaf.Val.volume)
                    continue; // face will not be used since there's no boundary
                if (!childLeaf.face(axis).Val.Equals(optimized.faces[axis].Val)) {
                    if (hasBoundary)
                        return null; // boundaries are different, can't merge
                    optimized.faces[axis] = childLeaf.face(axis);
                    modified = true;
                }
                hasBoundary = true;
            }
        }
        if (!modified) return leaf0; // avoid allocation
        return optimized.Immut();
    }
}
