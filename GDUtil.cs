using Godot;
using System;
using System.Reflection;

public class NodeRefAttribute : Attribute {
    private string path;

    public NodeRefAttribute(string path) {
        this.path = path;
    }

    public static void GetAllNodes(Node node) {
        foreach (var field in node.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static)) {
            foreach (var attr in field.GetCustomAttributes<NodeRefAttribute>()) {
                field.SetValue(node, node.GetNode(attr.path));
            }
        }
    }
}

public static class GDUtil {
    public static float DistanceToCamera(Camera camera, Vector3 point) {
        var transform = camera.GetCameraTransform();
        return (point - transform.origin).Project(transform.basis.z).Length();
    }

    public static (Vector3 origin, Vector3 dir) ProjectRayClipped(Camera camera, Vector2 point) {
        var camPos = camera.GetCameraTransform().origin;
        var dir = camera.ProjectRayNormal(point);
        var nearPlane = (Plane)camera.GetFrustum()[0]; // TODO avoid allocation
        return (nearPlane.IntersectRay(camPos, dir) ?? camPos, dir);
    }

    public static Vector3 AxisRotation(int axis, bool dir = true) {
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
}
