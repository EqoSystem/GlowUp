using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class CardHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Hover & Selection Settings")]
    [SerializeField] private float scaleAmount = 1.05f;
    [SerializeField] private float animationSpeed = 15f;
    [SerializeField] private Outline outlineEffect;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private Coroutine scaleCoroutine;

    private bool isSelected = false;
    private static CardHoverEffect currentlySelectedCard;

    void Start()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;

        if (outlineEffect == null)
        {
            outlineEffect = GetComponent<Outline>();
        }

        if (outlineEffect != null)
        {
            outlineEffect.enabled = false;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        
        if (!isSelected)
        {
            targetScale = originalScale * scaleAmount;
            StartScaleAnimation();

            if (outlineEffect != null)
                outlineEffect.enabled = true;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
      
        if (!isSelected)
        {
            targetScale = originalScale;
            StartScaleAnimation();

            if (outlineEffect != null)
                outlineEffect.enabled = false;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        
        if (currentlySelectedCard != null && currentlySelectedCard != this)
        {
            currentlySelectedCard.DeselectCard();
        }

        currentlySelectedCard = this;
        isSelected = true;

      
        targetScale = originalScale * scaleAmount;
        StartScaleAnimation();

        if (outlineEffect != null)
            outlineEffect.enabled = true;
    }

    public void DeselectCard()
    {
        isSelected = false;
        targetScale = originalScale;
        StartScaleAnimation();

        if (outlineEffect != null)
            outlineEffect.enabled = false;
    }

    private void StartScaleAnimation()
    {
        if (scaleCoroutine != null)
        {
            StopCoroutine(scaleCoroutine);
        }
        scaleCoroutine = StartCoroutine(ScaleRoutine());
    }

    IEnumerator ScaleRoutine()
    {
        while (Vector3.Distance(transform.localScale, targetScale) > 0.001f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * animationSpeed);
            yield return null;
        }
        transform.localScale = targetScale;
    }
}