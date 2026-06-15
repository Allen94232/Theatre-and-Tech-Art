using System;
using System.Collections;
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

    [Header("Transition")]
    [Tooltip("Seconds for the cross-fade (old content fades out while new content fades in simultaneously).")]
    [SerializeField] [Range(0.05f, 1.5f)] private float _fadeDuration = 0.35f;

    private void Awake()
    {
        foreach (var slot in slots)
        {
            if (slot.content == null) continue;
            slot.content.SetActive(false);
            // Pre-add CanvasGroup so cross-fades work without runtime AddComponent cost.
            if (slot.content.GetComponent<CanvasGroup>() == null)
                slot.content.AddComponent<CanvasGroup>();
        }
    }

    public GameObject GetContentForState(TDAchromaFlowManager.AchromaState state)
    {
        foreach (var slot in slots)
            if (slot.state == state) return slot.content;
        return null;
    }

    public void ShowState(TDAchromaFlowManager.AchromaState state)
    {
        StopAllCoroutines();
        StartCoroutine(ShowStateCrossFadeCo(state));
    }

    public void ShowStateInstant(TDAchromaFlowManager.AchromaState state)
    {
        StopAllCoroutines();
        foreach (var slot in slots)
        {
            if (slot.content == null) continue;
            bool active = slot.state == state;
            slot.content.SetActive(active);
            var cg = slot.content.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = active ? 0f : 1f;
        }
    }

    // Fades out the currently active content's CanvasGroup from 1 → 0 over duration.
    public void FadeOutActiveContent(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeContentCo(1f, 0f, duration));
    }

    // Fades in the currently active content's CanvasGroup from 0 → 1 over duration.
    public void FadeInActiveContent(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeContentCo(0f, 1f, duration));
    }

    // Instantly sets the active content's CanvasGroup alpha (used when fadeIn = 0).
    public void SetActiveContentAlpha(float alpha)
    {
        foreach (var slot in slots)
        {
            if (slot.content == null || !slot.content.activeSelf) continue;
            var cg = slot.content.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = alpha;
            break;
        }
    }

    private IEnumerator FadeContentCo(float from, float to, float duration)
    {
        CanvasGroup cg = null;
        foreach (var slot in slots)
        {
            if (slot.content != null && slot.content.activeSelf)
            {
                cg = slot.content.GetComponent<CanvasGroup>();
                break;
            }
        }
        if (cg == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        cg.alpha = to;
    }

    private void ApplyState(TDAchromaFlowManager.AchromaState state)
    {
        foreach (var slot in slots)
        {
            if (slot.content == null) continue;
            slot.content.SetActive(slot.state == state);
        }
    }

    private IEnumerator ShowStateCrossFadeCo(TDAchromaFlowManager.AchromaState state)
    {
        // Find currently active content
        GameObject oldContent = null;
        foreach (var slot in slots)
            if (slot.content != null && slot.content.activeSelf) { oldContent = slot.content; break; }

        // Find new content
        GameObject newContent = null;
        foreach (var slot in slots)
            if (slot.state == state) { newContent = slot.content; break; }

        if (newContent == oldContent) yield break;
        if (newContent == null) { ApplyState(state); yield break; }

        CanvasGroup oldCG = oldContent != null ? oldContent.GetComponent<CanvasGroup>() : null;
        CanvasGroup newCG = newContent.GetComponent<CanvasGroup>();

        // Enable new content fully transparent so VideoPlayer "Play on Awake" can start
        if (newCG != null) newCG.alpha = 0f;
        newContent.SetActive(true);
        if (oldCG != null) oldCG.alpha = 1f;

        // Cross-fade: old out, new in simultaneously
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeDuration);
            if (oldCG != null) oldCG.alpha = 1f - t;
            if (newCG != null) newCG.alpha = t;
            yield return null;
        }

        if (newCG != null) newCG.alpha = 1f;

        // Disable all other content and reset their alpha for next transition
        foreach (var slot in slots)
        {
            if (slot.content == null || slot.content == newContent) continue;
            var cg = slot.content.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
            slot.content.SetActive(false);
        }
    }
}
