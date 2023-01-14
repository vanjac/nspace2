using Godot;
using System;
using System.Collections.Generic;

public class TouchController : Node {
    private const float MAX_SELECT_DIST = 1000;
    private const float ZOOM_SCALE = 0.08f;
    private const float ROTATE_SCALE = 5.0f;
    private const float PAN_SCALE = 1.5f;

    private enum TouchState {
        None, Gui, Select, Adjust
    }

    [Signal] delegate void SelectStart(Vector3 pos, Vector3 norm);
    [Signal] delegate void SelectDrag(Vector3 pos, Vector3 norm);
    [Signal] delegate void SelectEnd();
    [Signal] delegate void SelectClear();
    [Signal] delegate void CameraRefocus(float newZoom);
    [Signal] delegate void CameraZoom(float factor);
    [Signal] delegate void CameraRotate(float yaw, float pitch);
    [Signal] delegate void CameraPan(Vector3 move); // not yet scaled by zoom factor

    [NodeRef("RayCast")] private RayCast nRayCast;
    private Camera nCamera;

    private Dictionary<int, Vector2> touchPositions = new Dictionary<int, Vector2>();
    private int singleTouch;
    private TouchState singleTouchState;
    private AdjustHandle grabbedHandle;

    public override void _Ready() {
        NodeRefAttribute.GetAllNodes(this);
        nCamera = GetViewport().GetCamera();
    }

    public override void _Input(InputEvent ev) {
        if (ev is InputEventScreenTouch touch) {
            if (touch.Pressed) {
                touchPositions[touch.Index] = touch.Position;
                if (touchPositions.Count == 1) {
                    singleTouch = touch.Index;
                    singleTouchState = TouchState.Gui; // assume GUI until caught by _UnhandledInput
                } else {
                    GetTree().SetInputAsHandled();
                }
            } else {
                if (touch.Index == singleTouch && singleTouchState != TouchState.Gui) {
                    switch (singleTouchState) {
                        case TouchState.Select:
                            EmitSignal(nameof(SelectEnd));
                            break;
                        case TouchState.Adjust:
                            grabbedHandle.OnRelease();
                            break;
                    }
                    GetTree().SetInputAsHandled();
                } else if (touchPositions.Count > 1) {
                    GetTree().SetInputAsHandled();
                }
                touchPositions.Remove(touch.Index);
            }
        } else if (ev is InputEventScreenDrag drag) {
            touchPositions[drag.Index] = drag.Position;
            if (touchPositions.Count == 1 && drag.Index == singleTouch
                    && singleTouchState != TouchState.Gui) {
                switch (singleTouchState) {
                    case TouchState.Select:
                        if (RayCastCursor(drag.Position, out Vector3 pos, out Vector3 norm, out _))
                            EmitSignal(nameof(SelectDrag), pos, norm);
                        break;
                    case TouchState.Adjust:
                        grabbedHandle.OnDrag(nCamera.ProjectRayOrigin(drag.Position),
                            nCamera.ProjectRayNormal(drag.Position));
                        break;
                }
            } else if (touchPositions.Count > 1) {
                GetTree().SetInputAsHandled();
            }
            if (touchPositions.Count == 2) {
                // TODO camera
            } else if (touchPositions.Count == 3) {
                // TODO camera
            }
        } else if (ev is InputEventMouseButton button && button.Pressed) {
            int index = button.ButtonIndex;
            if (index == (int)ButtonList.Middle || index == (int)ButtonList.Right) {
                if (RayCastCursor(button.Position, out Vector3 pos, out _, out _))
                    EmitSignal(nameof(CameraRefocus), pos.DistanceTo(nCamera.GlobalTranslation));
                GetTree().SetInputAsHandled();
            }
        } else if (ev is InputEventMouseMotion motion) {
            if ((motion.ButtonMask & (int)ButtonList.MaskRight) != 0) {
                var amount = motion.Relative * ROTATE_SCALE / GetViewport().Size.y;
                EmitSignal(nameof(CameraRotate), -amount.x, -amount.y);
            } else if ((motion.ButtonMask & (int)ButtonList.MaskMiddle) != 0) {
                var amount = motion.Relative / GetViewport().Size.y;
                EmitSignal(nameof(CameraPan), (nCamera.GlobalTransform.basis.y * amount.y
                    + nCamera.GlobalTransform.basis.x * -amount.x) * PAN_SCALE);
            }
        }
    }

    public override void _UnhandledInput(InputEvent ev) {
        if (ev is InputEventScreenTouch touch && touch.Pressed && touchPositions.Count == 1
                && touch.Index == singleTouch) {
            singleTouchState = TouchState.None; // definitely not GUI
            if (RayCastCursor(touch.Position,
                    out Vector3 pos, out Vector3 norm, out CollisionObject obj)) {
                if ((obj.CollisionLayer & PhysicsLayers.CubeMask) != 0) {
                    singleTouchState = TouchState.Select;
                    EmitSignal(nameof(SelectStart), pos, norm);
                } else if ((obj.CollisionLayer & (1 << PhysicsLayers.AdjustHandle)) != 0) {
                    singleTouchState = TouchState.Adjust;
                    grabbedHandle = (AdjustHandle)obj.GetParent();
                    grabbedHandle.OnPress(nCamera.ProjectRayOrigin(touch.Position),
                        nCamera.ProjectRayNormal(touch.Position));
                }
            }
            if (singleTouchState == TouchState.None)
                EmitSignal(nameof(SelectClear));
        } else if (ev is InputEventMouseButton button && button.Pressed) {
            int index = button.ButtonIndex;
            if (index == (int)ButtonList.WheelUp || index == (int)ButtonList.WheelDown) {
                if (RayCastCursor(button.Position, out Vector3 pos, out _, out _))
                    EmitSignal(nameof(CameraRefocus), pos.DistanceTo(nCamera.GlobalTranslation));
                GetTree().SetInputAsHandled();
            }
            if (index == (int)ButtonList.WheelUp) {
                EmitSignal(nameof(CameraZoom), 1.0f / (1 + ZOOM_SCALE));
            } else if (index == (int)ButtonList.WheelDown) {
                EmitSignal(nameof(CameraZoom), 1 + ZOOM_SCALE);
            }
        }
    }

    private bool RayCastCursor(Vector2 screenPoint,
            out Vector3 point, out Vector3 normal, out CollisionObject obj) {
        var rayDir = nCamera.ProjectRayNormal(screenPoint);
        nRayCast.GlobalTranslation = nCamera.ProjectRayOrigin(screenPoint);
        nRayCast.CastTo = rayDir * MAX_SELECT_DIST;
        nRayCast.ForceRaycastUpdate();
        while (nRayCast.IsColliding()) {
            point = nRayCast.GetCollisionPoint();
            normal = nRayCast.GetCollisionNormal();
            obj = nRayCast.GetCollider() as CollisionObject;
            if (rayDir.Dot(normal) > 0) { // hit back face
                if ((obj.CollisionLayer & (1 << PhysicsLayers.CubeSingleSided)) != 0) {
                    nRayCast.GlobalTranslation = point + rayDir * 0.01f;
                    nRayCast.ForceRaycastUpdate(); // try again
                } else {
                    normal = -normal;
                    return true;
                }
            } else { // hit front face
                return true;
            }
        }
        point = Vector3.Zero;
        normal = Vector3.Zero;
        obj = null;
        return false;
    }
}
