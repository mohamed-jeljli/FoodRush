using UnityEngine;
using TMPro;
using Unity.Netcode;

public class MoneyManager : NetworkBehaviour
{
    public static MoneyManager Instance;

    [SerializeField] private TMP_Text moneyText;

    // This is the synchronized money value
    private NetworkVariable<int> syncedMoney = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server  // Only server can change it
    );

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Update UI when value changes (including initial sync)
        syncedMoney.OnValueChanged += OnMoneyChanged;
        UpdateUI(syncedMoney.Value); // Initial update

        if (IsServer)
        {
            Debug.Log("MoneyManager: Server initialized money to 0");
        }
    }

    public override void OnNetworkDespawn()
    {
        syncedMoney.OnValueChanged -= OnMoneyChanged;
    }

    private void OnMoneyChanged(int previous, int current)
    {
        UpdateUI(current);
        Debug.Log($"[SYNCED] Money changed: {previous} → {current} (Client {NetworkManager.Singleton.LocalClientId})");
    }

    private void UpdateUI(int amount)
    {
        if (moneyText != null)
        {
            moneyText.text = $"$ {amount}";
        }
    }

    // Call this from anywhere (e.g., when player collects coin)
    public void AddMoney(int amount)
    {
        if (!IsServer)
        {
            // Clients ask server to add money
            AddMoneyServerRpc(amount);
        }
        else
        {
            // Server directly adds
            syncedMoney.Value += amount;
        }
    }

    [ServerRpc(RequireOwnership = false)] // Anyone can request money add
    private void AddMoneyServerRpc(int amount)
    {
        syncedMoney.Value += amount;
    }

    public int GetMoney()
    {
        return syncedMoney.Value;
    }

    public void ResetMoney()
    {
        if (IsServer)
        {
            syncedMoney.Value = 0;
        }
        else
        {
            ResetMoneyServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResetMoneyServerRpc()
    {
        syncedMoney.Value = 0;
    }
}