using UnityEngine;

#region Glued Follower
// Obiekt przyklejony miodem do tostu (sam miod albo to, co na nim lezy) leci razem z tostem: w fazie biegu przesuwa sie
// o tyle, o ile tost jest teraz wysuniety z tostera. Miedzy wyskokami stoi tam, gdzie go postawiono.
// Wszystko liczone u kazdego gracza z czasu serwera - nic nie jest wysylane.
public class GluedFollower : MonoBehaviour
{
    private const float PixelsPerUnit = 32f;

    private CatapultShooter _carrier;
    private Vector3 _basePosition;
    private Rigidbody2D _body;

    public static void Link(GameObject view, CatapultShooter carrier)
    {
        if (!view.TryGetComponent(out GluedFollower follower))
        {
            follower = view.AddComponent<GluedFollower>();
            follower._basePosition = view.transform.position;
            follower.DetachFromSharedCollider(view);
        }

        follower._carrier = carrier;
    }

    // Bryly (noz, lod, chleb...) wychodza ze wspolnego collidera postawionych obiektow i dostaja wlasne cialo kinematyczne:
    // ruszane fizyka pchaja i nosza postacie, a strefy (ostrze, odbicie) jada razem z nimi. Miod nie ma colliderow.
    private void DetachFromSharedCollider(GameObject view)
    {
        Collider2D[] colliders = view.GetComponentsInChildren<Collider2D>();

        if (colliders.Length == 0)
        {
            return;
        }

        foreach (Collider2D viewCollider in colliders)
        {
            viewCollider.compositeOperation = Collider2D.CompositeOperation.None;
        }

        _body = view.AddComponent<Rigidbody2D>();
        _body.bodyType = RigidbodyType2D.Kinematic;
        _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        _body.position = _basePosition;
    }

    private Vector3 GetTargetPosition()
    {
        Vector2 offset = _carrier != null ? _carrier.GetPopOffset() : Vector2.zero;

        // Pozycja co do piksela, tak jak tost - obiekt nie drga wzgledem niego.
        offset.x = Mathf.Round(offset.x * PixelsPerUnit) / PixelsPerUnit;
        offset.y = Mathf.Round(offset.y * PixelsPerUnit) / PixelsPerUnit;
        return _basePosition + (Vector3)offset;
    }

    private void FixedUpdate()
    {
        if (_body != null)
        {
            _body.MovePosition(GetTargetPosition());
        }
    }

    private void LateUpdate()
    {
        if (_body == null)
        {
            transform.position = GetTargetPosition();
        }
    }
}
#endregion
