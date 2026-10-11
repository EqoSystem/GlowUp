using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BaselineCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI cardLabelText; 
    [SerializeField] private Button continueButton;         

    void Start()
    {
        
        if (continueButton != null)
        {
          
        }
    }

    public void SaveBaselineChoice()
    {
        string baselineName = (cardLabelText != null) ? cardLabelText.text : "Unknown";

      
        PlayerPrefs.SetString("SelectedBaseline", baselineName);
        PlayerPrefs.Save();

        Debug.Log("Selected Baseline Mapping: " + baselineName);

     
        if (continueButton != null)
        {
            continueButton.interactable = true;
        }
    }
}