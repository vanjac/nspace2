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
    [NodeRef("ExtrudeAdjust")] private AdjustHandle nExtrudeAdjust = null;
    [NodeRef("MoveAdjust")] private Spatial nMoveAdjust = null;
    [NodeRef("MoveAdjust/X")] private AdjustHandle nMoveAdjustX = null;
    [NodeRef("MoveAdjust/Y")] private AdjustHandle nMoveAdjustY = null;
    [NodeRef("MoveAdjust/Z")] private AdjustHandle nMoveAdjustZ = null;
    private AdjustHandle[] nMoveAdjustAxes;
    [NodeRef("EditorCamera")] private EditorCamera nCam = null;
    [NodeRef("/root/Spatial/EditorGUI")] private EditorGUI nGUI;

    private struct Clipping {
        public Cube root;
        public CubePos min, max; // in root coordinates!
        public int rootDepth;
    }

    private Guid[] materialGuids;
    private Dictionary<Guid, Material> materialsDict = new Dictionary<Guid, Material>();

    private EditState state;
    private Undoer<EditState> undo = new Undoer<EditState>();
    private Stack<Immut<CubeModel>> adjustPos = new Stack<Immut<CubeModel>>();
    private Stack<Immut<CubeModel>> adjustNeg = new Stack<Immut<CubeModel>>();
    private (string, ulong)? adjustOp;

    private CubePos selStartMin, selStartMax;
    private Clipping clipboard;

    private string filePath;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        nMoveAdjustAxes = new AdjustHandle[] { nMoveAdjustX, nMoveAdjustY, nMoveAdjustZ };
        nGUI.Init();

        // https://www.reddit.com/r/godot/comments/d459x2/finally_figured_out_how_to_enable_wireframes_in/
        VisualServer.SetDebugGenerateWireframes(true);

        nGUI.nGridHalf.Connect("pressed", this, nameof(_OnGridHalfPressed));
        nGUI.nGridDouble.Connect("pressed", this, nameof(_OnGridDoublePressed));
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
        nGUI.nSaveDialog.Connect("file_selected", this, nameof(_OnSaveFileSelected));
        nGUI.nOpenDialog.Connect("file_selected", this, nameof(_OnOpenFileSelected));
        nGUI.nDeleteDialog.Connect("file_selected", this, nameof(_OnDeleteFileSelected));
        nGUI.nDeleteDialog.Connect("dir_selected", this, nameof(_OnDeleteDirSelected));

        var matButtonScene = GD.Load<PackedScene>("res://edit/MaterialButton.tscn");

        materialGuids = new Guid[nBuiltIn.baseMaterials.Length + 1];
        for (int i = 0; i < nBuiltIn.baseMaterials.Length; i++) {
            Resource matInfo = nBuiltIn.baseMaterials[i];

            SpatialMaterial material = new SpatialMaterial();
            Texture texture = (Texture)matInfo.Get("texture");
            material.AlbedoTexture = texture;
            Vector2 textureScale = (Vector2)matInfo.Get("texture_scale");
            material.Uv1Scale = new Vector3(textureScale.x, textureScale.y, 1);

            Guid guid = new Guid((string)matInfo.Get("guid"));
            if (guid == Guid.Empty)
                throw new Exception("Missing material GUID");
            if (materialsDict.ContainsKey(guid))
                throw new Exception("Duplicate material GUIDs");
            materialGuids[i] = guid;
            materialsDict[guid] = material;

            var instance = matButtonScene.Instance<Button>();
            instance.Icon = texture;
            instance.Connect("pressed", this, nameof(_OnBaseMatButtonPressed), new GDArray { i });
            nGUI.nMaterialsGrid.AddChild(instance);
        }

        materialGuids[nBuiltIn.baseMaterials.Length] = CubeMaterial.DEFAULT_OVERLAY;
        materialsDict[CubeMaterial.DEFAULT_OVERLAY] = nBuiltIn.defaultOverlay;

        NewWorld(VOLUME_EMPTY, CubeVolume.SOLID);
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
            nRectSelection.Rotation = AxisRotation(axis);
            int s = axis + 1, t = axis + 2;
            Vector3 size = selSize.ToModelSize();
            size = CubeUtil.CycleVector(size, 5 - axis);
            size.z = state.selDir ? 1 : -1;
            nRectSelection.Scale = size;
        }
    }

    private void UpdateAdjustPos() {
        foreach (var adjust in nMoveAdjustAxes)
            adjust.Enabled = state.AnySelection;

        if ((state.selMax - state.selMin).Dimension() == 2) {
            nExtrudeAdjust.Enabled = true;
            nExtrudeAdjust.Rotation = AxisRotation(state.selAxis, state.selDir);
            nMoveAdjustAxes[state.selAxis].Enabled = false;
        } else {
            nExtrudeAdjust.Enabled = false;
        }
        if (!state.AnySelection)
            return;

        Vector3 center = (state.selMin.ToModelPos() + state.selMax.ToModelPos()) / 2;
        nMoveAdjust.Translation = center;
        nExtrudeAdjust.Translation = center;

        float snap = CubeUtil.ModelCubeSize(state.editDepth);
        nExtrudeAdjust.snap = snap;
        foreach (var adjust in nMoveAdjustAxes)
            adjust.snap = snap;
    }

    private Vector3 AxisRotation(int axis, bool dir = true) {
        float hpi = Mathf.Pi / 2;
        float hpi3 = Mathf.Pi * 3 / 2;
        switch ((axis, dir)) {
            case (0, false): return new Vector3(0, hpi3, hpi3);
            case (1, false): return new Vector3(-hpi3, -hpi3, 0);
            case (2, false): return new Vector3(0, Mathf.Pi, 0);
            case (0, true): return new Vector3(0, hpi, hpi);
            case (1, true): return new Vector3(-hpi, -hpi, 0);
            default: return new Vector3(0, 0, 0);
        }
    }

    private CubePos FaceSize(int axis) {
        return CubePos.FromAxisSize(axis + 1, state.editDepth)
             + CubePos.FromAxisSize(axis + 2, state.editDepth);
    }

    private void SelectStart(CubePos pos, int axis, bool dir) {
        var posMax = pos + FaceSize(axis);
        if (state.AnySelection
                && (nGUI.AddSelectEnabled ^ Input.IsKeyPressed((int)KeyList.Shift))) {
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
        CubePos move = CubePos.FromAxisSize(axis, state.editDepth, count);
        state.selMin += move;
        state.selMax += move;
    }

    private bool ExpandIncludeSelection(CubePos offset) {
        bool modified = false;
        state.world = Util.AssignChanged(state.world, CubeEdit.ExpandModel(state.world,
            new CubePos[] { state.selMin + offset, state.selMax + offset }), ref modified);
        return modified;
    }

    private bool Move(int axis, bool dir) {
        if (!state.AnySelection)
            return false;
        CubePos axisOff = CubePos.FromAxisSize(axis, state.editDepth, dir ? 1 : -1);
        bool worldModified = ExpandIncludeSelection(axisOff);

        CubeModel m = state.world.Val;
        Cube oldRoot = m.root;
        bool rootModified = false;
        m.root = Util.AssignChanged(m.root, CubeEdit.TransferBox(
            oldRoot, state.selMin.ToRoot(m), state.selMax.ToRoot(m),
            m.root, (state.selMin + axisOff).ToRoot(m), 0), ref rootModified);
        m.root = Util.AssignChanged(m.root, CubeEdit.ExtrudeRect(
            oldRoot, m.root, state.selMin.ToRoot(m), state.selMax.ToRoot(m),
            state.RootEditDepth, axis, dir), ref rootModified);
        if (rootModified)
            state.world = Immut.Create(m);

        return worldModified | rootModified;
    }

    private bool SetVolume(Guid volume) {
        if (!state.AnySelection)
            return false;
        bool worldModified = ExpandIncludeSelection(CubePos.ZERO);
        CubeModel m = state.world.Val;
        bool rootModified = false;
        m.root = Util.AssignChanged(m.root, CubeEdit.PutVolumes(m.root,
            state.selMin.ToRoot(m), state.selMax.ToRoot(m), volume), ref rootModified);
        if (rootModified)
            state.world = Immut.Create(m);
        return worldModified | rootModified;
    }

    private bool Paint(Immut<Cube.Face> face) {
        if (!state.AnySelection)
            return false;
        CubeModel m = state.world.Val;
        bool modified = false;
        m.root = Util.AssignChanged(m.root, CubeEdit.PutFaces(
            m.root, state.selMin.ToRootClamped(m), state.selMax.ToRootClamped(m), face),
            ref modified);
        if (modified)
            state.world = Immut.Create(m);
        return modified;
    }

    private void Copy() {
        if (!state.AnySelection)
            return;
        var m = CubeEdit.ExpandModel(state.world, new CubePos[] { state.selMin, state.selMax }).Val;
        clipboard = new Clipping {
            root = m.root,
            min = state.selMin.ToRoot(m),
            max = state.selMax.ToRoot(m),
            rootDepth = m.rootDepth,
        };
    }

    private bool Paste(Clipping clip) {
        if (!state.AnySelection)
            return false;
        ExpandIncludeSelection(CubePos.ZERO);
        CubeModel m = state.world.Val;
        CubePos pasteMin = state.selMin.ToRoot(m), pasteMax = state.selMax.ToRoot(m);
        bool modified = false;

        // fill selection with repeated copies
        CubePos pastePos = pasteMin;
        bool loopCond(int axis) => pastePos[axis] < pasteMax[axis]
            || (pasteMin[axis] == pasteMax[axis] && pastePos[axis] == pasteMax[axis]);
        void loopStep(int axis) => pastePos[axis] = (clip.min[axis] == clip.max[axis]) ?
            uint.MaxValue : (pastePos[axis] + (clip.max[axis] - clip.min[axis]));
        for (pastePos[2] = pasteMin[2]; loopCond(2); loopStep(2)) {
            for (pastePos[1] = pasteMin[1]; loopCond(1); loopStep(1)) {
                for (pastePos[0] = pasteMin[0]; loopCond(0); loopStep(0)) {
                    m.root = Util.AssignChanged(m.root, CubeEdit.TransferBox(clip.root,
                        clip.min, CubePos.Min(clip.max, clip.min + (pasteMax - pastePos)),
                        m.root, pastePos, clip.rootDepth - m.rootDepth), ref modified);
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
        var startTick = Time.GetTicksMsec();
        using (var stream = System.IO.File.Open(path, System.IO.FileMode.Open)) {
            try {
                state = new CubeDeserialize().ReadFile(stream);
            } catch (Exception e) {
                GD.Print(e);
                return;
            }
        }
        GD.Print($"Load took {Time.GetTicksMsec() - startTick}ms");
        undo.Clear();
        ResetAdjust();
        filePath = path;
        UpdateState(true);
    }

    private void Save() {
        var op = BeginOperation("Save");
        bool modified = SimplifyWorld();
        using (var stream = System.IO.File.Open(filePath, System.IO.FileMode.Create)) {
            new CubeSerialize().WriteFile(stream, state);
        }
        EndOperation(op, modified);
    }

    private (string name, ulong startTick) BeginOperation(string name) {
        undo.Push(state);
        CubeDebug.allocCount = 0;
        return (name, Time.GetTicksMsec());
    }

    private void EndOperation((string name, ulong startTick) op, bool modified) {
        GD.Print($"{op.name} took {Time.GetTicksMsec() - op.startTick}ms"
            + $" and created {CubeDebug.allocCount} cubes");
        UpdateState(modified);
    }

    private void ResetAdjust() {
        adjustOp = null;
        adjustPos.Clear();
        adjustNeg.Clear();
    }

    private void MoveAdjust(int axis, int units) {
        if (!nGUI.MoveEnabled) {
            MoveSelection(axis, units);
            UpdateState(false);
            return;
        }

        if (adjustOp == null)
            adjustOp = BeginOperation("Move");
        for (; units > 0; units--) {
            if (adjustNeg.Count != 0) {
                state.world = adjustNeg.Pop();
            } else {
                adjustPos.Push(state.world);
                Move(axis, true);
            }
            MoveSelection(axis, 1);
        }
        for (; units < 0; units++) {
            if (adjustPos.Count != 0) {
                state.world = adjustPos.Pop();
            } else {
                adjustNeg.Push(state.world);
                Move(axis, false);
            }
            MoveSelection(axis, -1);
        }
        UpdateState(true);
    }

    /* SIGNAL RECEIVERS */

    // TouchController...

    public void _OnSelectClear() {
        state.ClearSelection();
        UpdateState(false);
    }

    public void _OnSelectStart(Vector3 pos, Vector3 normal) {
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
        if (adjustOp != null)
            EndOperation(adjustOp.Value, false);
        ResetAdjust();
    }

    // GUI...

    public void _OnGridHalfPressed() {
        state.editDepth += 1;
        UpdateState(false);
    }

    public void _OnGridDoublePressed() {
        state.editDepth -= 1;
        state.ClearSelection();
        UpdateState(false);
    }

    public void _OnCopyPressed() {
        Copy();
    }

    public void _OnPastePressed() {
        var op = BeginOperation("Paste");
        EndOperation(op, Paste(clipboard));
    }

    public void _OnVolumeButtonPressed(string guid) {
        var op = BeginOperation("Volumes");
        EndOperation(op, SetVolume(new Guid(guid)));
    }

    public void _OnBaseMatButtonPressed(int index) {
        var op = BeginOperation("Paint");
        EndOperation(op, Paint(Immut.Create(new Cube.Face {
            base_ = new Cube.Layer { material = materialGuids[index] },
            overlay = new Cube.Layer { material = CubeMaterial.DEFAULT_OVERLAY }
        })));
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
            case 5: // wireframe (TODO broken)
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
