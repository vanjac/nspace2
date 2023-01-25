using Godot;

public class EditorCamera : Spatial {
    [NodeRef("Pitch")] private Spatial nPitch = null;
    [NodeRef("Pitch/Camera")] private Camera nCamera = null;

    public Vector3 GlobalCamPosition => nCamera.GlobalTranslation;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
    }

    public void Update(EditState state) {
        Translation = state.camFocus;
        Scale = Vector3.One * state.camZoom;
        Rotation = new Vector3(0, state.camYaw, 0);
        nPitch.Rotation = new Vector3(state.camPitch, 0, 0);
    }
}
