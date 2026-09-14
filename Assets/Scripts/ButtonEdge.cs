/// <summary>
/// Rising-edge detector over a button's <c>isPressed</c> state, sampled once per
/// frame. Use this instead of <c>wasPressedThisFrame</c> for key/mouse checks:
/// the unity-cli bridge injects input by writing device state and running its
/// own Input System update, which consumes the press edge before
/// MonoBehaviour.Update sees it. Sampling <c>isPressed</c> works for both real
/// and simulated input.
/// </summary>
public sealed class ButtonEdge
{
    bool _wasPressed;

    /// <summary>Feed the current pressed state; returns true on the frame it became pressed.</summary>
    public bool Update(bool pressed)
    {
        bool edge = pressed && !_wasPressed;
        _wasPressed = pressed;
        return edge;
    }
}
