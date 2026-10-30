using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float MovementSpeed = 5f;
    [SerializeField] private float JumpForce = 12f;

    [SerializeField] private Rigidbody2D _rigidbody;

    // Gracze przenikaja sie nawzajem (jak w imprezowych platformowkach) - wszyscy startuja z jednej lodowki.
    private static readonly List<Collider2D> _playerColliders = new List<Collider2D>();
    private Collider2D _collider;

    // Postac telefonu-pada: sterowanie tylko z jego telefonu, nigdy z klawiatury hosta.
    private PadInput _padInput;
    private PlayerBuilder _builder;

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
        TryGetComponent(out _builder);

        if (TryGetComponent(out _collider))
        {
            foreach (Collider2D other in _playerColliders)
            {
                if (other != null)
                {
                    Physics2D.IgnoreCollision(_collider, other);
                }
            }

            _playerColliders.Add(_collider);
        }
    }

    public override void OnNetworkDespawn()
    {
        _playerColliders.Remove(_collider);
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
        {
            return;
        }

        // Budowanie albo odpadniecie z rundy - postac stoi w miejscu, bez grawitacji i zderzen.
        bool canMove = CanMove();
        _rigidbody.simulated = canMove;

        if (!canMove)
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

        if (!CanMove())
        {
            return;
        }

        HandleJump();
    }

    // Bez GameManager (lobby) mozna biegac zawsze; w grze tylko w fazie biegu.
    private bool CanMove()
    {
        if (GameManager.Instance == null)
        {
            return true;
        }

        return GameManager.Instance.GetCurrentState() == GameState.Platforming && (_builder == null || !_builder.IsOut && !_builder.HasFinished);
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
