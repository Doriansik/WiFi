using UnityEngine;

#region Pad Input
// Wejscie postaci sterowanej telefonem-padem. PadServer dodaje ten komponent do postaci przed jej zespawnowaniem.
public class PadInput : MonoBehaviour
{
    private PadSession _session;

    public void Bind(PadSession session)
    {
        _session = session;
    }

    public float GetHorizontalInput()
    {
        int buttons = _session.Buttons;
        float input = 0f;

        if ((buttons & PadSession.ButtonLeft) != 0)
        {
            input -= 1f;
        }

        if ((buttons & PadSession.ButtonRight) != 0)
        {
            input += 1f;
        }

        return input;
    }

    public bool ConsumeJumpPress()
    {
        return _session.ConsumeJumpPress();
    }
}
#endregion
