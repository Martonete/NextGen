using System;

namespace ArgentumNextgen.Game;

/// <summary>
/// Auto-repeat timer for spell training (VB6: tmrMacroCast).
/// Casts the stored spell at a configurable interval (~2.0s default).
/// Activated via /MACROSP command. Stops on movement, death, or toggle.
/// </summary>
public class SpellMacro
{
    public bool Active { get; private set; }
    public double Interval = 2.0; // seconds between casts (spells have longer cooldown)
    private double _timer;
    private Action? _action;

    /// <summary>Start repeating the given spell cast action.</summary>
    public void Start(Action castAction)
    {
        _action = castAction;
        Active = true;
        _timer = 0;
    }

    /// <summary>Stop the macro and clear the stored action.</summary>
    public void Stop()
    {
        Active = false;
        _action = null;
        _timer = 0;
    }

    /// <summary>Toggle the macro on/off for the given action.</summary>
    public void Toggle(Action castAction)
    {
        if (Active) Stop();
        else Start(castAction);
    }

    /// <summary>
    /// Called each frame from Main._Process. Fires the spell when the interval elapses.
    /// </summary>
    public void Update(double delta)
    {
        if (!Active || _action == null) return;
        _timer += delta;
        if (_timer >= Interval)
        {
            _timer = 0;
            _action.Invoke();
        }
    }
}
