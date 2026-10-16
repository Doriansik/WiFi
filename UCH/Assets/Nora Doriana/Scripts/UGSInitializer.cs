using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;
using System.Threading.Tasks;
using System;

#region Interfaces
public interface IAuthenticationService
{
    Task AuthenticatePlayerAsync();
}
#endregion

#region Unity Gaming Services
public class UGSInitializer : MonoBehaviour, IAuthenticationService
{
    private const int MaxRetries = 3;
    private const int DelayBetweenRetriesMilliseconds = 1000;
    private const string PrimaryProfileName = "PrimaryEditor";

    private async void Start()
    {
        await AuthenticatePlayerAsync();
    }

    public async Task AuthenticatePlayerAsync()
    {
        int attempt = 0;

        while (attempt < MaxRetries)
        {
            try
            {
                InitializationOptions options = new InitializationOptions();

#if UNITY_EDITOR
                if (ParrelSync.ClonesManager.IsClone())
                {
                    string cloneProfile = "Clone_" + Guid.NewGuid().ToString().Substring(0, 8);
                    options.SetProfile(cloneProfile);
                }
                else
                {
                    options.SetProfile(PrimaryProfileName);
                }
#endif

                await UnityServices.InitializeAsync(options);

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                break;
            }
            catch (Exception exception)
            {
                attempt++;
                Debug.LogException(exception);

                if (attempt >= MaxRetries)
                {
                    throw;
                }

                await Task.Delay(DelayBetweenRetriesMilliseconds);
            }
        }
    }
}
#endregion