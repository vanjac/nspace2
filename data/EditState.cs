using Godot;

public enum SelectMode {
    Faces, Cubes
}

public struct EditState {
    public Immut<CubeWorld> world;
    public int editDepth;

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
    public bool HasSelection(SelectMode mode) {
        return selMode == mode && AnySelection;
    }
}
