using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine.UI;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;

using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

public class LobbyMenu : Panel
{
    [SerializeField] private LobbyPlayerItem lobbyPlayerItemPrefab = null;
    [SerializeField] private RectTransform lobbyPlayersContainer = null;
    [SerializeField] public TextMeshProUGUI nameText = null;
    [SerializeField] private Button closeButton = null;
    [SerializeField] private Button leaveButton = null;
    [SerializeField] private Button readyButton = null;
    [SerializeField] private Button startButton = null;

    private Lobby lobby = null;
    public Lobby JoinedLobby { get { return lobby; } }

    private float updateTimer = 0f;
    private float heartbeatPeriod = 15f;
    private bool sendingHeartbeat = false;
    private ILobbyEvents events = null;
    private bool isReady = false;
    private bool isHost = false;
    private string eventsLobbyId = "";
    private bool isJoining = false;

    public override void Initialize()
    {
        if (IsInitialized) return;

        ClearPlayersList();

        closeButton.onClick.AddListener(ClosePanel);
        leaveButton.onClick.AddListener(LeaveLobby);
        readyButton.onClick.AddListener(SwitchReady);
        startButton.onClick.AddListener(StartGame);

        base.Initialize();
    }

    // This method is added back so other scripts (like LobbySettingsMenu) can keep using it without errors
    public async Task UpdateLobby(UpdateLobbyOptions options)
    {
        if (lobby == null) return;

        try
        {
            lobby = await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, options);
            LoadPlayers(); // Refresh player list in case something changed
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to update lobby: " + ex.Message);
        }
    }

    private async void StartGame()
    {
        PanelManager.Open("loading");

        try
        {
            // Create Relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(lobby.MaxPlayers);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var serverData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transport.SetRelayServerData(serverData);

            // Get join code
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // Save session info
            SessionManager.role = SessionManager.Role.Host;
            SessionManager.joinCode = code;
            SessionManager.lobbyID = lobby.Id;

            // Update lobby data with join code
            await SetLobbyStarting(code);

            // Clean up lobby events
            await UnsubscribeToEventsAsync();

            // Close all UI panels and menu canvas
            PanelManager.CloseAll();
            if (MenuManager.Singleton != null) MenuManager.Singleton.CloseCanvas();

            // Start the network as Host (exactly like your MainMenu host button)
            NetworkManager.Singleton.StartHost();

            // OPTIONAL: Load a dedicated game scene (uncomment if needed)
            // NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);

            Debug.Log("Host successfully started the game via Lobby + Relay!");
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to start game: " + ex.Message);
            PanelManager.Close("loading");
        }
    }

    private async Task SetLobbyStarting(string joinCode)
    {
        try
        {
            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "started", new DataObject(DataObject.VisibilityOptions.Public, "1") },
                    { "join_code", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                }
            };
            lobby = await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, options);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to set lobby starting: " + e.Message);
        }
    }

    private void Update()
    {
        if (lobby == null) return;

        // Clients poll for join code to auto-join
        if (!isHost && !isJoining)
        {
            CheckStartGameStatus();
        }

        // Host heartbeat
        if (lobby.HostId == AuthenticationService.Instance.PlayerId && !sendingHeartbeat)
        {
            updateTimer += Time.deltaTime;
            if (updateTimer >= heartbeatPeriod)
            {
                updateTimer = 0f;
                HeartbeatLobbyAsync();
            }
        }
    }

    private async void HeartbeatLobbyAsync()
    {
        sendingHeartbeat = true;
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(lobby.Id);
        }
        catch (Exception exception)
        {
            Debug.LogError(exception.Message);
        }
        sendingHeartbeat = false;
    }

    public void Open(Lobby lobby)
    {
        if (eventsLobbyId != lobby.Id)
        {
            _ = SubscribeToEventsAsync(lobby.Id);
        }

        this.lobby = lobby;
        nameText.text = lobby.Name;

        CheckStartGameStatus();
        startButton.gameObject.SetActive(false);
        isHost = false;

        LoadPlayers();
        base.Open();
    }

    private void CheckStartGameStatus()
    {
        if (lobby.Data != null && lobby.Data.ContainsKey("join_code"))
        {
            string joinCode = lobby.Data["join_code"].Value;
            if (!isJoining && !string.IsNullOrEmpty(joinCode))
            {
                JoinGame(joinCode);
            }
        }
    }

    private async void JoinGame(string joinCode)
    {
        if (string.IsNullOrEmpty(joinCode)) return;

        isJoining = true;
        PanelManager.Open("loading");

        try
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var serverData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transport.SetRelayServerData(serverData);

            SessionManager.role = SessionManager.Role.Client;
            SessionManager.joinCode = joinCode;
            SessionManager.lobbyID = lobby.Id;

            await UnsubscribeToEventsAsync();

            PanelManager.CloseAll();
            if (MenuManager.Singleton != null) MenuManager.Singleton.CloseCanvas();

            // Start network as Client (exactly like your MainMenu client button)
            NetworkManager.Singleton.StartClient();

            Debug.Log("Client successfully joined the game via Lobby + Relay!");
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to join game: " + ex.Message);
            await Leave();
            isJoining = false;
        }
        finally
        {
            PanelManager.Close("loading");
        }
    }

    private void LoadPlayers()
    {
        ClearPlayersList();

        bool isEveryoneReady = true;
        bool youAreMember = false;

        for (int i = 0; i < lobby.Players.Count; i++)
        {
            LobbyPlayer lp = lobby.Players[i];
            bool ready = lp.Data != null && lp.Data.ContainsKey("ready") && lp.Data["ready"].Value == "1";

            LobbyPlayerItem item = Instantiate(lobbyPlayerItemPrefab, lobbyPlayersContainer);
            item.Initialize(lp, lobby.Id, lobby.HostId);

            string playerId = lp.Data != null && lp.Data.ContainsKey("id") ? lp.Data["id"].Value : "";

            if (playerId == AuthenticationService.Instance.PlayerId)
            {
                youAreMember = true;
                isReady = ready;
                isHost = playerId == lobby.HostId;
            }

            if (!ready) isEveryoneReady = false;
        }

        startButton.gameObject.SetActive(isHost);
        if (isHost) startButton.interactable = isEveryoneReady;

        if (!youAreMember) Close();
    }

    public async void CreateLobby(string lobbyName, int maxPlayers, bool isPrivate, string mode, string map, string language)
    {
        PanelManager.Open("loading");
        try
        {
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Data = new Dictionary<string, DataObject>
                {
                    { "mode", new DataObject(DataObject.VisibilityOptions.Public, mode) },
                    { "map", new DataObject(DataObject.VisibilityOptions.Public, map) },
                    { "language", new DataObject(DataObject.VisibilityOptions.Public, language) },
                },
                Player = new LobbyPlayer
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "id", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, AuthenticationService.Instance.PlayerId) },
                        { "name", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, AuthenticationService.Instance.PlayerName) },
                        { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "0") }
                    }
                }
            };

            lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            PanelManager.Close("lobby_search");
            Open(lobby);
        }
        catch (Exception exception)
        {
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to create the lobby.", "OK");
            Debug.LogError(exception.Message);
        }
        finally
        {
            PanelManager.Close("loading");
        }
    }

    public async void JoinLobby(string id)
    {
        PanelManager.Open("loading");
        try
        {
            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = new LobbyPlayer
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "id", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, AuthenticationService.Instance.PlayerId) },
                        { "name", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, AuthenticationService.Instance.PlayerName) },
                        { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "0") }
                    }
                }
            };

            lobby = await LobbyService.Instance.JoinLobbyByIdAsync(id, options);
            PanelManager.Close("lobby_search");
            Open(lobby);
        }
        catch (Exception exception)
        {
            ErrorMenu panel = (ErrorMenu)PanelManager.GetSingleton("error");
            panel.Open(ErrorMenu.Action.None, "Failed to join the lobby.", "OK");
            Debug.LogError(exception.Message);
        }
        finally
        {
            PanelManager.Close("loading");
        }
    }

    private void ClearPlayersList()
    {
        if (lobbyPlayersContainer == null) return;
        foreach (Transform child in lobbyPlayersContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void ClosePanel()
    {
        Close();
    }

    private void LeaveLobby()
    {
        _ = Leave();
    }

    private async Task Leave()
    {
        PanelManager.Open("loading");
        try
        {
            if (lobby != null)
            {
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
            }
            lobby = null;
            Close();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
        finally
        {
            PanelManager.Close("loading");
        }
    }

    private async Task<bool> SubscribeToEventsAsync(string lobbyId)
    {
        try
        {
            var callbacks = new LobbyEventCallbacks();
            callbacks.LobbyChanged += OnChanged;
            callbacks.KickedFromLobby += OnKicked;

            events = await LobbyService.Instance.SubscribeToLobbyEventsAsync(lobbyId, callbacks);
            eventsLobbyId = lobbyId;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
            return false;
        }
    }

    private async Task UnsubscribeToEventsAsync()
    {
        try
        {
            if (events != null)
            {
                await events.UnsubscribeAsync();
                events = null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
    }

    private void SwitchReady()
    {
        _ = SwitchReadyAsync();
    }

    private async Task SwitchReadyAsync()
    {
        readyButton.interactable = false;
        try
        {
            var options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, isReady ? "0" : "1") }
                }
            };

            lobby = await LobbyService.Instance.UpdatePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId, options);
            LoadPlayers();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
        finally
        {
            readyButton.interactable = true;
        }
    }

    private void OnKicked()
    {
        if (IsOpen) Close();
        lobby = null;
        events = null;
        isJoining = false;
    }

    private void OnChanged(ILobbyChanges changes)
    {
        if (changes.LobbyDeleted)
        {
            if (IsOpen) Close();
            lobby = null;
            events = null;
            isJoining = false;
            return;
        }

        changes.ApplyToLobby(lobby);
        CheckStartGameStatus();
        if (IsOpen) LoadPlayers();
    }
}