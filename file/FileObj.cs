using objnum = System.UInt16;

public static class FileConst {
    public static readonly byte[] MAGIC = new byte[] {0x4E, 0x53, 0x50, 0x41}; // NSPA
    public static readonly uint WRITER_VERSION = 0x00000002;
    public static readonly uint COMPAT_VERSION = 0x00000002;

    public const objnum NO_OBJECT = objnum.MaxValue;

    public enum Type : ushort {
        Guid, Face, Leaf, Cube, World, Editor
    }
}

// TODO hash functions
public static class FileObj {
    public struct DirEntry {
        public FileConst.Type type;
        public uint offset;
        public uint numObjects;
        public uint objectSize;
    }

    public struct Layer {
        public objnum material;
        public byte orientation, uOffset, vOffset;
    }

    public struct Face {
        public Layer base_, overlay;
    }

    public struct Leaf {
        public objnum volume;
        public Arr3<objnum> faces;
        public objnum split;
    }
}
