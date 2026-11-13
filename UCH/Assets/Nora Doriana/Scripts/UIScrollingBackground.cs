using UnityEngine;
using UnityEngine.UI;

#region UI Scrolling Background
// Kafelkowane tlo UI (RawImage) przesuwajace sie w strone prawego dolnego rogu.
// Tekstura musi miec Wrap Mode = Repeat.
[RequireComponent(typeof(RawImage))]
public class UIScrollingBackground : MonoBehaviour
{
    [SerializeField] private float _tileSize = 128f;
    [SerializeField] private Vector2 _direction = new Vector2(1f, -1f);
    [SerializeField] private float _speed = 0.25f;

    private RawImage _image;
    private RectTransform _rect;
    private Vector2 _offset;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        _rect = (RectTransform)transform;
    }

    private void Update()
    {
        // Tresc jedzie w prawo-dol, wiec UV przesuwamy w przeciwna strone.
        _offset -= _direction.normalized * (_speed * Time.unscaledDeltaTime);
        _offset.x = Mathf.Repeat(_offset.x, 1f);
        _offset.y = Mathf.Repeat(_offset.y, 1f);

        Vector2 size = _rect.rect.size / _tileSize;
        _image.uvRect = new Rect(_offset, size);
    }
}
#endregion
