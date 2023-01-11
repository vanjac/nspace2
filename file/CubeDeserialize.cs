using Godot;
using System;
using System.IO;
using System.Collections.Generic;

using objnum = System.UInt16;

public class CubeDeserialize {
    private Dictionary<FileConst.Type, FileObj.DirEntry> directory;
    private Guid[] guids;
    private Immut<Cube.Face>[] faces;
    private Cube.LeafImmut[] leaves;
    private Cube[] cubes;
    private Immut<CubeWorld>[] worlds;

    // 12 bytes
    private Vector3 DeserializeVector3(BinaryReader reader) {
        Vector3 v = new Vector3();
        for (int i = 0; i < 3; i++)
            v[i] = reader.ReadSingle();
        return v;
    }

    // 12 bytes
    private CubePos DeserializeCubePos(BinaryReader reader) {
        CubePos p = new CubePos();
        for (int i = 0; i < 3; i++)
            p[i] = reader.ReadUInt32();
        return p;
    }

    // 16 bytes
    private Guid DeserializeGuid(BinaryReader reader) {
        return new Guid(reader.ReadBytes(16));
    }

    // 5 bytes
    private Cube.Layer DeserializeLayer(BinaryReader reader) {
        Cube.Layer layer = new Cube.Layer();
        layer.material = guids[reader.ReadUInt16()];
        layer.orientation = reader.ReadByte();
        layer.uOffset = reader.ReadByte();
        layer.vOffset = reader.ReadByte();
        return layer;
    }

    // 10 bytes
    private Immut<Cube.Face> DeserializeFace(BinaryReader reader) {
        Cube.Face face = new Cube.Face();
        face.base_ = DeserializeLayer(reader);
        face.overlay = DeserializeLayer(reader);
        return Immut.Create(face);
    }

    // 8 bytes
    private Cube.LeafImmut DeserializeLeaf(BinaryReader reader) {
        Cube.Leaf leaf = new Cube.Leaf();
        leaf.volume = guids[reader.ReadUInt16()];
        for (int i = 0; i < 3; i++)
            leaf.faces[i] = faces[reader.ReadUInt16()];
        // TODO: split
        return leaf.Immut();
    }

    private Cube DeserializeCube(BinaryReader reader) {
        objnum n = reader.ReadUInt16();
        if (n < objnum.MaxValue - 32 + 1) {
            return leaves[n];
        } else {
            int depth = objnum.MaxValue - n + 1;
            Cube.Branch branch = new Cube.Branch();
            for (int i = 0; i < 8; i++)
                branch.children[i] = DeserializeCube(reader);
            for (int i = 1; i < depth; i++) { // skip 0!
                branch.children[0] = branch.Immut();
                for (int j = 1; j < 8; j++) // skip 0!
                    branch.children[j] = DeserializeCube(reader);
            }
            return branch.Immut();
        }
    }

    // 20 bytes
    private Immut<CubeWorld> DeserializeWorld(BinaryReader reader) {
        CubeWorld world = new CubeWorld();
        world.root = cubes[reader.ReadUInt16()];
        world.rootPos = DeserializeVector3(reader);
        world.rootSize = reader.ReadSingle();
        world.voidVolume = guids[reader.ReadUInt16()];
        return Immut.Create(world);
    }

    // 56 bytes
    private EditState DeserializeEditor(BinaryReader reader) {
        EditState editor = new EditState();
        editor.world = worlds[reader.ReadUInt16()];
        editor.editDepth = reader.ReadUInt16();
        editor.camFocus = DeserializeVector3(reader);
        editor.camYaw = reader.ReadSingle();
        editor.camPitch = reader.ReadSingle();
        editor.camZoom = reader.ReadSingle();
        editor.selMode = (SelectMode)reader.ReadUInt16();
        ushort selIdx = reader.ReadUInt16();
        editor.selAxis = selIdx / 2;
        editor.selDir = (selIdx % 2) == 1;
        editor.selMin = DeserializeCubePos(reader);
        editor.selMax = DeserializeCubePos(reader);
        return editor;
    }

    private T[] LoadObjects<T>(BinaryReader reader, FileConst.Type type, int minSize,
            Func<BinaryReader, T> loadFn) {
        if (!directory.TryGetValue(type, out FileObj.DirEntry entry))
            return new T[0];
        if (entry.objectSize < minSize)
            throw new Exception("Unrecognized object format");
        if (entry.numObjects > (int)objnum.MaxValue)
            throw new Exception("Too many objects");
        if (entry.offset + entry.numObjects * entry.objectSize > reader.BaseStream.Length)
            throw new Exception("File is truncated (not enough data)");
        T[] objs = new T[entry.numObjects];

        if (entry.objectSize == 0)
            reader.BaseStream.Position = entry.offset;
        for (int i = 0; i < entry.numObjects; i++) {
            if (entry.objectSize != 0)
                reader.BaseStream.Position = entry.offset + i * entry.objectSize;
            objs[i] = loadFn(reader);
        }
        return objs;
    }

    public EditState ReadFile(Stream stream) {
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) {
            byte[] magic = reader.ReadBytes(4);
            if (!System.Linq.Enumerable.SequenceEqual(magic, FileConst.MAGIC))
                throw new Exception("File format not supported");
            stream.Position = 8;
            uint compatVersion = reader.ReadUInt32();
            if (compatVersion > FileConst.WRITER_VERSION)
                throw new Exception("File requires a newer version");
            byte dirLen = reader.ReadByte();

            directory = new Dictionary<FileConst.Type, FileObj.DirEntry>();
            stream.Position = 16;
            for (int i = 0; i < dirLen; i++) {
                FileObj.DirEntry entry = new FileObj.DirEntry();
                entry.type = (FileConst.Type)reader.ReadUInt16();
                stream.Seek(2, SeekOrigin.Current);
                entry.offset = reader.ReadUInt32();
                entry.numObjects = reader.ReadUInt32();
                entry.objectSize = reader.ReadUInt32();
                directory[entry.type] = entry;
            }

            guids = LoadObjects(reader, FileConst.Type.Guid, 16, DeserializeGuid);
            faces = LoadObjects(reader, FileConst.Type.Face, 10, DeserializeFace);
            leaves = LoadObjects(reader, FileConst.Type.Leaf, 8, DeserializeLeaf);
            cubes = LoadObjects(reader, FileConst.Type.Cube, 0, DeserializeCube);
            worlds = LoadObjects(reader, FileConst.Type.World, 20, DeserializeWorld);
            EditState[] editors = LoadObjects(reader, FileConst.Type.Editor, 56, DeserializeEditor);

            return editors[0]; // TODO don't load others
        }
    }
}
