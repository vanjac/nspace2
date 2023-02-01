using System;
using System.Collections.Generic;

/// <summary>
/// Operations on Cubes and CubeModels.
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
            return GetCube(branch.child(pos.ChildIndex()), pos << 1, depth - 1);
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
            CubeApply(newBranch.children[childI], pos << 1, depth - 1, func), ref modified);
        if (!modified) return root; // avoid allocation
        return newBranch.Immut();
    }

    /// <summary>
    /// Callback function to be used with BoxApply. Called whenever a cube is found that is entirely
    /// inside the box.
    /// </summary>
    /// <param name="cube">
    /// The cube which is inside the box, could be a branch or leaf.
    /// Modify this value to replace the cube.</param>
    /// <param name="pos">Position of the cube</param>
    /// <param name="depth">Depth of the cube</param>
    /// <returns>true if recursion should stop and the current cube should be used.</returns>
    private delegate bool CubeCallback(ref Cube cube, CubePos pos, int depth);

    /// <summary>
    /// Apply a function to all cubes within the given box inside the root. Produce a new root cube
    /// with the function applied. Create branches if necessary.
    /// </summary>
    /// <param name="cube">Current cube being searched.</param>
    /// <param name="minPos">Minimum coordinates of the box.</param>
    /// <param name="maxPos">Maximum coordinates of the box.</param>
    /// <param name="func">Function to apply to each cube inside the box.</param>
    /// <param name="flattenAxes">
    /// Bitfield of axes along which the cube is collapsed to width 1 when checking if inside box.
    /// </param>
    /// <param name="cubePos">Position of the current cube.</param>
    /// <param name="cubeDepth">Depth of the current cube.</param>
    /// <returns>A new root cube with the function applied to each cube in the box.</returns>
    private static Cube BoxApply(Cube cube, CubePos minPos, CubePos maxPos, CubeCallback func,
            int flattenAxes = 0, CubePos cubePos = new CubePos(), int cubeDepth = 0) {
        var cubePosMax = cubePos + CubePos.FromCubeSize(cubeDepth, -1);
        if (!(cubePosMax >= minPos && cubePos < maxPos))
            return cube; // outside box
        for (int axis = 0; axis < 3; axis++)
            if ((flattenAxes & (1 << axis)) != 0)
                cubePosMax[axis] = cubePos[axis];
        if (cubePos >= minPos && cubePosMax < maxPos) {
            if (func(ref cube, cubePos, cubeDepth))
                return cube;
        }
        Cube.Branch newBranch = (cube is Cube.BranchImmut branch) ? branch.Val
            : new Cube.Branch(cube);
        bool modified = false;
        for (int i = 0; i < 8; i++) { // TODO fewer iterations if flattenAxes specified
            CubePos childPos = cubePos + (CubePos.FromChildIndex(i) >> cubeDepth);
            newBranch.children[i] = Util.AssignChanged(newBranch.children[i], BoxApply(
                newBranch.children[i], minPos, maxPos, func, flattenAxes, childPos, cubeDepth + 1),
                ref modified);
        }
        if (!modified) return cube;
        return newBranch.Immut();
    }

    /// <summary>
    /// Apply a function to all cubes which border the maximum bounds of a box.
    /// </summary>
    /// <param name="cube">Cube in which rectangle exists</param>
    /// <param name="minPos">Minimum coordinates of the rectangle (axis coord ignored).</param>
    /// <param name="maxPos">Maximum coordinates of the rectangle, including axis coord.</param>
    /// <param name="axesMask">
    /// Bitfield of axes of the box that cubes must be bordering the max side of.
    /// </param>
    /// <param name="func">Function to apply to each cube inside the rectangle.</param>
    /// <returns>A new root cube with the function applied to each cube in the rectangle.</returns>
    private static Cube MaxSideBoxApply(Cube cube, CubePos minPos, CubePos maxPos, int axesMask,
            CubeCallback func) {
        for (int axis = 0; axis < 3; axis++) {
            if ((axesMask & (1 << axis)) != 0) {
                minPos[axis] = maxPos[axis];
                maxPos[axis] ++;
            }
        }
        return BoxApply(cube, minPos, maxPos, func, axesMask);
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
    /// Set all volumes within a box located in the root.
    /// </summary>
    /// <param name="root">Root cube in which the box is located.</param>
    /// <param name="minPos">Minimum coordinates of the box.</param>
    /// <param name="maxPos">Maximum coordinates of the box.</param>
    /// <param name="volume">Volume to set within the box.</param>
    /// <returns>The root cube with all volumes in the box replaced.</returns>
    public static Cube PutVolumes(Cube root, CubePos minPos, CubePos maxPos, Guid volume) {
        // TODO auto simplify cubes not bordering minimum bounds
        return BoxApply(root, minPos, maxPos, (ref Cube cube, CubePos pos, int _) => {
            if (cube is Cube.LeafImmut leaf) {
                if (leaf.Val.volume != volume) {
                    var newLeaf = leaf.Val;
                    newLeaf.volume = volume;
                    cube = newLeaf.Immut();
                }
                return true;
            } else if (pos > minPos) { // not bordering any faces on min side
                cube = new Cube.Leaf(volume).Immut();
                return true;
            }
            return false;
        });
    }

    /// <summary>
    /// Set all faces within a box located in the root.
    /// </summary>
    /// <param name="root">Root cube in which the box is located.</param>
    /// <param name="minPos">Minimum coordinates of the box.</param>
    /// <param name="maxPos">Maximum coordinates of the box.</param>
    /// <param name="face">New value to replace existing faces.</param>
    /// <returns>The root cube with all faces in the box replaced.</returns>
    public static Cube PutFaces(Cube root, CubePos minPos, CubePos maxPos, Immut<Cube.Face> face) {
        for (int axis = 0; axis < 3; axis++) {
            root = MaxSideBoxApply(root, minPos, maxPos, 1 << axis,
                (ref Cube cube, CubePos pos, int _) => {
                    if (cube is Cube.LeafImmut leaf) {
                        if (!leaf.face(axis).Val.Equals(face.Val)) {
                            var newLeaf = leaf.Val;
                            newLeaf.faces[axis] = face;
                            cube = newLeaf.Immut();
                        }
                        return true;
                    }
                    return false;
                });
        }
        return BoxApply(root, minPos, maxPos, (ref Cube cube, CubePos pos, int _) => {
            if (cube is Cube.LeafImmut leaf) {
                var newLeaf = leaf.Val;
                bool modified = false;
                for (int axis = 0; axis < 3; axis++) {
                    if (!leaf.face(axis).Val.Equals(face.Val)) {
                        newLeaf.faces[axis] = face;
                        modified = true;
                    }
                }
                if (modified) cube = newLeaf.Immut();
                return true;
            }
            return false;
        });
    }

    /// <summary>
    /// Set all faces coplanar with the negative side of the given axis, recursively.
    /// </summary>
    /// <param name="cube">Target cube containing faces to be set.</param>
    /// <param name="axis">Normal axis of the plane of faces to be replaced.</param>
    /// <param name="face">New value to replace existing faces.</param>
    /// <returns>The given cube with all faces along one side replaced.</returns>
    private static Cube SetAllFaces(Cube cube, int axis, Immut<Cube.Face> face) {
        // TODO replace with call to BoxApply?
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
    /// Copy cubes in a box from one location to another, along with surrounding faces.
    /// </summary>
    /// <param name="srcRoot">The root cube in which the box to be copied exists.</param>
    /// <param name="srcMin">Minimum coordinates of the box to be copied.</param>
    /// <param name="srcMax">Maximum coordinates of the box to be copied.</param>
    /// <param name="dstRoot">The root which will be modified to place the copied cubes.</param>
    /// <param name="dstMin">Location to copy the cubes.</param>
    /// <param name="depthDiff">Difference in depth between srcRoot and dstRoot.</param>
    /// <returns>dstRoot with the box from srcRoot copied to its new location.</returns>
    public static Cube TransferBox(Cube srcRoot, CubePos srcMin, CubePos srcMax,
            Cube dstRoot, CubePos dstMin, int depthDiff) {
        (CubePos dstPos, int dstDepth) calcDst(CubePos srcPos, int srcDepth)
            => (dstMin + ((srcPos - srcMin) >> depthDiff), srcDepth + depthDiff);
        for (int axis = 0; axis < 3; axis++) {
            MaxSideBoxApply(srcRoot, srcMin, srcMax, 1 << axis,
                (ref Cube srcCubeRef, CubePos srcPos, int srcDepth) => {
                    (CubePos dstPos, int dstDepth) = calcDst(srcPos, srcDepth);
                    if (!dstPos.Floor(dstDepth).Equals(dstPos))
                        return false; // not aligned to grid
                    var srcCube = srcCubeRef; // can't use ref inside nested lambda
                    // transfer max face from cube at srcPos
                    CubePos srcAdj = srcPos - CubePos.FromAxisSize(axis, srcDepth);
                    dstRoot = CubeApply(dstRoot, dstPos, dstDepth,
                        c => TransferFaces(GetCube(srcRoot, srcAdj, srcDepth), srcCube, c, axis));
                    return true;
                });
        }
        BoxApply(srcRoot, srcMin, srcMax, (ref Cube srcCube, CubePos srcPos, int srcDepth) => {
            (CubePos dstPos, int dstDepth) = calcDst(srcPos, srcDepth);
            if (!dstPos.Floor(dstDepth).Equals(dstPos))
                return false; // not aligned to grid
            Cube copied = srcCube;
            for (int axis = 0; axis < 3; axis++) {
                if (srcPos[axis] != srcMin[axis])
                    continue; // not bordering min bounds
                // transfer min face from existing cube at dstPos
                CubePos dstAdj = dstPos - CubePos.FromAxisSize(axis, dstDepth);
                copied = TransferFaces(GetCube(dstRoot, dstAdj, dstDepth),
                    GetCube(dstRoot, dstPos, dstDepth), copied, axis);
                // transfer min face from cube at srcPos
                CubePos srcAdj = srcPos - CubePos.FromAxisSize(axis, srcDepth);
                copied = TransferFaces(GetCube(srcRoot, srcAdj, srcDepth),
                    srcCube, copied, axis);
            }
            dstRoot = PutCube(dstRoot, dstPos, dstDepth, copied);
            return true;
        });
        return dstRoot;
    }

    /// <summary>
    /// Extrude the sides of all cubes in a rectangle into the adjacent cubes, including any
    /// faces/volumes along those sides.
    /// </summary>
    /// <param name="srcRoot">The root cube in which the rectangle to be extruded exists.</param>
    /// <param name="dstRoot">
    /// The root cube which will be modified to add the extruded cubes and faces.
    /// </param>
    /// <param name="rectMin">Minimum coordinates of the rectangle to be extruded.</param>
    /// <param name="rectMax">Maximum coordinates of the rectangle to be extruded.</param>
    /// <param name="extDepth">Depth of the distance to extrude in the tree.</param>
    /// <param name="extAxis">Axis of the sides to be extruded (on the negative side)</param>
    /// <param name="extDir">
    /// If true, rect will be extruded in the positive direction along the axis; if false, negative.
    /// </param>
    /// <returns>dstRoot with the rectangle from srcRoot extruded.</returns>
    public static Cube ExtrudeRect(Cube srcRoot, Cube dstRoot, CubePos rectMin, CubePos rectMax,
            int extDepth, int extAxis, bool extDir) {
        // TODO call TransferBox
        if (extDir)
            rectMax[extAxis] = rectMin[extAxis];
        CubePos extAxisOff = CubePos.FromAxisSize(extAxis, extDepth);
        int sideChildI = extDir ? (1 << extAxis) : 0;
        MaxSideBoxApply(srcRoot, rectMin, rectMax, 1 << extAxis,
            (ref Cube maxCubeRef, CubePos maxPos, int depth) => {
                if (depth != extDepth)
                    return depth > extDepth; // TODO!
                CubePos minPos = maxPos - extAxisOff;
                CubePos fromPos = extDir ? minPos : maxPos, toPos = extDir ? maxPos : minPos;
                Cube minCube = GetCube(srcRoot, minPos, depth), maxCube = maxCubeRef;
                Cube fromCube = extDir ? minCube : maxCube, toCube = extDir ? maxCube : minCube;

                Cube extruded = MakeExtruded(fromCube, extAxis, extDir);
                // transfer front face (TODO: some of this could be handled by TransferBox instead)
                if (extDir) {
                    dstRoot = CubeApply(dstRoot, toPos + extAxisOff, depth,
                        c => TransferFaces(minCube, maxCube, c, extAxis)); // TODO don't do this!
                } else {
                    // extruded cube completely replaces toCube, so transfer faces from that as well
                    extruded = TransferFaces(GetCube(srcRoot, toPos - extAxisOff, depth), toCube,
                        extruded, extAxis);
                    extruded = TransferFaces(minCube, maxCube, extruded, extAxis);
                }
                // update min sides
                for (int i = 0; i < 2; i++) {
                    int sideAxis = (extAxis + i + 1) % 3;
                    if (maxPos[sideAxis] != rectMin[sideAxis])
                        continue; // not bordering min bounds
                    CubePos sideAxisOff = CubePos.FromAxisSize(sideAxis, depth);
                    // transfer existing face...
                    extruded = TransferFaces(GetCube(srcRoot, toPos - sideAxisOff, depth), toCube,
                        extruded, sideAxis);
                    // extrude front
                    extruded = TransferExtendedEdge(minCube, maxCube, extruded,
                        srcChildI: 0, srcFaceAxis: extAxis, dstFaceAxis: sideAxis, extAxis);
                    // extrude side
                    extruded = TransferExtendedEdge(GetCube(srcRoot, fromPos - sideAxisOff, depth),
                        fromCube, extruded, srcChildI: sideChildI,
                        srcFaceAxis: sideAxis, dstFaceAxis: sideAxis, extAxis);
                }
                dstRoot = PutCube(dstRoot, toPos, depth, extruded);
                return true;
            });
        // max edges
        for (int i = 0; i < 2; i++) {
            int sideAxis = (extAxis + i + 1) % 3;
            if (rectMin[sideAxis] == rectMax[sideAxis])
                continue; // flat along this axis
            CubePos sideAxisOff = CubePos.FromAxisSize(sideAxis, extDepth);
            MaxSideBoxApply(srcRoot, rectMin, rectMax, (1 << extAxis) | (1 << sideAxis),
                (ref Cube maxCubeRef, CubePos maxPos, int depth) => {
                    if (depth != extDepth)
                        return depth > extDepth; // TODO!
                    CubePos minPos = maxPos - extAxisOff;
                    CubePos toPos = extDir ? maxPos : minPos;
                    Cube minAdj = GetCube(srcRoot, minPos - sideAxisOff, depth);
                    Cube maxAdj = GetCube(srcRoot, maxPos - sideAxisOff, depth);
                    Cube fromCube = extDir ? GetCube(srcRoot, minPos, depth) : maxCubeRef;
                    Cube fromAdj = extDir ? minAdj : maxAdj;
                    dstRoot = CubeApply(dstRoot, toPos, depth, c => {
                        // extrude front
                        c = TransferExtendedEdge(minAdj, maxAdj, c, srcChildI: 1 << sideAxis,
                            srcFaceAxis: extAxis, dstFaceAxis: sideAxis, extAxis);
                        // extrude side
                        return TransferExtendedEdge(fromAdj, fromCube, c, srcChildI: sideChildI,
                            srcFaceAxis: sideAxis, dstFaceAxis: sideAxis, extAxis);
                    });
                    return true;
                });
        }
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
    /// If any of the given unit positions are outside the bounds of the model, expand the root of
    /// the model to include those points, keeping existing cubes at the same position/size.
    /// </summary>
    /// <param name="model">The model which may be expanded.</param>
    /// <param name="points">Points to test in global coordinates.</param>
    /// <returns>The expanded model.</returns>
    public static Immut<CubeModel> ExpandModel(Immut<CubeModel> model,
            IEnumerable<CubePos> points) {
        CubeModel m = model.Val;
        bool modified = false;
        foreach (CubePos point in points) {
            while (!(point >= m.rootPos && point < m.rootPos + CubePos.FromCubeSize(m.rootDepth))) {
                var rootPos = m.rootPos;
                int rootChildI = (point - rootPos).ChildIndex(); // relies on 2's complement
                Cube.Branch newRoot = new Cube.Branch(new Cube.Leaf(m.voidVolume).Immut());
                newRoot.children[rootChildI] = m.root;

                m.root = newRoot.Immut();
                m.rootDepth -= 1;
                m.rootPos -= CubePos.FromChildIndex(rootChildI) >> m.rootDepth;
                modified = true;
            }
        }
        return modified ? Immut.Create(m) : model;
    }

    /// <summary>
    /// Reduce the size of the model's root as much as possible without affecting its contents.
    /// </summary>
    /// <param name="model">The model to shrink.</param>
    /// <returns>The model, possibly reduced in size.</returns>
    private static Immut<CubeModel> ShrinkModel(Immut<CubeModel> model) {
        CubeModel m = model.Val;
        bool modified = false;
        while (CanShrink(m, out int childI)) {
            m.root = (m.root as Cube.BranchImmut).child(childI);
            m.rootPos += CubePos.FromChildIndex(childI) >> m.rootDepth;
            m.rootDepth += 1;
            modified = true;
        }
        return modified ? Immut.Create(m) : model;
    }

    /// <summary>
    /// Check if the root of the model can be reduced to one of its children.
    /// </summary>
    /// <param name="model">The model to check.</param>
    /// <param name="childI">Index of the child which can become the new root.</param>
    /// <returns>True if the model root can be replaced with one of its children (childI).</returns>
    private static bool CanShrink(CubeModel model, out int childI) {
        childI = -1;
        if (!(model.root is Cube.BranchImmut branch))
            return false;
        for (int i = 0; i < 8; i++) {
            if (branch.child(i) is Cube.BranchImmut) {
                if (childI != -1) // more than one branch, can't shrink
                    return false;
                childI = i;
            } else if ((branch.child(i) as Cube.LeafImmut).Val.volume != model.voidVolume) {
                return false;
            }
        }
        var child = branch.child(childI);
        for (int axis = 0; axis < 3; axis++) {
            if ((childI & (1 << axis)) == 0) {
                if (!MaxSideVolumeEqual(child, axis, model.voidVolume))
                    return false;
            }
        }
        return true;
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
    /// Remove as many branch cubes as possible from the model without changing the 3D structure.
    /// </summary>
    /// <param name="model">The model to be simplified.</param>
    /// <returns>Equivalent simplified version of the model.</returns>
    public static Immut<CubeModel> Simplify(Immut<CubeModel> model) {
        CubeModel m = model.Val;
        var voidLeaf = new Cube.Leaf(m.voidVolume).Immut();
        bool modified = false;
        m.root = Util.AssignChanged(m.root, SimplifyCube(m.root, (voidLeaf, voidLeaf, voidLeaf)),
            ref modified);
        if (modified) model = Immut.Create(m);
        return ShrinkModel(model);
    }

    /// <summary>
    /// From the given cube, build an equivalent cube containing as few branches as possible.
    /// </summary>
    /// <param name="cube">The cube to be simplified.</param>
    /// <param name="minCubes">
    /// Cubes adjacent to the given cube along each axis, on the negative side.
    /// </param>
    /// <returns>Equivalent simplified cube.</returns>
    private static Cube SimplifyCube(Cube cube, Arr3<Cube> minCubes) {
        if (cube is Cube.BranchImmut branch) {
            var newBranch = branch.Val;
            bool modified = false;
            for (int i = 0; i < 8; i++) {
                Arr3<Cube> childMinCubes = new Arr3<Cube>();
                for (int axis = 0; axis < 3; axis++) {
                    if ((i & (1 << axis)) != 0) {
                        // use new child (always lower index, so already simplified!)
                        childMinCubes[axis] = newBranch.children[i & ~(1 << axis)];
                    } else if (minCubes[axis] is Cube.BranchImmut branchMin) {
                        childMinCubes[axis] = branchMin.child(i | (1 << axis));
                    } else {
                        childMinCubes[axis] = minCubes[axis];
                    }
                }
                newBranch.children[i] = Util.AssignChanged(newBranch.children[i],
                    SimplifyCube(branch.child(i), childMinCubes), ref modified);
            }
            Cube simplified = SimplifyShallow(newBranch, minCubes);
            if (simplified != null) return simplified;
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
    private static Cube SimplifyShallow(Cube.Branch branch, Arr3<Cube> minCubes) {
        if (!(branch.children[0] is Cube.LeafImmut leaf0))
            return null;
        Cube.Leaf simplified = leaf0.Val;
        for (int i = 1; i < 8; i++) { // skip 0!
            if (!(branch.children[i] is Cube.LeafImmut childLeaf)
                    || childLeaf.Val.volume != simplified.volume)
                return null;
        }
        bool modified = false;
        for (int axis = 0; axis < 3; axis++) {
            bool hasBoundary = false;
            for (int i = 0; i < 4; i++) {
                var childLeaf = branch.children[CubeUtil.CycleIndex(i, axis + 1)]
                    as Cube.LeafImmut;
                if (MaxSideVolumeEqual(minCubes[axis], axis, childLeaf.Val.volume))
                    continue; // face will not be used since there's no boundary
                if (!childLeaf.face(axis).Val.Equals(simplified.faces[axis].Val)) {
                    if (hasBoundary)
                        return null; // boundaries are different, can't merge
                    simplified.faces[axis] = childLeaf.face(axis);
                    modified = true;
                }
                hasBoundary = true;
            }
        }
        if (!modified) return leaf0; // avoid allocation
        return simplified.Immut();
    }
}
