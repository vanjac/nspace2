using Godot;

public class DebugCube : Spatial {
    [NodeRef("MeshInstance")] private Spatial nMesh = null;
    private CubePos pos;
    private int depth;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
    }

    private void Update() {
        GD.Print($"{pos}, {depth}");
        Vector3 v = new Vector3(pos[0], pos[1], pos[2]);
        nMesh.Translation = v / (1L << 32);
        nMesh.Scale = Vector3.One * CubeUtil.ModelCubeSize(depth + CubeModel.UNIT_DEPTH);
    }

    private void SelectChild(int i) {
        Visible = true;
        pos += CubePos.FromChildIndex(i) >> depth;
        depth++;
        Update();
    }

    private void SelectParent() {
        Visible = true;
        depth--;
        pos = pos.Floor(depth);
        Update();
    }

    public override void _UnhandledInput(InputEvent ev) {
        if (ev is InputEventKey key && key.Pressed) {
            var code = (KeyList)key.Scancode;
            if (code == KeyList.Key0 && !key.Echo) {
                SelectChild(0);
            } else if (code == KeyList.Key1 && !key.Echo) {
                SelectChild(1);
            } else if (code == KeyList.Key2 && !key.Echo) {
                SelectChild(2);
            } else if (code == KeyList.Key3 && !key.Echo) {
                SelectChild(3);
            } else if (code == KeyList.Key4 && !key.Echo) {
                SelectChild(4);
            } else if (code == KeyList.Key5 && !key.Echo) {
                SelectChild(5);
            } else if (code == KeyList.Key6 && !key.Echo) {
                SelectChild(6);
            } else if (code == KeyList.Key7 && !key.Echo) {
                SelectChild(7);
            } else if (code == KeyList.Key9) {
                SelectParent();
            } else if (code == KeyList.Quoteleft && !key.Echo) {
                Visible = !Visible;
            }
        }
    }
}
