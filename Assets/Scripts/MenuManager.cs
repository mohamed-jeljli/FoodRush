using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Vivox;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    private bool initialized = false;
    private bool eventInitialized = false;
    private static MenuManager singleton = null;
    private string lastUserName = "";
    private bool isInChannel = false; // track channel join
    private bool isTalking = false;

    [SerializeField] private Canvas canvs;

    public static MenuManager Singleton
    {
        get
        {
            if (singleton == null)
            {
                singleton = FindAnyObjectByType<MenuManager>();
                singleton.Initialize();
            }
            return singleton;
        }
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
    }

    private void Awake()
    {
        Application.runInBackground = true;
        StartClientService();
    }

    private void OnDestroy()
    {
        if (singleton == this)
            singleton = null;
    }

    public void CloseCanvas()
    {
        canvs.gameObject.SetActive(false);
    }

    public async void StartClientService()
    {
        PanelManager.CloseAll();
        PanelManager.Open("loading");

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                options.SetProfile("default_profile");
                await UnityServices.InitializeAsync();
                await VivoxService.Instance.InitializeAsync();
            }

            if (!eventInitialized)
                SetupEvents();

            if (AuthenticationService.Instance.SessionTokenExists)
                await SignInAnonymouslyAsync();
            else
                PanelManager.Open("auth");
        }
        catch (Exception ex)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to connect to the network", "retry");
        }
    }

    private void SetupEvents()
    {
        eventInitialized = true;
        AuthenticationService.Instance.SignedIn += SignInConfirmAsync;
        AuthenticationService.Instance.SignedOut += () =>
        {
            PanelManager.CloseAll();
            PanelManager.Open("auth");
        };
        AuthenticationService.Instance.Expired += async () => await SignInAnonymouslyAsync();
    }

    public async Task SignInAnonymouslyAsync()
    {
        PanelManager.Open("loading");
        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            await OnSignedIn();
        }
        catch (AuthenticationException ex)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to sign in", "ok");
        }
        catch (RequestFailedException ex)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to connect to the network", "ok");
        }
    }

    public async void SignInWithUserNameAndPasswordAsync(string userName, string password)
    {
        lastUserName = userName.Trim();
        PanelManager.Open("loading");
        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(userName, password);
            await OnSignedIn();
        }
        catch (AuthenticationException)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Username or password is wrong", "ok");
        }
        catch (RequestFailedException)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to connect to the network", "ok");
        }
    }

    public async void SignUpWithUserNameAndPasswordAsync(string userName, string password)
    {
        PanelManager.Open("loading");
        try
        {
            await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(userName, password);
            await OnSignedIn();
        }
        catch (AuthenticationException)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to sign you up", "ok");
        }
        catch (RequestFailedException)
        {
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to connect to the network", "ok");
        }
    }
    public async void SignInWithUnityPlayerAccountsAsync()
    {
        PanelManager.Open("loading");

        try
        {
            // Subscribe to the SignedIn event (fires when browser callback succeeds)
            PlayerAccountService.Instance.SignedIn += OnPlayerAccountsSignedIn;

            // Start the browser sign-in flow
            await PlayerAccountService.Instance.StartSignInAsync();
        }
        catch (Exception ex)
        {
            PlayerAccountService.Instance.SignedIn -= OnPlayerAccountsSignedIn; // Clean up
            Debug.LogError("Unity Player Accounts start failed: " + ex.Message);
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to start sign in with Unity", "OK");
            PanelManager.Close("loading");
        }
    }

    private async void OnPlayerAccountsSignedIn()
    {
        // Unsubscribe to avoid multiple calls
        PlayerAccountService.Instance.SignedIn -= OnPlayerAccountsSignedIn;

        try
        {
            // Now use the AccessToken (not IdToken!) — this is what Unity docs recommend
            if (string.IsNullOrEmpty(PlayerAccountService.Instance.AccessToken))
            {
                throw new Exception("Access token missing after sign-in");
            }

            await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);

            await OnSignedIn(); // Your existing success flow (Vivox, etc.)
        }
        catch (Exception ex)
        {
            Debug.LogError("Unity Authentication sign-in failed: " + ex.Message);
            ShowError(ErrorMenu.Action.OpenAuthMenu, "Failed to sign in with Unity", "OK");
        }
        finally
        {
            PanelManager.Close("loading");
        }
    }
    private async Task OnSignedIn()
    {
        Debug.Log("✅ Unity Authentication complete, now logging into Vivox...");

       
     

        // Login to Vivox (use Unity Auth token automatically)
        await VivoxService.Instance.LoginAsync();

        // Join a default channel
        string channelName = "Lobby";
        await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);

        isInChannel = true;
        Debug.Log("🎤 Joined Vivox channel: " + channelName);

       // PanelManager.CloseAll();
      //  PanelManager.Open("main");
        await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None, "Lobby");
    }

    public void SignOut()
    {
        AuthenticationService.Instance.SignOut();
        PanelManager.CloseAll();
        PanelManager.Open("auth");
        
        isInChannel = false;
    }
    public async void LogoutOfVivoxAsync()
    {
        await VivoxService.Instance.LogoutAsync();
    }

    private void ShowError(ErrorMenu.Action action = ErrorMenu.Action.None, string error = "", string button = "")
    {
        PanelManager.Close("loading");
        ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
        panel.Open(action, error, button);
    }

    private async void SignInConfirmAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName))
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync("players");
            }
            PanelManager.CloseAll();
            PanelManager.Open("main");
        }
        catch (Exception) { }
    }

    private async void Update()
    {
        if (!isInChannel) return;

        string channelName = "Lobby";

        if (Input.GetKeyDown(KeyCode.P) )
        {
           await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.All,"Lobby");
            Debug.Log("talking");
        }

        if (Input.GetKeyUp(KeyCode.P))
        {
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None,"Lobby");
            Debug.Log("not talking");
        }
    }

   

}
