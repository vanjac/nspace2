using Godot;

public class ResizeHandle : Spatial, AdjustHandle {
    private const float SCALE_MULT = 0.01f;

    [Export] public Material material;

    [Signal] public delegate void AdjustStart();
    [Signal] public delegate void Adjust(Vector3 units);
    [Signal] public delegate void AdjustEnd();

    public float snap = 1;

    [NodeRef("Handle")] private Spatial nHandle = null;
    [NodeRef("Handle/CollisionShape")] private CollisionShape nShape = null;
    [NodeRef("Grid")] private Spatial nGrid = null;
    private Camera nCamera;

    public bool Enabled {
        get => Visible;
        set {
            Visible = value;
            nShape.Disabled = !value;
        }
    }

    public CubePos CurrentPos { get; set; }
    public bool Adjusting { get; set; }

    private Plane plane;
    private Vector3 snappedPoint;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        nHandle.GetNode<MeshInstance>("MeshInstance").MaterialOverride = material;
        nCamera = GetViewport().GetCamera();
    }

    public void Update() {
        nHandle.Scale = GDUtil.DistanceToCamera(nCamera, GlobalTranslation)
            * Vector3.One * SCALE_MULT;
    }

    private Plane GetPlane(out int axis) {
        Vector3 cameraDir = nCamera.GetCameraTransform().basis.z.Normalized();
        axis = (int)cameraDir.Abs().MaxAxis();
        Vector3 planeNormal = Vector3.Zero;
        planeNormal[axis] = cameraDir[axis] >= 0 ? 1 : -1;
        return new Plane(planeNormal, GlobalTranslation.Dot(planeNormal));
    }

    public void OnPress(Vector2 touchPos) {
        Adjusting = true;
        plane = GetPlane(out int axis);
        snappedPoint = GlobalTranslation;
        nGrid.Visible = true;
        nGrid.Scale = Vector3.One * snap;
        nGrid.Rotation = GDUtil.AxisRotation(axis);
        nGrid.Translation = Vector3.Zero;
        EmitSignal(nameof(AdjustStart));
    }

    public void OnDrag(Vector2 touchPos) {
        var (rayOrigin, rayDir) = GDUtil.ProjectRayClipped(nCamera, touchPos);
        if (plane.IntersectRay(rayOrigin, rayDir) is Vector3 point) {
            var move = ((point - snappedPoint) / snap).Round();
            if (move != Vector3.Zero) {
                snappedPoint += move * snap;
                nGrid.GlobalTranslation = snappedPoint;
                EmitSignal(nameof(Adjust), move);
            }
        }
    }

    public void OnRelease() {
        Adjusting = false;
        nGrid.Visible = false;
        EmitSignal(nameof(AdjustEnd));
    }
}
