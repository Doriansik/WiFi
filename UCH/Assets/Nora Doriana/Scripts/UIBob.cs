using UnityEngine;

#region UI Bob
// Buja elementem UI gora-dol wokol pozycji startowej (np. logo w menu).
[RequireComponent(typeof(RectTransform))]
public class UIBob : MonoBehaviour
{
    [SerializeField] private float _amplitude = 15f;
    [SerializeField] private float _speed = 2f;

    private RectTransform _rect;
    private Vector2 _startPosition;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _startPosition = _rect.anchoredPosition;
    }

    private void Update()
    {
        float offset = Mathf.Sin(Time.unscaledTime * _speed) * _amplitude;
        _rect.anchoredPosition = _startPosition + Vector2.up * offset;
    }
}
#endregion
