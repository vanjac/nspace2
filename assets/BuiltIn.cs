using Godot;

public class BuiltIn : Node {
    [Export] public Resource[] baseMaterials = new Resource[0]; // AA_MaterialInfo.gd
    [Export] public Resource[] overlayMaterials = new Resource[0];
}
