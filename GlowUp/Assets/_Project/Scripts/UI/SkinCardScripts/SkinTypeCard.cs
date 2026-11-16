using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkinTypeCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI cardLabelText; 
    [SerializeField] private Button continueButton;         

    private Button cardButton;
    private static SkinTypeCard selectedInstance;

    void Start()
    {
   
        cardButton = GetComponent<Button>();
        if (cardButton == null)
        {
            cardButton = gameObject.AddComponent<Button>();
        }

   
        cardButton.onClick.AddListener(OnCardSelected);

        
        if (continueButton != null)
        {
             
        }
    }

    public void OnCardSelected()
    {
        selectedInstance = this;

        
        string skinTypeName = (cardLabelText != null) ? cardLabelText.text : "Unknown";

        
        PlayerPrefs.SetString("SelectedSkinType", skinTypeName);
        PlayerPrefs.Save();

        { }

      
        if (continueButton != null)
        {
            continueButton.interactable = true;
        }
    }
}