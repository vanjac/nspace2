using Godot;
using System;
using System.Collections.Generic;

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
    [NodeRef("EditorCamera")] private EditorCamera nCam = null;
    [NodeRef("/root/Spatial/EditorGUI")] private EditorGUI nGUI;

    private Guid[] materialGuids;
    private Dictionary<Guid, Material> materialsDict = new Dictionary<Guid, Material>();

    private EditState state;
    private Undoer<EditState> undo = new Undoer<EditState>();
    private (string, ulong)? adjustOp;

    private CubePos selStartMin, selStartMax;
    private CubePos mark;

    private string filePath;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
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
            new Godot.Collections.Array { nGUI.nViewPopup });
        nGUI.nTabContainer.Connect("tab_changed", this, nameof(_OnTabChanged));
        nGUI.nEmptyVolume.Connect("pressed", this, nameof(_OnVolumeButtonPressed),
            new Godot.Collections.Array { VOLUME_EMPTY.ToString() });
        nGUI.nSolidVolume.Connect("pressed", this, nameof(_OnVolumeButtonPressed),
            new Godot.Collections.Array { Volume.SOLID.ToString() });
        nGUI.nFluidVolume.Connect("pressed", this, nameof(_OnVolumeButtonPressed),
            new Godot.Collections.Array { "39c2ca46-e6ce-4e0c-b851-295a7a5921b2" });
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
            instance.Connect("pressed", this, nameof(_OnBaseMatButtonPressed),
                new Godot.Collections.Array { i });
            nGUI.nMaterialsGrid.AddChild(instance);
        }

        materialGuids[nBuiltIn.baseMaterials.Length] = CubeMaterial.DEFAULT_OVERLAY;
        materialsDict[CubeMaterial.DEFAULT_OVERLAY] = nBuiltIn.defaultOverlay;

        NewWorld(VOLUME_EMPTY, Volume.SOLID);
    }

    private void UpdateState(bool updateWorld) {
        Transform = state.world.Val.transform;
        if (updateWorld)
            UpdateWorld();
        UpdateSelection();
        UpdateAdjustPos();
        nCam.Update(state);

        nExtrudeAdjust.Update();
        nMoveAdjustX.Update();
        nMoveAdjustY.Update();
        nMoveAdjustZ.Update();

        nGUI.UpdateState(state, undo);
        nCubeMesh.GridSize = CubeUtil.WorldCubeSize(state.editDepth);
        nDebugCube.Translation = state.world.Val.rootPos.ToWorldPos();
        nDebugCube.Scale = Vector3.One * CubeUtil.WorldCubeSize(state.world.Val.rootDepth);
    }

    private void UpdateWorld() {
        var w = state.world.Val;
        ulong startTick = Time.GetTicksMsec();
        var stats = nCubeMesh.UpdateMesh(w.root, w.rootPos.ToWorldPos(),
            CubeUtil.WorldCubeSize(w.rootDepth), w.voidVolume, materialsDict);
        nGUI.StatsText =
            $"{stats.branches} br {stats.quads} qu {Time.GetTicksMsec() - startTick} ms";
    }

    private void UpdateSelection() {
        nRectSelection.Visible = false;
        nBoxSelection.Visible = false;
        if (state.HasSelection(SelectMode.Cubes)) {
            nBoxSelection.Visible = true;
            nBoxSelection.Translation = state.selMin.ToWorldPos();
            nBoxSelection.Scale = (state.selMax - state.selMin).ToWorldSize();
        } else if (state.HasSelection(SelectMode.Faces)) {
            nRectSelection.Visible = true;
            nRectSelection.Translation = state.selMin.ToWorldPos();
            nRectSelection.Rotation = AxisRotation(state.selAxis);
            int s = state.selAxis + 1, t = state.selAxis + 2;
            Vector3 size = (state.selMax - state.selMin).ToWorldSize();
            size = CubeUtil.CycleVector(size, 5 - state.selAxis);
            size.z = state.selDir ? 1 : -1;
            nRectSelection.Scale = size;
        }
    }

    private void UpdateAdjustPos() {
        nExtrudeAdjust.Enabled = false;
        nMoveAdjustX.Enabled = nMoveAdjustY.Enabled = nMoveAdjustZ.Enabled = false;
        Vector3 center = (state.selMin.ToWorldPos() + state.selMax.ToWorldPos()) / 2;
        float snap = CubeUtil.WorldCubeSize(state.editDepth);
        if (state.HasSelection(SelectMode.Cubes)) {
            nMoveAdjustX.Enabled = nMoveAdjustY.Enabled = nMoveAdjustZ.Enabled = true;
            nMoveAdjust.Translation = center;
            nMoveAdjustX.snap = nMoveAdjustY.snap = nMoveAdjustZ.snap = snap;
        } else if (state.HasSelection(SelectMode.Faces)) {
            nExtrudeAdjust.Enabled = true;
            nExtrudeAdjust.Translation = center;
            nExtrudeAdjust.Rotation = AxisRotation(state.selAxis, state.selDir);
            nExtrudeAdjust.snap = snap;
        }
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
        var size = CubePos.CubeSize(state.editDepth);
        return CubePos.FromAxis(axis + 1, size) + CubePos.FromAxis(axis + 2, size);
    }

    private IEnumerable<CubePos> IterateSelectedCubes() {
        if (!state.HasSelection(SelectMode.Cubes))
            yield break;
        uint size = CubePos.CubeSize(state.editDepth);
        var pos = CubePos.ZERO;
        for (pos[2] = state.selMin[2]; pos[2] < state.selMax[2]; pos[2] += size) {
            for (pos[1] = state.selMin[1]; pos[1] < state.selMax[1]; pos[1] += size) {
                for (pos[0] = state.selMin[0]; pos[0] < state.selMax[0]; pos[0] += size) {
                    yield return pos;
                }
            }
        }
    }

    private IEnumerable<CubePos> IterateSelectedFaces() {
        if (!state.HasSelection(SelectMode.Faces))
            yield break;
        int s = state.selAxis + 1, t = state.selAxis + 2;
        uint size = CubePos.CubeSize(state.editDepth);
        var pos = state.selMin;
        for (pos[s] = state.selMin[s]; pos[s] < state.selMax[s]; pos[s] += size) {
            for (pos[t] = state.selMin[t]; pos[t] < state.selMax[t]; pos[t] += size) {
                yield return pos;
            }
        }
    }

    private void SelectStart(CubePos pos, int axis, bool dir) {
        var posMax = pos + FaceSize(axis);
        if (state.AnySelection
                && (nGUI.AddSelectEnabled ^ Input.IsKeyPressed((int)KeyList.Shift))) {
            if (state.selMode == SelectMode.Faces && axis != state.selAxis)
                return;
            (selStartMin, selStartMax) = SelectExpand(state.selMin, state.selMax, pos, posMax);
        } else {
            state.selAxis = axis;
            state.selDir = dir;
            (selStartMin, selStartMax) = (pos, posMax);
        }
        state.selMin = selStartMin;
        state.selMax = selStartMax;
    }

    private void SelectDrag(CubePos pos, int axis) {
        if (!state.AnySelection || (state.selMode == SelectMode.Faces && axis != state.selAxis))
            return;
        var posMax = pos + FaceSize(axis);
        (state.selMin, state.selMax) = SelectExpand(selStartMin, selStartMax, pos, posMax);
    }

    private (CubePos, CubePos) SelectExpand(CubePos oldMin, CubePos oldMax,
            CubePos addMin, CubePos addMax) {
        for (int i = 0; i < 3; i++) {
            if (state.selMode == SelectMode.Faces && i == state.selAxis)
                continue;
            if (addMin[i] < oldMin[i]) oldMin[i] = addMin[i];
            if (addMax[i] > oldMax[i]) oldMax[i] = addMax[i];
        }
        return (oldMin, oldMax);
    }

    private void MoveSelection(int axis, bool dir) {
        CubePos move = CubePos.FromAxis(axis, CubePos.CubeSize(state.editDepth), dir);
        state.selMin += move;
        state.selMax += move;
    }

    private bool ExpandIncludeCubeSelection(CubePos offset) {
        var cubeSize = CubePos.CubeSize(state.editDepth);
        bool modified = false;
        foreach (CubePos cubePos in IterateSelectedCubes()) {
            var testPos = cubePos + offset;
            state.world = Util.AssignChanged(state.world, CubeEdit.ExpandWorld(state.world,
                new CubePos[] { testPos, testPos + new CubePos(cubeSize) }), ref modified);
        }
        return modified;
    }

    private bool Extrude(bool pull) {
        if (!state.HasSelection(SelectMode.Faces))
            return false;
        var cubeSize = CubePos.CubeSize(state.editDepth);
        bool dir = !pull ^ state.selDir;

        bool worldModified = false;
        foreach (CubePos facePos in IterateSelectedFaces()) {
            var testPos = dir ? facePos : (facePos - CubePos.FromAxis(state.selAxis, cubeSize));
            state.world = Util.AssignChanged(state.world, CubeEdit.ExpandWorld(state.world,
                new CubePos[] { testPos, testPos + new CubePos(cubeSize) }), ref worldModified);
        }

        CubeWorld w = state.world.Val;
        Cube oldRoot = w.root;
        bool rootModified = false;
        foreach (CubePos facePos in IterateSelectedFaces()) {
            w.root = Util.AssignChanged(w.root, CubeEdit.Extrude(oldRoot, w.root,
                facePos.ToRoot(w), state.RootEditDepth, state.selAxis, dir),
                ref rootModified);
        }
        if (rootModified)
            state.world = Immut.Create(w);

        MoveSelection(state.selAxis, dir);
        return worldModified | rootModified;
    }

    private bool Move(int axis, bool dir) {
        // TODO lots of copied code from Extrude
        if (!state.HasSelection(SelectMode.Cubes))
            return false;
        CubePos axisOff = CubePos.FromAxis(axis, CubePos.CubeSize(state.editDepth), dir);
        bool worldModified = ExpandIncludeCubeSelection(axisOff);

        CubeWorld w = state.world.Val;
        Cube oldRoot = w.root;
        bool rootModified = false;
        foreach (CubePos cubePos in IterateSelectedCubes()) {
            w.root = Util.AssignChanged(w.root,
                CubeEdit.TransferCube(
                    oldRoot, cubePos.ToRoot(w), state.RootEditDepth,
                    w.root, (cubePos + axisOff).ToRoot(w), state.RootEditDepth),
                    ref rootModified);
            if (!state.IsSelected(cubePos - axisOff)) {
                CubePos facePos = dir ? cubePos : (cubePos - axisOff);
                w.root = Util.AssignChanged(w.root, CubeEdit.Extrude(oldRoot, w.root,
                    facePos.ToRoot(w), state.RootEditDepth, axis, dir),
                    ref rootModified);
            }
        }
        if (rootModified)
            state.world = Immut.Create(w);

        MoveSelection(axis, dir);
        return worldModified | rootModified;
    }

    private bool SetVolume(Guid volume) {
        if (!state.HasSelection(SelectMode.Cubes))
            return false;
        bool worldModified = ExpandIncludeCubeSelection(CubePos.ZERO);
        CubeWorld w = state.world.Val;
        bool rootModified = false;
        foreach (CubePos cubePos in IterateSelectedCubes()) {
            w.root = Util.AssignChanged(w.root, CubeEdit.PutVolumes(w.root,
                cubePos.ToRoot(w), state.RootEditDepth, volume), ref rootModified);
        }
        if (rootModified)
            state.world = Immut.Create(w);
        return worldModified | rootModified;
    }

    private bool Paint(Immut<Cube.Face> face) {
        if (!state.AnySelection)
            return false;
        CubeWorld w = state.world.Val;
        bool modified = false;
        if (state.selMode == SelectMode.Faces) {
            foreach (CubePos facePos in IterateSelectedFaces()) {
                if (!facePos.InCube(w.rootPos, w.rootDepth))
                    continue;
                w.root = Util.AssignChanged(w.root, CubeEdit.PutFaces(w.root,
                    facePos.ToRoot(w), state.RootEditDepth, state.selAxis, face),
                    ref modified);
            }
        } else if (state.selMode == SelectMode.Cubes) {
            var cubeSize = CubePos.CubeSize(state.editDepth);
            foreach (CubePos cubePos in IterateSelectedCubes()) {
                if (!cubePos.InCube(w.rootPos, w.rootDepth))
                    continue;
                w.root = Util.AssignChanged(w.root, CubeEdit.PutFaces(w.root,
                    cubePos.ToRoot(w), state.RootEditDepth, face), ref modified);
                for (int axis = 0; axis < 3; axis++) {
                    CubePos adjPos = cubePos + CubePos.FromAxis(axis, cubeSize);
                    if (!state.IsSelected(adjPos)) {
                        w.root = Util.AssignChanged(w.root, CubeEdit.PutFaces(w.root,
                            adjPos.ToRoot(w), state.RootEditDepth, axis, face), ref modified);
                    }
                }
            }
        }
        if (modified)
            state.world = Immut.Create(w);
        return modified;
    }

    private bool Paste(CubePos fromPos) {
        if (!state.HasSelection(SelectMode.Cubes))
            return false;
        ExpandIncludeCubeSelection(CubePos.ZERO);
        Cube oldRoot = state.world.Val.root;
        CubeWorld w = state.world.Val;
        bool modified = false;
        foreach (CubePos cubePos in IterateSelectedCubes()) {
            w.root = Util.AssignChanged(w.root, CubeEdit.TransferCube(
                oldRoot, (cubePos - state.selMin + fromPos).ToRoot(w), state.RootEditDepth,
                w.root, cubePos.ToRoot(w), state.RootEditDepth), ref modified);
        }
        if (modified)
            state.world = Immut.Create(w);
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
        var world = Immut.Create(new CubeWorld {
            root = new Cube.Branch {
                children = (inner, outer, outer, outer, outer, outer, outer, outer)
            }.Immut(),
            rootDepth = CubeWorld.UNIT_DEPTH - 3,
            rootPos = CubePos.HALF,
            transform = Transform.Identity,
            voidVolume = outerVolume
        });
        state = new EditState {
            world = world,
            editDepth = CubeWorld.UNIT_DEPTH,
            camFocus = new Vector3(2, 2, 2),
            camZoom = 6f,
        };
        undo.Clear();
        adjustOp = null;
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
        adjustOp = null;
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
        if (adjustOp == null)
            adjustOp = BeginOperation("Extrude");
        bool modified = false;
        for (; units > 0; units--)
            modified |= Extrude(true);
        for (; units < 0; units++)
            modified |= Extrude(false);
        UpdateState(modified);
    }

    public void _OnMoveAdjust(int units, int axis) {
        if (nGUI.MoveEnabled) {
            if (adjustOp == null)
                adjustOp = BeginOperation("Move");
            bool modified = false;
            for (; units > 0; units--)
                modified |= Move(axis, true);
            for (; units < 0; units++)
                modified |= Move(axis, false);
            UpdateState(modified);
        } else {
            for (; units > 0; units--)
                MoveSelection(axis, true);
            for (; units < 0; units++)
                MoveSelection(axis, false);
            UpdateState(false);
        }
    }

    public void _OnAdjustEnd() {
        if (adjustOp != null)
            EndOperation(adjustOp.Value, false);
        adjustOp = null;
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

    public void _OnTabChanged(int tab) {
        state.selMode = (SelectMode)tab;
        state.ClearSelection();
        UpdateState(false);
    }

    public void _OnCopyPressed() {
        if (state.HasSelection(SelectMode.Cubes))
            mark = state.selMin;
    }

    public void _OnPastePressed() {
        var op = BeginOperation("Paste");
        EndOperation(op, Paste(mark));
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
                NewWorld(VOLUME_EMPTY, Volume.SOLID);
                break;
            case 1: // new outdoor
                NewWorld(Volume.SOLID, VOLUME_EMPTY);
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
