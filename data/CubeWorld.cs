using Godot;
using System;

public struct CubeWorld {
    public const int UNIT_DEPTH = 24;
    public Cube root;
    public int rootDepth;
    public CubePos rootPos;
    // assuming local origin position corresponds to CubePos.HALF
    // and 1 unit along local axis is the size of cube at UNIT_DEPTH
    public Transform transform;
    public Guid voidVolume;
}
