// matches project settings
public static class PhysicsLayers {
    public const int CubeSingleSided = 1;
    public const int CubeDoubleSided = 2;
    public const int AdjustHandle = 3;

    public const int CubeMask = (1 << CubeSingleSided) | (1 << CubeDoubleSided);
}
