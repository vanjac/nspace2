using Godot;
using System;
using System.IO;
using System.Collections.Generic;

using objnum = System.UInt16;

public class CubeDeserialize {
    private uint writerVersion;
    private Dictionary<FileConst.Type, FileObj.DirEntry> directory;
    private Guid[] guids;
    private Immut<Cube.Face>[] faces;
    private Cube.LeafImmut[] leaves;
    private Cube[] cubes;
    private Immut<CubeModel>[] models;

    // 12 bytes
    private Vector3 DeserializeVector3(BinaryReader reader) {
        Vector3 v = new Vector3();
        for (int i = 0; i < 3; i++)
            v[i] = reader.ReadSingle();
        return v;
    }

    // 12 bytes
    private CubePos DeserializeCubePos(BinaryReader reader) {
        CubePos p = CubePos.ZERO;
        for (int i = 0; i < 3; i++)
            p[i] = reader.ReadUInt32();
        return p;
    }

    // 48 bytes
    private Transform DeserializeTransform(BinaryReader reader) {
        Transform t = new Transform();
        for (int i = 0; i < 4; i++)
            t[i] = DeserializeVector3(reader);
        return t;
    }

    // 16 bytes
    private Guid DeserializeGuid(BinaryReader reader) {
        return new Guid(reader.ReadBytes(16));
    }

    // 11 bytes
    private Cube.Layer DeserializeLayer(BinaryReader reader) {
        Cube.Layer layer = new Cube.Layer();
        layer.material = guids[reader.ReadUInt16()];
        if (writerVersion >= 0x00000003) {
            layer.uOffset = reader.ReadInt32();
            layer.vOffset = reader.ReadInt32();
            layer.orientation = reader.ReadByte();
        } else {
            reader.ReadUInt16();
            reader.ReadByte();
        }
        return layer;
    }

    // 22 bytes
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

    // 18 bytes
    private Immut<CubeModel> DeserializeModel(BinaryReader reader) {
        CubeModel model = new CubeModel();
        model.root = cubes[reader.ReadUInt16()];
        model.rootDepth = reader.ReadUInt16();
        model.rootPos = DeserializeCubePos(reader);
        model.voidVolume = guids[reader.ReadUInt16()];
        return Immut.Create(model);
    }

    // 20 bytes
    private Immut<CubeModel> DeserializeModelVersion1(BinaryReader reader) {
        CubeModel model = new CubeModel();
        model.root = cubes[reader.ReadUInt16()];
        Vector3 rootPos = DeserializeVector3(reader);
        model.rootPos = CubePos.FromModelPos(rootPos);
        float rootSize = reader.ReadSingle();
        model.rootDepth = CubeModel.UNIT_DEPTH - (int)Math.Round(Math.Log(rootSize, 2));
        model.voidVolume = guids[reader.ReadUInt16()];
        return Immut.Create(model);
    }

    // 56 bytes
    private EditState DeserializeEditor(BinaryReader reader) {
        EditState editor = new EditState();
        editor.world = models[reader.ReadUInt16()];
        editor.editDepth = reader.ReadUInt16();
        editor.camFocus = DeserializeVector3(reader);
        editor.camYaw = reader.ReadSingle();
        editor.camPitch = reader.ReadSingle();
        editor.camZoom = reader.ReadSingle();
        reader.ReadUInt16(); // previously selMode
        ushort selIdx = reader.ReadUInt16();
        editor.selAxis = selIdx / 2;
        editor.selDir = (selIdx % 2) == 1;
        editor.selMin = DeserializeCubePos(reader);
        editor.selMax = DeserializeCubePos(reader);
        if (writerVersion < 0x00000002) {
            CubeModel m = editor.world.Val;
            editor.editDepth += m.rootDepth;
            editor.selMin = (editor.selMin >> m.rootDepth) + m.rootPos;
            editor.selMax = (editor.selMax >> m.rootDepth) + m.rootPos;
        }
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
            writerVersion = reader.ReadUInt32();
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
            faces = LoadObjects(reader, FileConst.Type.Face, writerVersion >= 0x00000003 ? 22 : 10,
                DeserializeFace);
            leaves = LoadObjects(reader, FileConst.Type.Leaf, 8, DeserializeLeaf);
            cubes = LoadObjects(reader, FileConst.Type.Cube, 0, DeserializeCube);
            if (writerVersion >= 0x00000002)
                models = LoadObjects(reader, FileConst.Type.Model, 18, DeserializeModel);
            else
                models = LoadObjects(reader, FileConst.Type.Model, 20, DeserializeModelVersion1);
            EditState[] editors = LoadObjects(reader, FileConst.Type.Editor, 56, DeserializeEditor);

            return editors[0]; // TODO don't load others
        }
    }
}
