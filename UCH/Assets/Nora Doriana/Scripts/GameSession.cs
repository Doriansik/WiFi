using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#region Game Session
public static class GameSession
{
    public static MatchmakingManager Matchmaking =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.GetComponent<MatchmakingManager>() : null;

    // Rozlacza, wychodzi z lobby i wraca do main menu.
    // NetworkManager zyje w DontDestroyOnLoad - trzeba go zniszczyc, bo scena menu ma wlasny.
    public static void ReturnToMainMenu()
    {
        Time.timeScale = 1f;

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null)
        {
            Matchmaking?.LeaveSessionAsync();
            networkManager.Shutdown();
            Object.Destroy(networkManager.gameObject);
        }

        SceneManager.LoadScene(SceneNames.Menu);
    }
}
#endregion
