using Godot;

public class AdjustHandle : Spatial {
    [Export] public float ScaleMult = 0.25f;

    [Export] public Material material;

    [Signal] delegate void AdjustStart();
    [Signal] delegate void Adjust(int units);
    [Signal] delegate void AdjustEnd();

    public float snap = 1;

    [NodeRef("Line")] private Spatial nLine = null;
    [NodeRef("Handle")] private StaticBody nHandle = null;
    [NodeRef("Handle/CollisionShape")] private CollisionShape nHandleShape = null;
    private Camera nCamera;
    private float startDist, dist;

    public bool Enabled {
        get => Visible;
        set {
            Visible = value;
            nHandleShape.Disabled = !value;
        }
    }

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        nLine.GetNode<MeshInstance>("MeshInstance").MaterialOverride = material;
        nHandle.GetNode<MeshInstance>("MeshInstance").MaterialOverride = material;
        nCamera = GetViewport().GetCamera();
    }

    public void Update() {
        Vector3 globalOrigin = GlobalTranslation + dist * GlobalTransform.basis.z.Normalized();
        Scale = GDUtil.DistanceToCamera(nCamera, globalOrigin) * Vector3.One * ScaleMult;

        float scaledDist = 1 + dist / GlobalTransform.basis.z.Length();
        nLine.Scale = new Vector3(1, 1, scaledDist);
        nHandle.Translation = new Vector3(0, 0, scaledDist);
    }

    private float DistAlongLine(Vector2 touchPos) {
        var screenOrigin = nCamera.UnprojectPosition(GlobalTranslation);
        var dir = GlobalTransform.basis.z.Normalized();
        var screenDir = nCamera.UnprojectPosition(GlobalTranslation + dir * 0.1f) - screenOrigin;
        if (screenDir.LengthSquared() == 0)
            return 0;
        var dist = (touchPos - screenOrigin).Dot(screenDir) / screenDir.LengthSquared();
        var posAlongLine = screenOrigin + dist * screenDir;

        var (rayOrigin, rayDir) = GDUtil.ProjectRayClipped(nCamera, posAlongLine);

        // https://math.stackexchange.com/a/3436386
        // see also: https://stackoverflow.com/q/2316490/11525734
        rayDir = rayDir.Normalized();
        Vector3 myOrigin = GlobalTranslation;
        Vector3 myDir = GlobalTransform.basis.z.Normalized();
        Vector3 cDir = myDir.Cross(rayDir).Normalized();
        Vector3 oDiff = myOrigin - rayOrigin;
        Vector3 projection = (oDiff).Dot(rayDir) * rayDir;
        Vector3 rejection = oDiff - projection - oDiff.Dot(cDir) * cDir;
        if (rejection.LengthSquared() == 0)
            return 0;
        return -rejection.Length() / myDir.Dot(rejection.Normalized());
    }

    public void OnPress(Vector2 touchPos) {
        startDist = DistAlongLine(touchPos);
        EmitSignal(nameof(AdjustStart));
    }

    public void OnDrag(Vector2 touchPos) {
        dist = DistAlongLine(touchPos) - startDist;
        if (Mathf.Abs(dist) > snap) {
            int units = dist >= 0 ? Mathf.FloorToInt(dist / snap) : Mathf.CeilToInt(dist / snap);
            EmitSignal(nameof(Adjust), units); // should move position!
            dist -= units * snap;
#if GODOT_MOBILE
            Input.VibrateHandheld(1);
#endif
        }
        Update();
    }

    public void OnRelease() {
        dist = 0;
        Update();
        EmitSignal(nameof(AdjustEnd));
    }
}
