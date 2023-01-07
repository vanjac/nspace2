using System;

public static class Util {
    /// <summary>
    /// If oldVal != newVal, set changed to true, then return newVal.
    /// </summary>
    /// <typeparam name="T">Type of values to compare.</typeparam>
    /// <param name="oldVal">First value to compare</param>
    /// <param name="newVal">Second value to compare</param>
    /// <param name="changed">
    /// Set to true if the values are not equal, otherwise not modified.
    /// </param>
    /// <returns>newVal</returns>
    public static T AssignChanged<T>(T oldVal, T newVal, ref bool changed) where T : class {
        changed |= oldVal != newVal;
        return newVal;
    }
}

/// <summary>
/// An immutable reference-type box for a struct.
/// </summary>
/// <typeparam name="T">Type of value to be made immutable.</typeparam>
public class Immut<T> {
    public readonly T Val;

    public Immut(T val) {
        Val = val;
    }
}

public static class Immut {
    /// <summary>
    /// Shorthand for creating a new Immut&amp;lt;T&amp;gt;.
    /// </summary>
    public static Immut<T> Create<T>(T val) {
        return new Immut<T>(val);
    }
}

/// <summary>
/// A value-type array of 3 items.
/// </summary>
/// <typeparam name="T">Type of item contained in the array</typeparam>
public struct Arr3<T> {
    private ValueTuple<T, T, T> Items;

    public Arr3(T item1, T item2, T item3) {
        Items = (item1, item2, item3);
    }

    public static implicit operator Arr3<T>(ValueTuple<T, T, T> tuple)
        => new Arr3<T> { Items = tuple };

    public T this[int i] {
        readonly get {
            switch (i) {
                case 0: return Items.Item1;
                case 1: return Items.Item2;
                case 2: return Items.Item3;
                default: throw new IndexOutOfRangeException();
            }
        }
        set {
            switch (i) {
                case 0: Items.Item1 = value; break;
                case 1: Items.Item2 = value; break;
                case 2: Items.Item3 = value; break;
                default: throw new IndexOutOfRangeException();
            }
        }
    }
}

/// <summary>
/// A value-type array of 8 items.
/// </summary>
/// <typeparam name="T">Type of item contained in the array</typeparam>
public struct Arr8<T> {
    private ValueTuple<T, T, T, T, T, T, T, ValueTuple<T>> Items;

    public Arr8(T item1, T item2, T item3, T item4, T item5, T item6, T item7, T item8) {
        Items = (item1, item2, item3, item4, item5, item6, item7, item8);
    }

    public static implicit operator Arr8<T>(ValueTuple<T, T, T, T, T, T, T, ValueTuple<T>> tuple)
        => new Arr8<T> { Items = tuple };

    public T this[int i] {
        readonly get {
            switch (i) {
                case 0: return Items.Item1;
                case 1: return Items.Item2;
                case 2: return Items.Item3;
                case 3: return Items.Item4;
                case 4: return Items.Item5;
                case 5: return Items.Item6;
                case 6: return Items.Item7;
                case 7: return Items.Rest.Item1;
                default: throw new IndexOutOfRangeException();
            }
        }
        set {
            switch (i) {
                case 0: Items.Item1 = value; break;
                case 1: Items.Item2 = value; break;
                case 2: Items.Item3 = value; break;
                case 3: Items.Item4 = value; break;
                case 4: Items.Item5 = value; break;
                case 5: Items.Item6 = value; break;
                case 6: Items.Item7 = value; break;
                case 7: Items.Rest.Item1 = value; break;
                default: throw new IndexOutOfRangeException();
            }
        }
    }
}
