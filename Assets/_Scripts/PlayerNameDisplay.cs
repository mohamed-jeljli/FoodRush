using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;

public class PlayerNameDisplay : NetworkBehaviour
{
    [SerializeField] private TextMeshPro nameTag; // Assign in prefab inspector

    private NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private void Awake()
    {
        Debug.Log($"PlayerNameDisplay Awake - GameObject: {gameObject.name} (Instantiated locally)");
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"OnNetworkSpawn - IsOwner: {IsOwner} | ClientID: {OwnerClientId} | LocalClientID: {NetworkManager.Singleton.LocalClientId}");

        if (nameTag == null)
        {
            Debug.LogError("nameTag not assigned!");
            return;
        }

        nameTag.text = playerName.Value.ToString(); // Initial value
        playerName.OnValueChanged += (oldValue, newValue) => nameTag.text = newValue.ToString();

        if (IsOwner)
        {
            string authName = AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerName
                : $"Player{OwnerClientId}";
            playerName.Value = authName;
            Debug.Log($"Owner set name: {authName}");
        }
    }
}