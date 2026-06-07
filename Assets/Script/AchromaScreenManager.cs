using System;
using System.Collections.Generic;
using UnityEngine;

// Attach one instance to Wall Screen and one to Floor Screen.
// All child content GameObjects should be DISABLED in the scene by default.
// When ShowState() is called, only the matching child is enabled; all others are disabled.
// VideoPlayers on children should use "Play on Awake" — they start automatically when enabled.
[AddComponentMenu("TD/Achroma/Screen Manager")]
public class AchromaScreenManager : MonoBehaviour
{
    [Serializable]
    public class StateSlot
    {
        public TDAchromaFlowManager.AchromaState state;
        [Tooltip("Child GameObject for this state. Keep it disabled in the Editor by default.")]
        public GameObject content;
    }

    [Tooltip("One entry per AchromaState. Leave content null to show nothing for that state.")]
    public List<StateSlot> slots = new List<StateSlot>();

    private void Awake()
    {
        // Ensure all content starts disabled regardless of Editor state.
        foreach (var slot in slots)
            if (slot.content != null) slot.content.SetActive(false);
    }

    public GameObject GetContentForState(TDAchromaFlowManager.AchromaState state)
    {
        foreach (var slot in slots)
            if (slot.state == state) return slot.content;
        return null;
    }

    // Called by TDAchromaFlowManager on every state transition.
    public void ShowState(TDAchromaFlowManager.AchromaState state)
    {
        foreach (var slot in slots)
        {
            if (slot.content == null) continue;
            slot.content.SetActive(slot.state == state);
            // VideoPlayer "Play on Awake" fires automatically when SetActive(true) is called.
        }
    }
}
