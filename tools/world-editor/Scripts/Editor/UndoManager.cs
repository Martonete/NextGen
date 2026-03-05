using System.Collections.Generic;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Records tile changes for undo/redo. Each action is a batch of tile modifications.
/// </summary>
public class UndoManager
{
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    private UndoAction? _currentBatch;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Begin a new batch of changes (e.g. a paint stroke).
    /// </summary>
    public void BeginBatch(string description)
    {
        _currentBatch = new UndoAction(description);
    }

    /// <summary>
    /// Record a tile change within the current batch.
    /// </summary>
    public void RecordTileChange(int x, int y, MapTile before, MapTile after)
    {
        _currentBatch?.Changes.Add(new TileChange(x, y, before, after));
    }

    /// <summary>
    /// End the current batch. If no changes were made, discard it.
    /// </summary>
    public void EndBatch()
    {
        if (_currentBatch != null && _currentBatch.Changes.Count > 0)
        {
            _undoStack.Push(_currentBatch);
            _redoStack.Clear();
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
