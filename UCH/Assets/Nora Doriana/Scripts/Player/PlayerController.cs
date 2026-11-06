using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float MovementSpeed = 5f;
    [SerializeField] private float JumpForce = 12f;

    [Header("Wall jump")]
    // Wzgledem zwyklego skoku i biegu - zmiana JumpForce/MovementSpeed w prefabie zmienia tez odbicie od sciany.
    [SerializeField] private float WallJumpHeightFactor = 1f;      // 1 = tak wysoko jak zwykly skok
    [SerializeField] private float WallJumpPushFactor = 1.2f;      // odbicie w bok = predkosc biegu x tyle
    [SerializeField] private float WallJumpInputLockSeconds = 0.12f; // tyle po odbiciu sterowanie nie zawraca postaci na sciane
    [SerializeField] private float WallSlideFallFactor = 0.4f;     // zjazd po scianie: maks. predkosc spadania = predkosc skoku x tyle
    [SerializeField] private float WallJumpGraceSeconds = 0.1f;    // skok wcisniety chwile po oderwaniu od sciany tez sie liczy

    [SerializeField] private Rigidbody2D _rigidbody;

    // Podloze = styk z powierzchnia, ktorej normalna patrzy w gore (do ~45 stopni); sciana = normalna prawie pozioma.
    private const float GroundNormalMinY = 0.7f;
    private const float WallNormalMinX = 0.7f;
    private const float MaxJumpStartVelocityY = 0.1f;  // nie skaczemy drugi raz, zanim fizyka odlepi postac od ziemi
    private const int MaxContacts = 16;

    // Gracze przenikaja sie nawzajem (jak w imprezowych platformowkach) - wszyscy startuja z jednej lodowki.
    private static readonly List<Collider2D> _playerColliders = new List<Collider2D>();
    // Bez tarcia - inaczej postac dociskana do sciany (ruch ustawia predkosc w strone sciany) wisi na niej zamiast spadac.
    // Ruch i tak ustawia predkosc pozioma wprost, wiec na ziemi tarcie nie jest potrzebne.
    private static PhysicsMaterial2D _noFriction;

    private readonly List<ContactPoint2D> _contacts = new List<ContactPoint2D>(MaxContacts);
    private Collider2D _collider;

    // Jak w imprezowych platformowkach: od sciany mozna sie odbijac wiele razy, takze od tej samej -
    // wspinaczka wymaga wracania do sciany po kazdym odbiciu (blokada sterowania odrzuca postac na chwile).
    private int _lastWallSide;
    private float _lastWallTime = float.MinValue;
    private float _inputLockUntil;

    // Postac telefonu-pada: sterowanie tylko z jego telefonu, nigdy z klawiatury hosta.
    private PadInput _padInput;
    private PlayerBuilder _builder;

    private void Awake()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        if (_noFriction == null)
        {
            _noFriction = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };
        }

        if (TryGetComponent(out Collider2D playerCollider))
        {
            playerCollider.sharedMaterial = _noFriction;
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
        ReadContacts(out bool isGrounded, out int wallSide);
        Vector2 velocity = _rigidbody.linearVelocity;

        // Chwile po odbiciu od sciany postac leci tam, gdzie ja odbilo - inaczej trzymany kierunek wcisnalby ja z powrotem.
        if (Time.time >= _inputLockUntil)
        {
            velocity.x = horizontalInput * MovementSpeed;
        }

        // Zjazd po scianie: wolniejsze spadanie, gdy gracz trzyma kierunek w strone sciany - latwiej trafic z odbiciem.
        bool isPressingIntoWall = wallSide != 0 && horizontalInput * wallSide < 0f;

        float wallSlideMaxFallSpeed = GetJumpSpeed() * WallSlideFallFactor;

        if (!isGrounded && isPressingIntoWall && velocity.y < -wallSlideMaxFallSpeed)
        {
            velocity.y = -wallSlideMaxFallSpeed;
        }

        _rigidbody.linearVelocity = velocity;
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

        if (!isJumpRequested)
        {
            return;
        }

        ReadContacts(out bool isGrounded, out int wallSide);

        if (isGrounded)
        {
            if (_rigidbody.linearVelocity.y < MaxJumpStartVelocityY)
            {
                _rigidbody.AddForce(Vector2.up * JumpForce, ForceMode2D.Impulse);
            }

            return;
        }

        if (wallSide == 0 && Time.time - _lastWallTime <= WallJumpGraceSeconds)
        {
            wallSide = _lastWallSide;
        }

        if (wallSide != 0)
        {
            _rigidbody.linearVelocity = new Vector2(wallSide * MovementSpeed * WallJumpPushFactor, GetJumpSpeed() * WallJumpHeightFactor);
            _inputLockUntil = Time.time + WallJumpInputLockSeconds;
            _lastWallTime = float.MinValue;
        }
    }

    // Predkosc w gore, jaka daje zwykly skok (impuls JumpForce przy masie postaci).
    private float GetJumpSpeed()
    {
        return JumpForce / _rigidbody.mass;
    }

    // Czego postac dotyka po ostatnim kroku fizyki. wallSide: +1 = sciana po lewej (odbicie w prawo), -1 = po prawej, 0 = brak.
    private void ReadContacts(out bool isGrounded, out int wallSide)
    {
        isGrounded = false;
        wallSide = 0;
        int count = _rigidbody.GetContacts(_contacts);

        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = _contacts[i];

            if (contact.collider.isTrigger || contact.otherCollider.isTrigger)
            {
                continue;
            }

            if (contact.normal.y >= GroundNormalMinY)
            {
                isGrounded = true;
            }
            else if (Mathf.Abs(contact.normal.x) >= WallNormalMinX)
            {
                wallSide = contact.normal.x > 0f ? 1 : -1;
            }
        }

        if (isGrounded)
        {
            wallSide = 0;  // stojac przy scianie skaczemy normalnie, nie odbijamy sie
        }
        else if (wallSide != 0)
        {
            _lastWallSide = wallSide;
            _lastWallTime = Time.time;
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
