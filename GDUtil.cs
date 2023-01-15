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
        return (point - camera.GlobalTranslation).Project(camera.GlobalTransform.basis.z).Length();
    }
}
