using Godot;

public class LayerPaint : Node {
    [NodeRef("UV/OffsetUp")] public Button nUVOffsetUp = null;
    [NodeRef("UV/OffsetDown")] public Button nUVOffsetDown = null;
    [NodeRef("UV/OffsetLeft")] public Button nUVOffsetLeft = null;
    [NodeRef("UV/OffsetRight")] public Button nUVOffsetRight = null;
    [NodeRef("UV/FlipHoriz")] public Button nUVFlipHoriz = null;
    [NodeRef("UV/FlipVert")] public Button nUVFlipVert = null;
    [NodeRef("UV/RotateCCW")] public Button nUVRotateCCW = null;
    [NodeRef("UV/RotateCW")] public Button nUVRotateCW = null;
    [NodeRef("UV/Reset")] public Button nUVReset = null;
    [NodeRef("Scroll/Grid")] public Container nMaterialsGrid = null;
    private PackedScene sMatButton;

    public void Init() {
        NodeRefAttribute.GetAllNodes(this);
        sMatButton = GD.Load<PackedScene>("res://edit/MaterialButton.tscn");
    }

    public Button AddMaterialButton(Texture texture) {
        var instance = sMatButton.Instance<Button>();
        instance.Icon = texture;
        nMaterialsGrid.AddChild(instance);
        return instance;
    }
}
