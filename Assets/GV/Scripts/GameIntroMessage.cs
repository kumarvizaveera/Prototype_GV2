using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GameIntroMessage : MonoBehaviour
{
    [Tooltip("Reference to the TextMeshPro component. If empty, tries to find one on this GameObject.")]
    public TMP_Text messageText;

    [Tooltip("The message to display on start.")]
    [TextArea]
    public string message = "Complete your objectives and earn Guru’s knowledge to invoke Astras";

    [Tooltip("How long (in seconds) the message stays visible.")]
    public float duration = 5f;

    void Start()
    {
        // Auto-getter if not assigned
        if (messageText == null)
        {
            messageText = GetComponent<TMP_Text>();
        }

        if (messageText != null)
        {
            messageText.text = message;
            // Ensure it's visible initially
            messageText.gameObject.SetActive(true);
            
            StartCoroutine(HideMessageRoutine());
        }
        else
        {
            Debug.LogWarning("GameIntroMessage: No TMP_Text assigned or found on this GameObject. Please assign one.");
        }
    }

    IEnumerator HideMessageRoutine()
    {
        yield return new WaitForSeconds(duration);

        if (messageText != null)
        {
            messageText.gameObject.SetActive(false);
        }
    }
}
