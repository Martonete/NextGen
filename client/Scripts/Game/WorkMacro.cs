using System;

namespace ArgentumNextgen.Game;

/// <summary>
/// Auto-repeat timer for gathering actions (fishing, mining, woodcutting, smelting).
/// VB6: tmrTrabajo timer — repeats the last work action every ~1.5 seconds.
/// Player activates via /MACRO command; stops on movement, death, or toggle.
/// </summary>
public class WorkMacro
{
    public bool Active { get; private set; }
    public double Interval = 1.5; // seconds between repeats
    private double _timer;
    private Action? _action;

    /// <summary>Start repeating the given work action.</summary>
    public void Start(Action workAction)
    {
        _action = workAction;
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
    public void Toggle(Action workAction)
    {
        if (Active) Stop();
        else Start(workAction);
    }

    /// <summary>
    /// Called each frame from Main._Process. Fires the action when the interval elapses.
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
