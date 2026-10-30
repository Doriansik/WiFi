using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float MovementSpeed = 5f;
    [SerializeField] private float JumpForce = 12f;

    [SerializeField] private Rigidbody2D _rigidbody;

    // Postac telefonu-pada: sterowanie tylko z jego telefonu, nigdy z klawiatury hosta.
    private PadInput _padInput;

    private void Awake()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody2D>();
        }
    }

    public override void OnNetworkSpawn()
    {
        TryGetComponent(out _padInput);
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
        {
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.GetCurrentState() != GameState.Platforming)
        {
            _rigidbody.linearVelocity = Vector2.zero;
            return;
        }

        HandleMovement();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.GetCurrentState() != GameState.Platforming)
        {
            return;
        }

        HandleJump();
    }

    private void HandleMovement()
    {
        float horizontalInput = _padInput != null ? _padInput.GetHorizontalInput() : GetLocalHorizontalInput();

        Vector2 targetVelocity = new Vector2(horizontalInput * MovementSpeed, _rigidbody.linearVelocity.y);
        _rigidbody.linearVelocity = targetVelocity;
    }

    private float GetLocalHorizontalInput()
    {
        float horizontalInput = 0f;

        if (MobileInputManager.Instance != null)
        {
            horizontalInput = MobileInputManager.Instance.GetHorizontalInput();
        }

        if (horizontalInput == 0f)
        {
            horizontalInput = Input.GetAxisRaw("Horizontal");
        }

        return horizontalInput;
    }

    private void HandleJump()
    {
        bool isJumpRequested = _padInput != null ? _padInput.ConsumeJumpPress() : GetLocalJumpInput();

        if (isJumpRequested && Mathf.Abs(_rigidbody.linearVelocity.y) < 0.01f)
        {
            _rigidbody.AddForce(Vector2.up * JumpForce, ForceMode2D.Impulse);
        }
    }

    private bool GetLocalJumpInput()
    {
        bool isJumpRequested = false;

        if (MobileInputManager.Instance != null)
        {
            isJumpRequested = MobileInputManager.Instance.GetJumpInput();
        }

        if (!isJumpRequested)
        {
            isJumpRequested = Input.GetButtonDown("Jump");
        }

        return isJumpRequested;
    }
}
