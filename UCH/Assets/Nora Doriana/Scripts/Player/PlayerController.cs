using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float MovementSpeed = 5f;
    [SerializeField] private float JumpHeight = 1.5f;  // w kratkach (1 kratka = 32 px), liczone od stop do szczytu skoku

    [Header("Wall jump")]
    // Wzgledem zwyklego skoku i biegu - zmiana JumpHeight/MovementSpeed w prefabie zmienia tez odbicie od sciany.
    [SerializeField] private float WallJumpHeightFactor = 1f;      // 1 = tak wysoko jak zwykly skok
    [SerializeField] private float WallJumpPushFactor = 1.2f;      // odbicie w bok = predkosc biegu x tyle
    [SerializeField] private float WallJumpInputLockSeconds = 0.12f; // tyle po odbiciu sterowanie nie zawraca postaci na sciane
    [SerializeField] private float WallSlideFallFactor = 0.4f;     // zjazd po scianie: maks. predkosc spadania = predkosc skoku x tyle
    [SerializeField] private float WallJumpGraceSeconds = 0.1f;    // skok wcisniety chwile po oderwaniu od sciany tez sie liczy

    [Header("Lod")]
    [SerializeField] private float IceAcceleration = 8f;  // kratki/s^2 - jak szybko postac na lodzie rozpedza sie i hamuje (mniej = bardziej slisko)

    [SerializeField] private Rigidbody2D _rigidbody;

    // Podloze = styk z powierzchnia, ktorej normalna patrzy w gore (do ~45 stopni); sciana = normalna prawie pozioma.
    private const float GroundNormalMinY = 0.7f;
    private const float WallNormalMinX = 0.7f;
    private const float MaxJumpStartVelocityY = 0.1f;  // nie skaczemy drugi raz, zanim fizyka odlepi postac od ziemi
    private const int MaxContacts = 16;
    private const float BounceCooldownSeconds = 0.2f;  // jedno odbicie na jedno ladowanie, nawet gdy postac chwile zostaje w strefie

    // Gracze przenikaja sie nawzajem (jak w imprezowych platformowkach) - wszyscy startuja z jednej lodowki.
    private static readonly List<Collider2D> _playerColliders = new List<Collider2D>();
    // Bez tarcia - inaczej postac dociskana do sciany (ruch ustawia predkosc w strone sciany) wisi na niej zamiast spadac.
    // Ruch i tak ustawia predkosc pozioma wprost, wiec na ziemi tarcie nie jest potrzebne.
    private static PhysicsMaterial2D _noFriction;

    private readonly List<ContactPoint2D> _contacts = new List<ContactPoint2D>(MaxContacts);
    private Collider2D _collider;
    private float _gravityScale = 1f;  // z prefabu - stojac na podlozu wylaczamy grawitacje, potem ja przywracamy

    // Jak w imprezowych platformowkach: od sciany mozna sie odbijac wiele razy, takze od tej samej -
    // wspinaczka wymaga wracania do sciany po kazdym odbiciu (blokada sterowania odrzuca postac na chwile).
    private int _lastWallSide;
    private float _lastWallTime = float.MinValue;
    private float _inputLockUntil;
    private float _nextBounceTime;
    // Zjechawszy albo wyskoczywszy z lodu postac leci dalej rozpedem, dopoki gracz nie wcisnie kierunku albo nie wyladuje.
    private bool _hasIceMomentum;

    // Postac telefonu-pada: sterowanie tylko z jego telefonu, nigdy z klawiatury hosta.
    private PadInput _padInput;
    private PlayerBuilder _builder;

    private void Awake()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        _gravityScale = _rigidbody.gravityScale;

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
        ReadContacts(out bool isGrounded, out int wallSide, out bool isOnIce);
        Vector2 velocity = _rigidbody.linearVelocity;

        if (isGrounded)
        {
            _hasIceMomentum = isOnIce;
        }
        else if (wallSide != 0)
        {
            _hasIceMomentum = false;
        }

        // Chwile po odbiciu od sciany postac leci tam, gdzie ja odbilo - inaczej trzymany kierunek wcisnalby ja z powrotem.
        if (Time.time >= _inputLockUntil)
        {
            float targetSpeedX = horizontalInput * MovementSpeed;

            if (isGrounded && isOnIce)
            {
                // Lod: predkosc dochodzi do zadanej powoli - postac rusza z poslizgiem i jeszcze kawalek jedzie po puszczeniu.
                velocity.x = Mathf.MoveTowards(velocity.x, targetSpeedX, IceAcceleration * Time.fixedDeltaTime);
            }
            else if (!(_hasIceMomentum && horizontalInput == 0f))
            {
                velocity.x = targetSpeedX;
            }
        }

        // Zjazd po scianie: wolniejsze spadanie, gdy gracz trzyma kierunek w strone sciany - latwiej trafic z odbiciem.
        bool isPressingIntoWall = wallSide != 0 && horizontalInput * wallSide < 0f;

        float wallSlideMaxFallSpeed = GetJumpSpeed() * WallSlideFallFactor;

        if (!isGrounded && isPressingIntoWall && velocity.y < -wallSlideMaxFallSpeed)
        {
            velocity.y = -wallSlideMaxFallSpeed;
        }

        // Postac jest bez tarcia (zeby nie wisiala na scianach), wiec na lagodnym skosie powoli by sie zsuwala.
        // Stojac na podlozu bez wcisnietego kierunku zatrzymujemy ja w miejscu - grawitacja wraca przy ruchu i skoku.
        bool isStandingStill = isGrounded && horizontalInput == 0f && Time.time >= _inputLockUntil && velocity.y <= MaxJumpStartVelocityY
            && (!isOnIce || Mathf.Abs(velocity.x) < 0.01f);
        _rigidbody.gravityScale = isStandingStill ? 0f : _gravityScale;

        if (isStandingStill)
        {
            velocity = Vector2.zero;
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

        ReadContacts(out bool isGrounded, out int wallSide, out _);

        if (isGrounded)
        {
            if (_rigidbody.linearVelocity.y < MaxJumpStartVelocityY)
            {
                _rigidbody.gravityScale = _gravityScale;  // stojac bez ruchu postac miala wylaczona grawitacje
                _rigidbody.linearVelocity = new Vector2(_rigidbody.linearVelocity.x, GetJumpSpeed());
            }

            return;
        }

        if (wallSide == 0 && Time.time - _lastWallTime <= WallJumpGraceSeconds)
        {
            wallSide = _lastWallSide;
        }

        if (wallSide != 0)
        {
            _rigidbody.gravityScale = _gravityScale;
            _rigidbody.linearVelocity = new Vector2(wallSide * MovementSpeed * WallJumpPushFactor, GetJumpSpeed() * WallJumpHeightFactor);
            _inputLockUntil = Time.time + WallJumpInputLockSeconds;
            _lastWallTime = float.MinValue;
        }
    }

    // Postac wpadla w strefe odbijajacego obiektu (np. chleba). Odbija tylko wlasciciel - u niego liczy sie fizyka postaci.
    public void TryBounce(ItemBouncer bouncer)
    {
        if (!IsOwner || !CanMove() || Time.time < _nextBounceTime)
        {
            return;
        }

        Vector2 direction = bouncer.Direction;
        Vector2 velocity = _rigidbody.linearVelocity;

        // Tylko gdy postac na obiekt spada albo na nim stoi - nie gdy juz od niego odlatuje.
        if (Vector2.Dot(velocity, direction) > MaxJumpStartVelocityY)
        {
            return;
        }

        _nextBounceTime = Time.time + BounceCooldownSeconds;
        _rigidbody.gravityScale = _gravityScale;
        _rigidbody.linearVelocity = velocity - Vector2.Dot(velocity, direction) * direction + direction * SpeedForHeight(bouncer.Height);

        // Obiekt obrocony bokiem odrzuca w poziomie - przez chwile sterowanie nie zawraca postaci (jak przy odbiciu od sciany).
        if (Mathf.Abs(direction.x) > 0.5f)
        {
            _inputLockUntil = Time.time + WallJumpInputLockSeconds;
        }

        bouncer.PlayAnimation();

        if (bouncer.PlacedIndex >= 0)
        {
            PlayBounceEffectRpc(bouncer.PlacedIndex);
        }
    }

    [Rpc(SendTo.NotMe)]
    private void PlayBounceEffectRpc(int placedIndex)
    {
        if (PlacementManager.Instance != null)
        {
            PlacementManager.Instance.PlayBounceEffect(placedIndex);
        }
    }

    private float GetJumpSpeed()
    {
        return SpeedForHeight(JumpHeight);
    }

    // Predkosc startowa, z ktora postac wzniesie sie dokladnie o tyle kratek. Fizyka liczy w krokach (semi-implicit Euler),
    // co zjada ok. pol kroku wzlotu - doliczamy to, zeby skok na 1.5 kratki naprawde siegal 1.5 kratki.
    private float SpeedForHeight(float heightCells)
    {
        float gravity = Mathf.Abs(Physics2D.gravity.y) * _gravityScale;
        float halfStep = gravity * Time.fixedDeltaTime * 0.5f;
        return halfStep + Mathf.Sqrt(halfStep * halfStep + 2f * gravity * Mathf.Max(0f, heightCells));
    }

    // Czego postac dotyka po ostatnim kroku fizyki. wallSide: +1 = sciana po lewej (odbicie w prawo), -1 = po prawej, 0 = brak.
    // isOnIce: postac stoi na sliskiej powierzchni (SlipperySurface).
    private void ReadContacts(out bool isGrounded, out int wallSide, out bool isOnIce)
    {
        isGrounded = false;
        wallSide = 0;
        isOnIce = false;
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

                if (contact.collider.TryGetComponent(out SlipperySurface _) || contact.otherCollider.TryGetComponent(out SlipperySurface _))
                {
                    isOnIce = true;
                }
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
