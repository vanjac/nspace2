using Godot;
using System;
using System.Collections.Generic;
using GDArray = Godot.Collections.Array;

public class Editor : Spatial {

    public static readonly Guid VOLUME_EMPTY = new Guid("e6f63db0-ae21-4237-a8a0-96ce77ef5e75");

    [NodeRef("BuiltIn")] private BuiltIn nBuiltIn = null;
    [NodeRef("CubeMesh")] private CubeMesh nCubeMesh = null;
    [NodeRef("DebugCube")] private Spatial nDebugCube = null;
    [NodeRef("RectSelection")] private Spatial nRectSelection = null;
    [NodeRef("BoxSelection")] private Spatial nBoxSelection = null;
    [NodeRef("ExtrudeAdjust")] private MoveHandle nExtrudeAdjust = null;
    [NodeRef("MoveAdjust")] private Spatial nMoveAdjustRoot = null;
    [NodeRef("MoveAdjust/X")] private MoveHandle nMoveAdjustX = null;
    [NodeRef("MoveAdjust/Y")] private MoveHandle nMoveAdjustY = null;
    [NodeRef("MoveAdjust/Z")] private MoveHandle nMoveAdjustZ = null;
    private MoveHandle[] nMoveAdjustAxes;
    [NodeRef("ResizeHandles")] private Spatial nResizeHandleRoot = null;
    [NodeRef("EditorCamera")] private EditorCamera nCam = null;
    [NodeRef("/root/Spatial/EditorGUI")] private EditorGUI nGUI = null;

    private struct Clipping {
        public Cube root;
        public CubePos min, max; // in root coordinates!
        public int rootDepth;
    }

    private Dictionary<Guid, Material> materialsDict = new Dictionary<Guid, Material>();

    private EditState state;
    private Undoer<EditState> undo = new Undoer<EditState>();
    private Stack<Immut<CubeModel>> adjustPos = new Stack<Immut<CubeModel>>();
    private Stack<Immut<CubeModel>> adjustNeg = new Stack<Immut<CubeModel>>();
    private bool adjusting;

    private CubePos selStartMin, selStartMax;
    private Clipping clipboard;
    private Dictionary<Guid, Clipping> savedClips = new Dictionary<Guid, Clipping>();

    private string filePath;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        nMoveAdjustAxes = new MoveHandle[] { nMoveAdjustX, nMoveAdjustY, nMoveAdjustZ };
        int handleI = 0;
        foreach (ResizeHandle handle in nResizeHandleRoot.GetChildren()) {
            handle.Connect(nameof(ResizeHandle.Adjust), this, nameof(_OnSelectionResize),
                new GDArray { handleI });
            handle.Connect(nameof(ResizeHandle.AdjustEnd), this, nameof(_OnSelectionResizeEnd),
                new GDArray { handleI++ });
        }
        nGUI.Init();

        // https://www.reddit.com/r/godot/comments/d459x2/finally_figured_out_how_to_enable_wireframes_in/
        VisualServer.SetDebugGenerateWireframes(true);

        nGUI.nGridHalf.Connect("pressed", this, nameof(_OnGridHalfPressed));
        nGUI.nGridDouble.Connect("pressed", this, nameof(_OnGridDoublePressed));
        nGUI.nAddSelect.Connect("pressed", this, nameof(_OnAddSelectPressed));
        nGUI.nUndo.Connect("pressed", this, nameof(_OnUndoPressed));
        nGUI.nRedo.Connect("pressed", this, nameof(_OnRedoPressed));
        nGUI.nSave.Connect("pressed", this, nameof(_OnSavePressed));
        nGUI.nFilePopup.Connect("id_pressed", this, nameof(_OnFileMenuItemPressed));
        nGUI.nViewPopup.Connect("id_pressed", this, nameof(_OnViewMenuItemPressed),
            new GDArray { nGUI.nViewPopup });
        nGUI.nEmptyVolume.Connect("pressed", this, nameof(_OnVolumeButtonPressed),
            new GDArray { VOLUME_EMPTY.ToString() });
        nGUI.nSolidVolume.Connect("pressed", this, nameof(_OnVolumeButtonPressed),
            new GDArray { CubeVolume.SOLID.ToString() });
        nGUI.nFluidVolume.Connect("pressed", this, nameof(_OnVolumeButtonPressed),
            new GDArray { "39c2ca46-e6ce-4e0c-b851-295a7a5921b2" });
        nGUI.nCopy.Connect("pressed", this, nameof(_OnCopyPressed));
        nGUI.nPaste.Connect("pressed", this, nameof(_OnPastePressed));
        nGUI.nSaveClip.Connect("pressed", this, nameof(_OnSaveClipPressed));
        nGUI.nSaveDialog.Connect("file_selected", this, nameof(_OnSaveFileSelected));
        nGUI.nOpenDialog.Connect("file_selected", this, nameof(_OnOpenFileSelected));
        nGUI.nDeleteDialog.Connect("file_selected", this, nameof(_OnDeleteFileSelected));
        nGUI.nDeleteDialog.Connect("dir_selected", this, nameof(_OnDeleteDirSelected));
        nGUI.nClipNameDialog.Connect("confirmed", this, nameof(_OnClipNameConfirmed));

        InitLayerPaintGUI(nGUI.nBasePaint, false, nBuiltIn.baseMaterials);
        InitLayerPaintGUI(nGUI.nOverlayPaint, true, nBuiltIn.overlayMaterials);

        NewWorld(VOLUME_EMPTY, CubeVolume.SOLID);
    }

    private void InitLayerPaintGUI(LayerPaint gui, bool overlay, Resource[] matInfos) {
        gui.Init();
        gui.nUVOffsetLeft.Connect("pressed", this, nameof(_OnUVOffsetButtonPressed),
            new GDArray { overlay, 1, 0 });
        gui.nUVOffsetRight.Connect("pressed", this, nameof(_OnUVOffsetButtonPressed),
            new GDArray { overlay, -1, 0 });
        gui.nUVOffsetUp.Connect("pressed", this, nameof(_OnUVOffsetButtonPressed),
            new GDArray { overlay, 0, 1 });
        gui.nUVOffsetDown.Connect("pressed", this, nameof(_OnUVOffsetButtonPressed),
            new GDArray { overlay, 0, -1 });
        gui.nUVFlipHoriz.Connect("pressed", this, nameof(_OnUVFlipButtonPressed),
            new GDArray { overlay, Cube.Orient.FLIP_U });
        gui.nUVFlipVert.Connect("pressed", this, nameof(_OnUVFlipButtonPressed),
            new GDArray { overlay, Cube.Orient.FLIP_V });
        gui.nUVRotateCCW.Connect("pressed", this, nameof(_OnUVRotateButtonPressed),
            new GDArray { overlay, false });
        gui.nUVRotateCW.Connect("pressed", this, nameof(_OnUVRotateButtonPressed),
            new GDArray { overlay, true });
        gui.nUVReset.Connect("pressed", this, nameof(_OnUVResetButtonPressed),
            new GDArray { overlay });

        foreach (Resource matInfo in matInfos) {
            SpatialMaterial material = new SpatialMaterial();
            Texture texture = (Texture)matInfo.Get("texture");
            material.AlbedoTexture = texture;
            Vector2 textureScale = (Vector2)matInfo.Get("texture_scale");
            material.Uv1Scale = new Vector3(textureScale.x, textureScale.y, 1);
            if (overlay) {
                material.ParamsGrowAmount = 0.001f;
                if ((bool)matInfo.Get("cutout")) {
                    material.ParamsUseAlphaScissor = true;
                    material.ParamsAlphaScissorThreshold = 0.5f;
                } else {
                    material.FlagsTransparent = true;
                }
            }

            Guid guid = new Guid((string)matInfo.Get("guid"));
            if (guid == Guid.Empty)
                throw new Exception("Missing material GUID");
            if (materialsDict.ContainsKey(guid))
                throw new Exception("Duplicate material GUIDs");
            materialsDict[guid] = material;

            var button = gui.AddMaterialButton(texture);
            button.Connect("pressed", this, nameof(_OnMatButtonPressed),
                new GDArray { overlay, guid.ToString() });
        }
    }

    private void UpdateState(bool updateWorld) {
        if (updateWorld)
            UpdateWorld();
        UpdateSelection();
        UpdateAdjustPos();
        nCam.Update(state);

        nExtrudeAdjust.Update();
        foreach (var adjust in nMoveAdjustAxes)
            adjust.Update();
        foreach (ResizeHandle handle in nResizeHandleRoot.GetChildren())
            handle.Update();

        nGUI.UpdateState(state, undo);
        nCubeMesh.GridSize = CubeUtil.ModelCubeSize(state.editDepth);
    }

    private void UpdateWorld() {
        var m = state.world.Val;
        ulong startTick = Time.GetTicksMsec();
        var stats = nCubeMesh.UpdateMesh(m.root, m.rootPos.ToModelPos(),
            CubeUtil.ModelCubeSize(m.rootDepth), m.voidVolume, materialsDict);
        nGUI.StatsText =
            $"{stats.branches} br {stats.quads} qu {Time.GetTicksMsec() - startTick} ms";
        nDebugCube.Translation = state.world.Val.rootPos.ToModelPos();
        nDebugCube.Scale = Vector3.One * CubeUtil.ModelCubeSize(state.world.Val.rootDepth);
    }

    private void UpdateSelection() {
        nRectSelection.Visible = false;
        nBoxSelection.Visible = false;
        var selSize = state.selMax - state.selMin;
        if (selSize.Dimension() == 3) {
            nBoxSelection.Visible = true;
            nBoxSelection.Translation = state.selMin.ToModelPos();
            nBoxSelection.Scale = selSize.ToModelSize();
        } else if (selSize.Dimension() == 2) {
            var axis = selSize[0] == 0 ? 0 : selSize[1] == 0 ? 1 : 2; // bleh
            nRectSelection.Visible = true;
            nRectSelection.Translation = state.selMin.ToModelPos();
            nRectSelection.Rotation = GDUtil.AxisRotation(axis);
            int s = axis + 1, t = axis + 2;
            Vector3 size = selSize.ToModelSize();
            size = CubeUtil.CycleVector(size, 5 - axis);
            size.z = 1;
            nRectSelection.Scale = size;
        }
        bool handlesEnabled = state.AnySelection && !nGUI.AddSelectEnabled;
        for (int i = 0; i < 8; i++) {
            var handle = nResizeHandleRoot.GetChild<ResizeHandle>(i);
            handle.Enabled = handlesEnabled;
            if (handlesEnabled) {
                CubePos handlePos = state.selMin;
                for (int axis = 0; axis < 3; axis++)
                    if ((i & (1 << axis)) != 0)
                        handlePos[axis] = state.selMax[axis];
                handle.Translation = handlePos.ToModelPos();
                if (!handle.Adjusting)
                    handle.CurrentPos = handlePos;
            }
        }
    }

    private void UpdateAdjustPos() {
        bool adjustEnabled = state.AnySelection && !nGUI.AddSelectEnabled;
        foreach (var adjust in nMoveAdjustAxes)
            adjust.Enabled = adjustEnabled;

        var selSize = state.selMax - state.selMin;
        if (adjustEnabled && selSize.Dimension() == 2 && selSize[state.selAxis] == 0) {
            nExtrudeAdjust.Enabled = true;
            nExtrudeAdjust.Rotation = GDUtil.AxisRotation(state.selAxis, state.selDir);
            nMoveAdjustAxes[state.selAxis].Enabled = false;
        } else {
            nExtrudeAdjust.Enabled = false;
        }
        if (!adjustEnabled)
            return;

        Vector3 center = (state.selMin.ToModelPos() + state.selMax.ToModelPos()) / 2;
        nMoveAdjustRoot.Translation = center;
        nExtrudeAdjust.Translation = center;

        float snap = CubeUtil.ModelCubeSize(state.editDepth);
        nExtrudeAdjust.snap = snap;
        foreach (var adjust in nMoveAdjustAxes)
            adjust.snap = snap;
        foreach (ResizeHandle handle in nResizeHandleRoot.GetChildren())
            handle.snap = snap;
    }

    private CubePos FaceSize(int axis) {
        return CubePos.FromAxisSize(axis + 1, state.editDepth)
             + CubePos.FromAxisSize(axis + 2, state.editDepth);
    }

    private void SelectStart(CubePos pos, int axis, bool dir) {
        var posMax = pos + FaceSize(axis);
        if ((nGUI.AddSelectEnabled ^ Input.IsKeyPressed((int)KeyList.Shift))
                && state.AnySelection) {
            nGUI.AddSelectEnabled = false;
            selStartMin = CubePos.Min(state.selMin, pos);
            selStartMax = CubePos.Max(state.selMax, posMax);
        } else {
            state.selAxis = axis;
            state.selDir = dir;
            (selStartMin, selStartMax) = (pos, posMax);
        }
        (state.selMin, state.selMax) = (selStartMin, selStartMax);
    }

    private void SelectDrag(CubePos pos, int axis) {
        if (!state.AnySelection)
            return;
        var posMax = pos + FaceSize(axis);
        state.selMin = CubePos.Min(selStartMin, pos);
        state.selMax = CubePos.Max(selStartMax, posMax);
    }

    private void MoveSelection(int axis, int count) {
        CubePos move = CubePos.FromAxisSize(axis, state.editDepth) * count;
        state.selMin += move;
        state.selMax += move;
    }

    private bool ExpandIncludeSelection(CubePos offset) {
        bool modified = false;
        state.world = Util.AssignChanged(state.world, CubeEdit.ExpandModel(state.world,
            new CubePos[] { state.selMin + offset, state.selMax + offset }), ref modified);
        return modified;
    }

    private bool ApplyRoot(Func<CubeModel, Cube> func) {
        CubeModel m = state.world.Val;
        bool modified = false;
        m.root = Util.AssignChanged(m.root, func(m), ref modified);
        if (modified)
            state.world = Immut.Create(m);
        return modified;
    }

    private bool Move(int axis, bool dir) {
        CubePos axisOff = CubePos.FromAxisSize(axis, state.editDepth) * (dir ? 1 : -1);
        bool modified = ExpandIncludeSelection(axisOff);
        modified |= ApplyRoot(m => CubeEdit.Extrude(
            m.root, m.root, state.selMin.ToRoot(m), state.selMax.ToRoot(m),
            state.RootEditDepth, axis, dir));
        return modified;
    }

    private bool SetVolume(Guid volume) {
        bool modified = ExpandIncludeSelection(CubePos.ZERO);
        modified |= ApplyRoot(m => CubeEdit.PutVolumes(m.root,
            state.selMin.ToRoot(m), state.selMax.ToRoot(m), volume));
        return modified;
    }

    private bool ApplyRootFaceLayers(bool overlay, Func<Cube.Layer, Cube.Layer> func) {
        return ApplyRoot(m => CubeEdit.ApplyFaces(
            m.root, state.selMin.ToRootClamped(m), state.selMax.ToRootClamped(m), f => {
                var newFace = f.Val;
                if (overlay)
                    newFace.overlay = func(newFace.overlay);
                else
                    newFace.base_ = func(newFace.base_);
                return newFace.Equals(f.Val) ? f : Immut.Create(newFace);
            }));
    }

    private bool Paint(bool overlay, Cube.Layer layer) {
        return ApplyRootFaceLayers(overlay, _ => layer);
    }

    private bool UVOffset(bool overlay, int u, int v) {
        int cubeSize = (int)CubePos.CubeSize(state.editDepth);
        return ApplyRootFaceLayers(overlay, l => {
            l.uOffset += u * cubeSize;
            l.vOffset += v * cubeSize;
            return l;
        });
    }

    private bool UVFlip(bool overlay, Cube.Orient flip) {
        return ApplyRootFaceLayers(overlay, l => {
            l.orient ^= flip;
            return l;
        });
    }

    private bool UVRotate(bool overlay, bool cw) {
        return ApplyRootFaceLayers(overlay, l => {
            l.orient = CubeUtil.Rotate(l.orient, cw);
            return l;
        });
    }

    private bool UVReset(bool overlay) {
        return ApplyRootFaceLayers(overlay, l => {
            l.uOffset = l.vOffset = 0;
            l.orient = 0;
            return l;
        });
    }

    private Clipping Copy() {
        var m = CubeEdit.ExpandModel(state.world, new CubePos[] { state.selMin, state.selMax }).Val;
        return new Clipping {
            root = m.root,
            min = state.selMin.ToRoot(m),
            max = state.selMax.ToRoot(m),
            rootDepth = m.rootDepth,
        };
    }

    private bool Paste(Clipping clip) {
        ExpandIncludeSelection(CubePos.ZERO);
        CubeModel m = state.world.Val;
        CubePos pasteMin = state.selMin.ToRoot(m), pasteMax = state.selMax.ToRoot(m);
        bool modified = false;

        // fill selection with repeated copies
        int depthDiff = clip.rootDepth - m.rootDepth;
        CubePos pastePos = pasteMin;
        CubePos pasteStep = (clip.max - clip.min) >> depthDiff;
        bool loopCond(int axis) => pastePos[axis] < pasteMax[axis]
            || (pasteMin[axis] == pasteMax[axis] && pastePos[axis] == pasteMax[axis]);
        void loopStep(int axis) => pastePos[axis] = (clip.min[axis] == clip.max[axis]) ?
            uint.MaxValue : (pastePos[axis] + pasteStep[axis]);
        for (pastePos[2] = pasteMin[2]; loopCond(2); loopStep(2)) {
            for (pastePos[1] = pasteMin[1]; loopCond(1); loopStep(1)) {
                for (pastePos[0] = pasteMin[0]; loopCond(0); loopStep(0)) {
                    var pasteLimit = pasteMax - pastePos;
                    var clipLimit = clip.max;
                    for (int axis = 0; axis < 3; axis++)
                        if (pasteLimit[axis] < pasteStep[axis]) // avoid overflow
                            clipLimit[axis] = clip.min[axis] + (pasteLimit << depthDiff)[axis];
                    m.root = Util.AssignChanged(m.root, CubeEdit.TransferBox(
                        clip.root, clip.min, clipLimit, m.root, pastePos, depthDiff), ref modified);
                }
            }
        }
        if (modified)
            state.world = Immut.Create(m);
        return modified;
    }

    private bool SimplifyWorld() {
        bool modified = false;
        state.world = Util.AssignChanged(state.world, CubeEdit.Simplify(state.world), ref modified);
        return modified;
    }

    private void NewWorld(Guid innerVolume, Guid outerVolume) {
        var inner = new Cube.Leaf(innerVolume).Immut();
        var outer = new Cube.Leaf(outerVolume).Immut();
        var world = Immut.Create(new CubeModel {
            root = new Cube.Branch {
                children = (inner, outer, outer, outer, outer, outer, outer, outer)
            }.Immut(),
            rootDepth = CubeModel.UNIT_DEPTH - 3,
            rootPos = CubePos.HALF,
            voidVolume = outerVolume
        });
        state = new EditState {
            world = world,
            editDepth = CubeModel.UNIT_DEPTH,
            camFocus = new Vector3(2, 2, 2),
            camZoom = 6f,
        };
        undo.Clear();
        ResetAdjust();
        filePath = null;
        UpdateState(true);
    }

    private void Load(string path) {
        var op = BeginOperation("Load");
        using (var stream = System.IO.File.Open(path, System.IO.FileMode.Open)) {
            try {
                state = new CubeDeserialize().ReadFile(stream);
            } catch (Exception e) {
                nGUI.OperationText = "Error loading";
                GD.Print(e);
                return;
            }
        }
        undo.Clear();
        ResetAdjust();
        filePath = path;
        EndOperation(op, true);
    }

    private void Save() {
        undo.Push(state);
        var op = BeginOperation("Save");
        bool modified = SimplifyWorld();
        using (var stream = System.IO.File.Open(filePath, System.IO.FileMode.Create)) {
            new CubeSerialize().WriteFile(stream, state);
        }
        EndOperation(op, modified);
    }

    private (string name, ulong startTick) BeginOperation(string name) {
        CubeDebug.allocCount = 0;
        return (name, Time.GetTicksMsec());
    }

    private void EndOperation((string name, ulong startTick) op, bool modified) {
        nGUI.OperationText = $"{op.name} +{CubeDebug.allocCount} cu"
            + $" {Time.GetTicksMsec() - op.startTick} ms";
        UpdateState(modified);
        if (!modified)
            nGUI.StatsText = "=== no change ===";
    }

    private void ResetAdjust() {
        adjusting = false;
        adjustPos.Clear();
        adjustNeg.Clear();
    }

    private void MoveAdjust(int axis, int units) {
        if (!state.AnySelection)
            return;
        if (!adjusting) {
            undo.Push(state);
            adjusting = true;
        }
        if (nGUI.MoveSelectEnabled ^ Input.IsKeyPressed((int)KeyList.Shift)) {
            MoveSelection(axis, units);
            UpdateState(false);
            return;
        }

        var op = BeginOperation("Move");
        bool modified = false;
        for (; units > 0; units--) {
            if (adjustNeg.Count != 0) {
                state.world = Util.AssignChanged(state.world, adjustNeg.Pop(), ref modified);
            } else {
                adjustPos.Push(state.world);
                modified |= Move(axis, true);
            }
            MoveSelection(axis, 1);
        }
        for (; units < 0; units++) {
            if (adjustPos.Count != 0) {
                state.world = Util.AssignChanged(state.world, adjustPos.Pop(), ref modified);
            } else {
                adjustNeg.Push(state.world);
                modified |= Move(axis, false);
            }
            MoveSelection(axis, -1);
        }
        EndOperation(op, modified);
    }

    /* SIGNAL RECEIVERS */

    // TouchController...

    public void _OnSelectClear() {
        undo.Push(state);
        state.ClearSelection();
        UpdateState(false);
    }

    public void _OnSelectStart(Vector3 pos, Vector3 normal) {
        undo.Push(state);
        pos = ToLocal(pos);
        normal = Transform.basis.XformInv(normal);
        SelectStart(CubeUtil.PickFace(pos, normal, state.editDepth, state.world.Val.rootPos,
            out int axis, out bool dir), axis, dir);
        UpdateState(false);
    }

    public void _OnSelectDrag(Vector3 pos, Vector3 normal) {
        pos = ToLocal(pos);
        normal = Transform.basis.XformInv(normal);
        SelectDrag(CubeUtil.PickFace(pos, normal, state.editDepth, state.world.Val.rootPos,
            out int axis, out _), axis);
        UpdateState(false);
    }

    public void _OnCameraRefocus(float newZoom) {
        state.camFocus = (state.camFocus - nCam.GlobalCamPosition).Normalized() * newZoom
            + nCam.GlobalCamPosition;
        state.camZoom = newZoom;
    }

    public void _OnCameraZoom(float factor) {
        state.camZoom *= factor;
        UpdateState(false);
    }

    public void _OnCameraRotate(float yaw, float pitch) {
        state.camYaw += yaw;
        state.camPitch += pitch;
        UpdateState(false);
    }

    public void _OnCameraPan(Vector3 move) {
        state.camFocus += move * state.camZoom;
        UpdateState(false);
    }

    // AdjustHandle...

    public void _OnExtrudeAdjust(int units) {
        MoveAdjust(state.selAxis, state.selDir ? units : -units);
    }

    public void _OnMoveAdjust(int units, int axis) {
        MoveAdjust(axis, units);
    }

    public void _OnAdjustEnd() {
        ResetAdjust();
    }

    public void _OnSelectionResize(Vector3 units, int i) {
        if (!adjusting) {
            undo.Push(state);
            adjusting = true;
        }
        var handle = nResizeHandleRoot.GetChild<ResizeHandle>(i);
        int gridSize = (int)CubePos.CubeSize(state.editDepth);
        CubePos curPos = handle.CurrentPos;
        CubePos newPos = curPos + new CubePos(units) * gridSize;
        for (int axis = 0; axis < 3; axis++) {
            uint min = state.selMin[axis], max = state.selMax[axis];
            if (curPos[axis] == min)
                min = newPos[axis];
            else
                max = newPos[axis];
            state.selMin[axis] = Math.Min(min, max);
            state.selMax[axis] = Math.Max(min, max);
        }
        handle.CurrentPos = newPos;
        UpdateState(false);
    }

    public void _OnSelectionResizeEnd(int i) {
        ResetAdjust();
        UpdateState(false); // assign correct CurrentPos value
    }

    // GUI...

    public void _OnGridHalfPressed() {
        state.editDepth += 1;
        UpdateState(false);
    }

    public void _OnGridDoublePressed() {
        state.editDepth -= 1;
        UpdateState(false);
    }

    public void _OnAddSelectPressed() {
        UpdateState(false); // show/hide handles
    }

    public void _OnCopyPressed() {
        if (state.AnySelection)
            clipboard = Copy();
    }

    public void _OnPastePressed() {
        if (clipboard.root != null && state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Paste");
            EndOperation(op, Paste(clipboard));
        }
    }

    public void _OnSaveClipPressed() {
        if (state.AnySelection)
            nGUI.ShowClipNameDialog();
    }

    public void _OnClipNameConfirmed() {
        Guid guid = Guid.NewGuid();
        savedClips[guid] = Copy();
        var (pasteButton, deleteButton) = nGUI.AddClipGroup(guid.ToString(), nGUI.ClipName);
        pasteButton.Connect("pressed", this, nameof(_OnPasteClipPressed),
            new GDArray { guid.ToString() });
        deleteButton.Connect("pressed", this, nameof(_OnDeleteClipPressed),
            new GDArray { guid.ToString() });
    }

    public void _OnPasteClipPressed(string guid) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Paste Clip");
            EndOperation(op, Paste(savedClips[new Guid(guid)]));
        }
    }

    public void _OnDeleteClipPressed(string guid) {
        nGUI.RemoveClipGroup(guid);
        savedClips.Remove(new Guid(guid));
    }

    public void _OnVolumeButtonPressed(string guid) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Volumes");
            EndOperation(op, SetVolume(new Guid(guid)));
        }
    }

    public void _OnMatButtonPressed(bool overlay, string guid) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Paint");
            EndOperation(op, Paint(overlay, new Cube.Layer { material = new Guid(guid) }));
        }
    }

    public void _OnUVOffsetButtonPressed(bool overlay, int u, int v) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Texture Offset");
            EndOperation(op, UVOffset(overlay, u, v));
        }
    }

    public void _OnUVFlipButtonPressed(bool overlay, Cube.Orient flip) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Texture Flip");
            EndOperation(op, UVFlip(overlay, flip));
        }
    }

    public void _OnUVRotateButtonPressed(bool overlay, bool cw) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Texture Rotate");
            EndOperation(op, UVRotate(overlay, cw));
        }
    }

    public void _OnUVResetButtonPressed(bool overlay) {
        if (state.AnySelection) {
            undo.Push(state);
            var op = BeginOperation("Texture Reset");
            EndOperation(op, UVReset(overlay));
        }
    }

    public void _OnUndoPressed() {
        state = undo.Undo(state);
        UpdateState(true);
    }

    public void _OnRedoPressed() {
        state = undo.Redo(state);
        UpdateState(true);
    }

    public void _OnSavePressed() {
        if (filePath == null) {
            nGUI.ShowSaveDialog();
        } else {
            Save();
        }
    }

    public void _OnFileMenuItemPressed(int id) {
        switch (id) {
            case 0: // new indoor
                NewWorld(VOLUME_EMPTY, CubeVolume.SOLID);
                break;
            case 1: // new outdoor
                NewWorld(CubeVolume.SOLID, VOLUME_EMPTY);
                break;
            case 2: // open
                nGUI.ShowOpenDialog();
                break;
            case 3: // save
                nGUI.ShowSaveDialog();
                break;
            case 4: // delete
                nGUI.ShowDeleteDialog();
                break;
            case 5: // simplify
                undo.Push(state);
                var op = BeginOperation("Simplify"); // TODO shouldn't be undoable
                EndOperation(op, SimplifyWorld());
                break;
        }
    }

    public void _OnViewMenuItemPressed(int id, PopupMenu popup) {
        int idx = popup.GetItemIndex(id);
        bool check = !popup.IsItemChecked(idx);
        if (popup.IsItemCheckable(idx))
            popup.SetItemChecked(idx, check);
        switch (id) {
            case 0: // faces
                nCubeMesh.FacesVisible = check;
                UpdateState(true);
                break;
            case 1: // edge shadows
                nCubeMesh.EdgeShadowsVisible = check;
                UpdateState(true);
                break;
            case 2: // grid
                nCubeMesh.GridVisible = check;
                break;
            case 3: // edges
                nCubeMesh.EdgesVisible = check;
                UpdateState(true);
                break;
            case 4: // leaves
                nCubeMesh.DebugLeavesVisible = check;
                UpdateState(true);
                break;
            case 5: // stats
                nGUI.StatusVisible = check;
                break;
            case 99: // wireframe (TODO broken)
                GetViewport().DebugDraw = check ? Viewport.DebugDrawEnum.Wireframe
                    : Viewport.DebugDrawEnum.Disabled;
                break;
        }
    }

    public void _OnSaveFileSelected(string path) {
        filePath = ProjectSettings.GlobalizePath(path);
        Save();
    }

    public void _OnOpenFileSelected(string path) {
        Load(ProjectSettings.GlobalizePath(path));
    }

    public void _OnDeleteFileSelected(string path) {
        System.IO.File.Delete(ProjectSettings.GlobalizePath(path));
    }

    public void _OnDeleteDirSelected(string path) {
        System.IO.Directory.Delete(ProjectSettings.GlobalizePath(path), true);
    }

    // timers...

    private void _OnUpdatePerf() {
        nGUI.PerfText = $"{Engine.GetFramesPerSecond()} fps";
    }
}
