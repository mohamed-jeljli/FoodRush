using UnityEngine;

public class OrderBubbleBillboard : MonoBehaviour
{
    private Camera playerCamera; // Use local player's camera

    private void Start()
    {
        // Find the local player's camera (adjust if your camera setup differs)
        playerCamera = Camera.main; // Or: FindObjectOfType<PlayerCamera>()
    }

    private void LateUpdate()
    {
        if (playerCamera == null) return;

        // Make bubble face the camera (billboard)
        transform.LookAt(playerCamera.transform);
        transform.Rotate(0, 180f, 0); // Flip to show front face
    }
}