using Godot;

public class EditorGUI : Node {
    [NodeRef("Toolbar/AddSelect")] public Button nAddSelect = null;
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
    [NodeRef("Toolbar/Stats/Label")] public Label nStats = null;
    [NodeRef("Toolbar/Perf/Label")] public Label nPerf = null;
    [NodeRef("LeftPanel/TabContainer")] public TabContainer nTabContainer = null;
    [NodeRef("LeftPanel/TabContainer/Cubes/Volumes/Empty")] public Button nEmptyVolume = null;
    [NodeRef("LeftPanel/TabContainer/Cubes/Volumes/Solid")] public Button nSolidVolume = null;
    [NodeRef("LeftPanel/TabContainer/Cubes/Volumes/Fluid")] public Button nFluidVolume = null;
    [NodeRef("LeftPanel/TabContainer/Cubes/Move")] public Button nMove = null;
    [NodeRef("LeftPanel/TabContainer/Cubes/Copy")] public Button nCopy = null;
    [NodeRef("LeftPanel/TabContainer/Cubes/Paste")] public Button nPaste = null;
    [NodeRef("LeftPanel/Materials/Grid")] public Container nMaterialsGrid = null;
    [NodeRef("SaveDialog")] public FileDialog nSaveDialog = null;
    [NodeRef("OpenDialog")] public FileDialog nOpenDialog = null;
    [NodeRef("DeleteDialog")] public FileDialog nDeleteDialog = null;

    public bool AddSelectEnabled => nAddSelect.Pressed;
    public bool MoveEnabled => nMove.Pressed;
    public string StatsText { set => nStats.Text = value; }
    public string PerfText { set => nPerf.Text = value; }

    public void Init() {
        NodeRefAttribute.GetAllNodes(this);
        nFilePopup = nFile.GetPopup();
        nViewPopup = nView.GetPopup();
    }

    public void UpdateState(EditState state, Undoer<EditState> undo) {
        nGridSize.Text = DepthString(state.editDepth);
        nTabContainer.CurrentTab = (int)state.selMode;
        nUndo.Disabled = !undo.CanUndo();
        nRedo.Disabled = !undo.CanRedo();
    }

    private string DepthString(int depth) {
        float size = CubeUtil.WorldCubeSize(depth);
        if (size < 1)
            return $"1 / {(int)(1 / size)}";
        else
            return size.ToString();
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
}