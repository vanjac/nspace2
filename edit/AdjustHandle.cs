using Godot;
using System;

public class AdjustHandle : Spatial {
    [Signal] delegate void AdjustStart();
    [Signal] delegate void Adjust(int units);
    [Signal] delegate void AdjustEnd();

    public float snap = 1;

    [NodeRef("Line")] private Spatial nLine;
    [NodeRef("Handle")] private Spatial nHandle;
    private const float SCALE = 0.25f;
    private Camera camera;
    private float startDist, dist;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        camera = GetViewport().GetCamera();
    }

    public void Update() {
        Vector3 globalHandlePos = GlobalTranslation
            + (1 + dist) * GlobalTransform.basis[2].Normalized();
        Scale = camera.GlobalTranslation.DistanceTo(globalHandlePos) * Vector3.One * SCALE;

        float scaledDist = 1 + dist / GlobalTransform.basis[2].Length();
        nLine.Scale = new Vector3(1, 1, scaledDist);
        nHandle.Translation = new Vector3(0, 0, scaledDist);
    }

    private float DistAlongLine(Vector3 rayOrigin, Vector3 rayDir) {
        // https://math.stackexchange.com/a/3436386
        rayDir = rayDir.Normalized();
        Vector3 myOrigin = GlobalTranslation;
        Vector3 myDir = GlobalTransform.basis[2].Normalized();
        Vector3 cDir = myDir.Cross(rayDir).Normalized();
        Vector3 oDiff = myOrigin - rayOrigin;
        Vector3 projection = (oDiff).Dot(rayDir) * rayDir;
        Vector3 rejection = oDiff - projection - oDiff.Dot(cDir) * cDir;
        if (rejection.LengthSquared() == 0)
            return 0;
        return -rejection.Length() / myDir.Dot(rejection.Normalized());
    }

    public void OnPress(Vector3 origin, Vector3 dir) {
        startDist = DistAlongLine(origin, dir);
        EmitSignal(nameof(AdjustStart));
    }

    public void OnDrag(Vector3 origin, Vector3 dir) {
        dist = DistAlongLine(origin, dir) - startDist;
        if (Mathf.Abs(dist) > snap) {
            int units = dist >= 0 ? Mathf.FloorToInt(dist / snap) : Mathf.CeilToInt(dist / snap);
            EmitSignal(nameof(Adjust), units); // should move position!
            dist -= units * snap;
        }
        Update();
    }

    public void OnRelease() {
        dist = 0;
        Update();
        EmitSignal(nameof(AdjustEnd));
    }
}
