using Godot;

public enum SelectMode {
    Faces, Cubes
}

public struct EditState {
    public Immut<CubeModel> world;
    public int editDepth;
    public int RootEditDepth => editDepth - world.Val.rootDepth;

    public Vector3 camFocus;
    public float camYaw, camPitch, camZoom;

    public SelectMode selMode;
    public CubePos selMin, selMax; // no selection if equal
    public int selAxis; // Faces only
    public bool selDir; // Faces only
    public bool AnySelection => !selMin.Equals(selMax);
    public void ClearSelection() {
        selMax = selMin;
    }
    public bool HasSelection(SelectMode mode) => selMode == mode && AnySelection;
    public bool IsSelected(CubePos pos) {
        for (int i = 0; i < 3; i++) {
            if (selMode == SelectMode.Faces && i == selAxis) {
                if (pos[i] != selMin[i]) return false;
            }
            else if (pos[i] < selMin[i]) return false;
            else if (pos[i] >= selMax[i]) return false;
        }
        return true;
    }
}
