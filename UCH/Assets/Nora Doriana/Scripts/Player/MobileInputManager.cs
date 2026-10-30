using UnityEngine;

public class MobileInputManager : MonoBehaviour
{
    public static MobileInputManager Instance { get; private set; }

    [SerializeField] private UIHoldButton _leftButton;
    [SerializeField] private UIHoldButton _rightButton;
    [SerializeField] private UIHoldButton _jumpButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public float GetHorizontalInput()
    {
        float input = 0f;

        if (_leftButton != null && _leftButton.IsPressed)
        {
            input -= 1f;
        }

        if (_rightButton != null && _rightButton.IsPressed)
        {
            input += 1f;
        }

        return input;
    }

    public bool GetJumpInput()
    {
        return _jumpButton != null && _jumpButton.IsPressed;
    }
}
