using Godot;
using System;
using System.IO;
using System.Collections.Generic;

using objnum = System.UInt16;

public class CubeSerialize {
    private struct ObjectCache<T> {
        public List<T> objects;
        public Dictionary<T, int> objectIndices;

        public void Clear() {
            objects = new List<T>();
            objectIndices = new Dictionary<T, int>();
        }

        public objnum CacheIndex(T obj) {
            if (!objectIndices.TryGetValue(obj, out int index32)) {
                index32 = objects.Count;
                objects.Add(obj);
                objectIndices[obj] = index32;
            }
            if (index32 > objnum.MaxValue)
                throw new Exception("Too many unique objects");
            return (objnum)index32;
        }
    }

    private ObjectCache<Guid> guidCache = new ObjectCache<Guid>();
    private ObjectCache<FileObj.Face> faceCache = new ObjectCache<FileObj.Face>();
    private ObjectCache<FileObj.Leaf> leafCache = new ObjectCache<FileObj.Leaf>();
    private ObjectCache<Cube> cubeCache = new ObjectCache<Cube>();
    private ObjectCache<Immut<CubeWorld>> worldCache = new ObjectCache<Immut<CubeWorld>>();

    // 12 bytes
    private void Serialize(BinaryWriter writer, Vector3 v) {
        for (int i = 0; i < 3; i++)
            writer.Write(v[i]);
    }

    // 12 bytes
    private void Serialize(BinaryWriter writer, CubePos p) {
        for (int i = 0; i < 3; i++)
            writer.Write(p[i]);
    }

    // 48 bytes
    private void Serialize(BinaryWriter writer, Transform t) {
        for (int i = 0; i < 4; i++)
            Serialize(writer, t[i]);
    }

    private objnum Cached(Guid guid) {
        return guidCache.CacheIndex(guid);
    }

    // 20 bytes
    private void Serialize(BinaryWriter writer, Guid guid, FileConst.Type type, objnum num) {
        writer.Write(guid.ToByteArray());
        writer.Write((ushort)type);
        writer.Write(num);
    }

    private FileObj.Layer MakeObj(Cube.Layer layer) {
        return new FileObj.Layer {
            material = Cached(layer.material),
            orientation = layer.orientation,
            uOffset = layer.uOffset,
            vOffset = layer.vOffset
        };
    }

    // 5 bytes
    private void Serialize(BinaryWriter writer, FileObj.Layer layer) {
        writer.Write(layer.material);
        writer.Write(layer.orientation);
        writer.Write(layer.uOffset);
        writer.Write(layer.vOffset);
    }

    private objnum Cached(Immut<Cube.Face> face) {
        var val = face.Val;
        return faceCache.CacheIndex(
            new FileObj.Face { base_ = MakeObj(val.base_), overlay = MakeObj(val.overlay) });
    }

    // 10 bytes
    private void Serialize(BinaryWriter writer, FileObj.Face face) {
        Serialize(writer, face.base_);
        Serialize(writer, face.overlay);
    }

    private objnum Cached(Cube.LeafImmut leaf) {
        var val = leaf.Val;
        return leafCache.CacheIndex(new FileObj.Leaf {
            volume = Cached(val.volume),
            faces = (Cached(val.faces[0]), Cached(val.faces[1]), Cached(val.faces[2])),
            split = objnum.MaxValue // TODO
        });
    }

    // 10 bytes
    private void Serialize(BinaryWriter writer, FileObj.Leaf leaf) {
        writer.Write(leaf.volume);
        for (int i = 0; i < 3; i++)
            writer.Write(leaf.faces[i]);
        writer.Write(leaf.split);
    }

    private objnum Cached(Cube cube) {
        return cubeCache.CacheIndex(cube);
    }

    private void Serialize(BinaryWriter writer, Cube cube, int depth = 0) {
        if (depth > 32)
            throw new Exception("World is too large or too detailed");
        if (cube is Cube.LeafImmut leaf) {
            if (depth > 0)
                writer.Write((objnum)(objnum.MaxValue - depth + 1));
            objnum leafNum = Cached(leaf);
            if (leafNum >= objnum.MaxValue - 32 + 1)
                throw new Exception("Too many unique cubes");
            writer.Write(leafNum);
        } else {
            var branch = cube as Cube.BranchImmut;
            Serialize(writer, branch.child(0), depth + 1);
            for (int i = 1; i < 8; i++) // skip 0!
                Serialize(writer, branch.child(i), 0);
        }
    }

    // 66 bytes
    private void Serialize(BinaryWriter writer, Immut<CubeWorld> world) {
        var val = world.Val;
        writer.Write(Cached(val.root));
        writer.Write((ushort)val.rootDepth);
        Serialize(writer, val.rootPos);
        Serialize(writer, val.transform);
        writer.Write(Cached(val.voidVolume));
    }

    private objnum Cached(Immut<CubeWorld> world) {
        return worldCache.CacheIndex(world);
    }

    // 56 bytes
    private void Serialize(BinaryWriter writer, EditState editor) {
        writer.Write(Cached(editor.world));
        writer.Write((ushort)editor.editDepth);
        Serialize(writer, editor.camFocus);
        writer.Write(editor.camYaw);
        writer.Write(editor.camPitch);
        writer.Write(editor.camZoom);
        writer.Write((ushort)editor.selMode);
        writer.Write((ushort)(editor.selAxis * 2 + (editor.selDir ? 1 : 0)));
        Serialize(writer, editor.selMin);
        Serialize(writer, editor.selMax);
    }

    private FileObj.DirEntry MakeDirEntry(Stream stream,
            FileConst.Type type, int numObjects, int objectSize) {
        return new FileObj.DirEntry {
            type = type,
            offset = (uint)stream.Position,
            numObjects = (uint)numObjects,
            objectSize = (uint)objectSize
        };
    }

    // 16 bytes
    private void Serialize(BinaryWriter writer, FileObj.DirEntry entry) {
        writer.Write((ushort)entry.type);
        writer.Write((ushort)0);
        writer.Write(entry.offset);
        writer.Write(entry.numObjects);
        writer.Write(entry.objectSize);
    }

    public void WriteFile(Stream stream, EditState editor) {
        // reset state
        guidCache.Clear();
        faceCache.Clear();
        leafCache.Clear();
        cubeCache.Clear();
        worldCache.Clear();

        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) {
            writer.Write(FileConst.MAGIC);
            writer.Write(FileConst.WRITER_VERSION);
            writer.Write(FileConst.COMPAT_VERSION);
            var directory = new FileObj.DirEntry[6];
            writer.Write(directory.Length);

            stream.Position = 16 + 16 * directory.Length;

            directory[5] = MakeDirEntry(stream, FileConst.Type.Editor, 1, 56);
            Serialize(writer, editor);

            directory[4] = MakeDirEntry(stream, FileConst.Type.World, worldCache.objects.Count, 66);
            foreach (var world in worldCache.objects)
                Serialize(writer, world);

            directory[3] = MakeDirEntry(stream, FileConst.Type.Cube, cubeCache.objects.Count, 0);
            foreach (var cube in cubeCache.objects)
                Serialize(writer, cube);

            directory[2] = MakeDirEntry(stream, FileConst.Type.Leaf, leafCache.objects.Count, 10);
            foreach (var leaf in leafCache.objects)
                Serialize(writer, leaf);

            directory[1] = MakeDirEntry(stream, FileConst.Type.Face, faceCache.objects.Count, 10);
            foreach (var face in faceCache.objects)
                Serialize(writer, face);

            directory[0] = MakeDirEntry(stream, FileConst.Type.Guid, guidCache.objects.Count, 20);
            foreach (var guid in guidCache.objects)
                Serialize(writer, guid, FileConst.Type.Guid, FileConst.NO_OBJECT);

            stream.Position = 16;
            foreach (var entry in directory)
                Serialize(writer, entry);
        }
    }
}
