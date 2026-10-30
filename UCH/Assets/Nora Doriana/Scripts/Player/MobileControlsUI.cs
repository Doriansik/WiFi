using UnityEngine;

#region Mobile Controls UI
// Przyciski ruchu widza tylko gracze, ktorzy dolaczyli przez JOIN (telefony / klon ParrelSync).
// Host na PC steruje klawiatura - PlayerController sam przelacza sie na klawiature, gdy przyciski nic nie daja.
public class MobileControlsUI : MonoBehaviour
{
    [SerializeField] private GameObject _controlsRoot;

    private void Awake()
    {
        _controlsRoot.SetActive(!MainMenuUI.IsHostDevice());
    }
}
#endregion
