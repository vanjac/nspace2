using Godot;
using System;
using System.Collections.Generic;

public class TouchController : Node {
    private const float MAX_SELECT_DIST = 1000;
    private const float SCROLL_ZOOM_SCALE = 0.08f;
    private const float ROTATE_SCALE = .015f;
    private const float PAN_SCALE = .003f;

    private enum TouchState {
        None, CameraOnly, Gui, SelectPending, SelectDrag, Adjust
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
                if (touchPositions.Count == 2 && (singleTouchState == TouchState.None
                        || singleTouchState == TouchState.SelectPending))
                    singleTouchState = TouchState.CameraOnly; // prevent accidental deselect
            } else { // released
                if (touch.Index == singleTouch && singleTouchState != TouchState.Gui) {
                    switch (singleTouchState) {
                        case TouchState.SelectPending:
                            if (RayCastCursor(touch.Position,
                                    out Vector3 pos, out Vector3 norm, out _)) {
                                EmitSignal(nameof(SelectStart), pos, norm);
                                EmitSignal(nameof(SelectEnd));
                            }
                            break;
                        case TouchState.SelectDrag:
                            EmitSignal(nameof(SelectEnd));
                            break;
                        case TouchState.Adjust:
                            grabbedHandle.OnRelease();
                            break;
                        case TouchState.None:
                            EmitSignal(nameof(SelectClear));
                            break;
                    }
                    GetTree().SetInputAsHandled();
                } else if (touchPositions.Count > 1) {
                    GetTree().SetInputAsHandled();
                }
                touchPositions.Remove(touch.Index);
            }
        } else if (ev is InputEventScreenDrag drag) {
            float oldPinchWidth = (touchPositions.Count == 2) ? PinchWidth() : 1;
            touchPositions[drag.Index] = drag.Position;
            if (touchPositions.Count == 1 && drag.Index == singleTouch
                    && singleTouchState != TouchState.Gui) {
                switch (singleTouchState) {
                    case TouchState.SelectPending:
                        if (RayCastCursor(drag.Position,
                                out Vector3 pos, out Vector3 norm, out _)) {
                            EmitSignal(nameof(SelectStart), pos, norm);
                            singleTouchState = TouchState.SelectDrag;
                        } else {
                            singleTouchState = TouchState.None;
                        }
                        break;
                    case TouchState.SelectDrag:
                        if (RayCastCursor(drag.Position, out pos, out norm, out _))
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
                RotateRelative(drag.Relative / 2);
                EmitSignal(nameof(CameraZoom), oldPinchWidth / PinchWidth());
            } else if (touchPositions.Count == 3) {
                PanRelative(drag.Relative / 3);
            }
        } else if (ev is InputEventMouseButton button && button.Pressed) {
            int index = button.ButtonIndex;
            if (index == (int)ButtonList.Middle || index == (int)ButtonList.Right) {
                RefocusCursor(button.Position);
                GetTree().SetInputAsHandled();
            }
        } else if (ev is InputEventMouseMotion motion) {
            if ((motion.ButtonMask & (int)ButtonList.MaskRight) != 0) {
                RotateRelative(motion.Relative);
            } else if ((motion.ButtonMask & (int)ButtonList.MaskMiddle) != 0) {
                PanRelative(motion.Relative);
            }
        }
    }

    public override void _UnhandledInput(InputEvent ev) {
        if (ev is InputEventScreenTouch touch && touch.Pressed) {
            if (touchPositions.Count == 1 && touch.Index == singleTouch) {
                singleTouchState = TouchState.None; // definitely not GUI
                if (RayCastCursor(touch.Position, out _, out _, out CollisionObject obj,
                        mask: PhysicsLayers.CubeMask | (1 << PhysicsLayers.AdjustHandle))) {
                    if ((obj.CollisionLayer & PhysicsLayers.CubeMask) != 0) {
                        singleTouchState = TouchState.SelectPending;
                    } else if ((obj.CollisionLayer & (1 << PhysicsLayers.AdjustHandle)) != 0) {
                        singleTouchState = TouchState.Adjust;
                        grabbedHandle = (AdjustHandle)obj.GetParent();
                        grabbedHandle.OnPress(nCamera.ProjectRayOrigin(touch.Position),
                            nCamera.ProjectRayNormal(touch.Position));
                    }
                }
            }
            RefocusCursor(AverageTouchPosition());
        } else if (ev is InputEventMouseButton button && button.Pressed) {
            int index = button.ButtonIndex;
            if (index == (int)ButtonList.WheelUp || index == (int)ButtonList.WheelDown) {
                RefocusCursor(button.Position);
                GetTree().SetInputAsHandled();
            }
            if (index == (int)ButtonList.WheelUp) {
                EmitSignal(nameof(CameraZoom), 1.0f / (1 + SCROLL_ZOOM_SCALE));
            } else if (index == (int)ButtonList.WheelDown) {
                EmitSignal(nameof(CameraZoom), 1 + SCROLL_ZOOM_SCALE);
            }
        }
    }

    private void RefocusCursor(Vector2 cursor) {
        if (RayCastCursor(cursor, out Vector3 pos, out _, out _)) {
            EmitSignal(nameof(CameraRefocus), GDUtil.DistanceToCamera(nCamera, pos));
        }
    }

    private void RotateRelative(Vector2 relative) {
        relative *= -ROTATE_SCALE;
        EmitSignal(nameof(CameraRotate), relative.x, relative.y);
    }

    private void PanRelative(Vector2 relative) {
        EmitSignal(nameof(CameraPan), (nCamera.GlobalTransform.basis.y * relative.y
            + nCamera.GlobalTransform.basis.x * -relative.x) * PAN_SCALE);
    }

    private Vector2 AverageTouchPosition() {
        var sum = Vector2.Zero;
        foreach (var kv in touchPositions)
            sum += kv.Value;
        return sum / touchPositions.Count;
    }

    private float PinchWidth() {
        var e = touchPositions.GetEnumerator();
        e.MoveNext();
        var pos1 = e.Current.Value;
        e.MoveNext();
        return e.Current.Value.DistanceTo(pos1);
    }

    private bool RayCastCursor(Vector2 screenPoint,
            out Vector3 point, out Vector3 normal, out CollisionObject obj,
            uint mask = PhysicsLayers.CubeMask) {
        var rayDir = nCamera.ProjectRayNormal(screenPoint);
        nRayCast.CollisionMask = mask;
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
