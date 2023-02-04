using Godot;

public interface AdjustHandle {
    public void Update();
    public void OnPress(Vector2 touchPos);
    public void OnDrag(Vector2 touchPos);
    public void OnRelease();
}
