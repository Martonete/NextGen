using System;
using System.Collections.Generic;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

public class UndoManager
{
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    private UndoAction? _currentBatch;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    // Called whenever a batch with actual changes is committed
    public event Action? Changed;

    public void BeginBatch(string description)
    {
        _currentBatch = new UndoAction(description);
    }

    public void RecordTileChange(int x, int y, MapTile before, MapTile after)
    {
        _currentBatch?.Changes.Add(new TileChange(x, y, before, after));
    }

    public void EndBatch()
    {
        if (_currentBatch != null && _currentBatch.Changes.Count > 0)
        {
            _undoStack.Push(_currentBatch);
            _redoStack.Clear();
            Changed?.Invoke();
        }
        _currentBatch = null;
    }

    public void Undo(MapData map)
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack.Pop();
        foreach (var change in action.Changes)
            map.Tiles[change.X, change.Y] = change.Before;
        _redoStack.Push(action);
    }

    public void Redo(MapData map)
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack.Pop();
        foreach (var change in action.Changes)
            map.Tiles[change.X, change.Y] = change.After;
        _undoStack.Push(action);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _currentBatch = null;
    }
}

public record TileChange(int X, int Y, MapTile Before, MapTile After);

public class UndoAction
{
    public string Description;
    public List<TileChange> Changes = new();
    public UndoAction(string description) => Description = description;
}
