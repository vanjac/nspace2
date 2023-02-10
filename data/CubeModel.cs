using System;

public struct CubeModel {
    public const int UNIT_DEPTH = 24;
    public const float SCALE = 1.0f / (1 << (32 - UNIT_DEPTH));
    public Cube root;
    public int rootDepth; // 1 unit along local axis is the size of cube at UNIT_DEPTH
    public CubePos rootPos; // local origin position corresponds to CubePos.HALF
    public Guid voidVolume;
}
