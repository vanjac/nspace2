using Godot;

public class EditorGUI : Node {
    [NodeRef("Toolbar/AddSelect")] public Button nAddSelect = null;
    [NodeRef("Toolbar/MoveSelect")] public Button nMoveSelect = null;
    [NodeRef("Toolbar/Grid/HBox/Size")] public Label nGridSize = null;
    [NodeRef("Toolbar/Grid/HBox/Half")] public Button nGridHalf = null;
    [NodeRef("Toolbar/Grid/HBox/Double")] public Button nGridDouble = null;
    [NodeRef("Toolbar/Undo")] public Button nUndo = null;
    [NodeRef("Toolbar/Redo")] public Button nRedo = null;
    [NodeRef("Toolbar/Save")] public Button nSave = null;
    [NodeRef("Toolbar/File")] public MenuButton nFile = null;
    public PopupMenu nFilePopup;
    [NodeRef("Toolbar/View")] public MenuButton nView = null;
    public PopupMenu nViewPopup;
    [NodeRef("Status")] public Container nStatus = null;
    [NodeRef("Status/Stats")] public Label nStats = null;
    [NodeRef("Status/Operation")] public Label nOperation = null;
    [NodeRef("Status/Perf")] public Label nPerf = null;
    [NodeRef("TabContainer/Edit/Volumes/Empty")] public Button nEmptyVolume = null;
    [NodeRef("TabContainer/Edit/Volumes/Solid")] public Button nSolidVolume = null;
    [NodeRef("TabContainer/Edit/Volumes/Fluid")] public Button nFluidVolume = null;
    [NodeRef("TabContainer/Edit/Copy")] public Button nCopy = null;
    [NodeRef("TabContainer/Edit/Paste")] public Button nPaste = null;
    [NodeRef("TabContainer/Edit/SaveClip")] public Button nSaveClip = null;
    [NodeRef("TabContainer/Edit/Clips/Scroll/VBox")] public Container nClipsList = null;
    [NodeRef("TabContainer/Paint/Scroll/Grid")] public Container nMaterialsGrid = null;
    [NodeRef("TabContainer/Paint/UV/OffsetUp")] public Button nUVOffsetUp = null;
    [NodeRef("TabContainer/Paint/UV/OffsetDown")] public Button nUVOffsetDown = null;
    [NodeRef("TabContainer/Paint/UV/OffsetLeft")] public Button nUVOffsetLeft = null;
    [NodeRef("TabContainer/Paint/UV/OffsetRight")] public Button nUVOffsetRight = null;
    [NodeRef("SaveDialog")] public FileDialog nSaveDialog = null;
    [NodeRef("OpenDialog")] public FileDialog nOpenDialog = null;
    [NodeRef("DeleteDialog")] public FileDialog nDeleteDialog = null;
    [NodeRef("ClipNameDialog")] public AcceptDialog nClipNameDialog = null;
    [NodeRef("ClipNameDialog/LineEdit")] public LineEdit nClipName = null;
    private PackedScene sMatButton, sClipGroup;

    public bool AddSelectEnabled {
        get => nAddSelect.Pressed;
        set => nAddSelect.Pressed = false;
    }
    public bool MoveSelectEnabled => nMoveSelect.Pressed;
    public string ClipName => nClipName.Text;
    public bool StatusVisible { set => nStatus.Visible = value; }
    public string StatsText { set => nStats.Text = value; }
    public string PerfText { set => nPerf.Text = value; }
    public string OperationText { set => nOperation.Text = value; }

    public void Init() {
        NodeRefAttribute.GetAllNodes(this);
        nFilePopup = nFile.GetPopup();
        nViewPopup = nView.GetPopup();
        sMatButton = GD.Load<PackedScene>("res://edit/MaterialButton.tscn");
        sClipGroup = GD.Load<PackedScene>("res://edit/ClipGroup.tscn");
    }

    public void UpdateState(EditState state, Undoer<EditState> undo) {
        nGridSize.Text = DepthString(state.editDepth);
        nUndo.Disabled = !undo.CanUndo();
        nRedo.Disabled = !undo.CanRedo();
    }

    private string DepthString(int depth) {
        float size = CubeUtil.ModelCubeSize(depth);
        if (size < 1)
            return $"1 / {(int)(1 / size)}";
        else
            return size.ToString();
    }

    public Button AddMaterialButton(Texture texture) {
        var instance = sMatButton.Instance<Button>();
        instance.Icon = texture;
        nMaterialsGrid.AddChild(instance);
        return instance;
    }

    public (Button pasteButton, Button deleteButton) AddClipGroup(string id, string name) {
        var clipScene = sClipGroup.Instance();
        clipScene.Name = id;
        var pasteButton = clipScene.GetNode<Button>("Paste");
        pasteButton.Text = name;
        var deleteButton = clipScene.GetNode<Button>("Delete");
        nClipsList.AddChild(clipScene);
        return (pasteButton, deleteButton);
    }

    public void RemoveClipGroup(string id) {
        nClipsList.GetNode(id).QueueFree();
    }

    public void ShowOpenDialog() {
        nOpenDialog.Invalidate();
        nOpenDialog.PopupCentered();
    }

    public void ShowSaveDialog() {
        nSaveDialog.Invalidate();
        nSaveDialog.PopupCentered();
    }

    public void ShowDeleteDialog() {
        nDeleteDialog.Invalidate();
        nDeleteDialog.PopupCentered();
    }

    public void ShowClipNameDialog() {
        nClipNameDialog.PopupCentered();
        nClipName.Text = "";
        nClipName.GrabFocus();
    }
}
