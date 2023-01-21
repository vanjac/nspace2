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
}
