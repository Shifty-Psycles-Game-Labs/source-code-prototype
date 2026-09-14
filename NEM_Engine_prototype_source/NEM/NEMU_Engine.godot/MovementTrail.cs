using Godot;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// MovementTrail — two-frame movement detector and ghost-line accumulator.
///
/// Implements H4.nmd §PART 4 movement semantics:
///   "If player is not static — ie location activates two frames in a row —
///    then log: player moving. This should be a vector, like a ghost line."
///
/// Usage (in PlayerController._PhysicsProcess):
///   _trail.Sample(GlobalTransform.Origin);
///   bool moving = _trail.IsMoving;
///
/// The trail grows unbounded until TrimToLength() or TrimOlderThan() is called.
/// Maximum length is capped by MaxTrailLength to prevent memory bloat in long sessions.
///
/// Drawing the trail: read Trail property and pass positions to an ImmediateMesh
/// or Line3D node.  This class is geometry-agnostic — it only stores points.
/// </summary>
public sealed class MovementTrail
{
    // ── tunables ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Minimum world-space displacement per frame to classify the player as moving.
    /// Below this threshold (floating-point jitter, physics settlement) the player is IDLE.
    /// </summary>
    public float MovementThreshold  = 0.01f;

    /// <summary>
    /// Hard cap on the number of trail points retained.
    /// Oldest points are dropped when the trail exceeds this length.
    /// 0 = unlimited (not recommended for long sessions).
    /// </summary>
    public int   MaxTrailLength     = 512;

    // ── state ─────────────────────────────────────────────────────────────────
    /// <summary>True if the player moved at least MovementThreshold units this frame.</summary>
    public bool    IsMoving         { get; private set; }

    /// <summary>
    /// Read-only view of the ghost-trail positions, oldest first.
    /// Use with ImmediateMesh or Line3D to visualise the trajectory.
    /// </summary>
    public IReadOnlyList<Vector3> Trail => _trail;

    // ── internals ─────────────────────────────────────────────────────────────
    private Vector3           _lastPos;
    private bool              _initialised;
    private readonly List<Vector3> _trail = new();

    // ── sampling ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sample the player's current position.
    /// Call once per physics frame with GlobalTransform.Origin.
    ///
    /// Sets IsMoving = true when displacement > MovementThreshold.
    /// Appends the position to the trail when moving.
    /// Trims the trail to MaxTrailLength if it grows too large.
    /// </summary>
    public void Sample(Vector3 currentPos)
    {
        if (!_initialised)
        {
            _lastPos      = currentPos;
            _initialised  = true;
            IsMoving      = false;
            return;
        }

        IsMoving = currentPos.DistanceTo(_lastPos) > MovementThreshold;

        if (IsMoving)
        {
            _trail.Add(currentPos);

            // Trim oldest entries when capped
            if (MaxTrailLength > 0 && _trail.Count > MaxTrailLength)
                _trail.RemoveAt(0);
        }

        _lastPos = currentPos;
    }

    // ── trail management ──────────────────────────────────────────────────────

    /// <summary>Discard all trail points.</summary>
    public void Clear() => _trail.Clear();

    /// <summary>Keep only the most recent <paramref name="count"/> points.</summary>
    public void TrimToLength(int count)
    {
        if (_trail.Count > count)
            _trail.RemoveRange(0, _trail.Count - count);
    }
}
