using System.Collections.Generic;

public class Undoer<T> {
    private Stack<T> undoStack = new Stack<T>();
    private Stack<T> redoStack = new Stack<T>();

    public void Clear() {
        undoStack.Clear();
        redoStack.Clear();
    }

    public void Push(T state) {
        undoStack.Push(state);
        redoStack.Clear();
    }

    public bool CanUndo() => undoStack.Count != 0;

    public T Undo(T state) {
        if (!CanUndo())
            return state;
        redoStack.Push(state);
        return undoStack.Pop();
    }

    public bool CanRedo() => redoStack.Count != 0;

    public T Redo(T state) {
        if (!CanRedo())
            return state;
        undoStack.Push(state);
        return redoStack.Pop();
    }
}
