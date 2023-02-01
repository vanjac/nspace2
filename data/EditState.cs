using Godot;

public struct EditState {
    public Immut<CubeModel> world;
    public int editDepth;
    public int RootEditDepth => editDepth - world.Val.rootDepth;

    public Vector3 camFocus;
    public float camYaw, camPitch, camZoom;

    public CubePos selMin, selMax; // no selection if equal
    public int selAxis; // Faces only
    public bool selDir; // Faces only
    public bool AnySelection => !selMin.Equals(selMax);
    public void ClearSelection() {
        selMax = selMin;
    }
}
