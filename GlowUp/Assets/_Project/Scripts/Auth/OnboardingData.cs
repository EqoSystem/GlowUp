using UnityEngine;

public class OnboardingData : MonoBehaviour
{
    public static OnboardingData Instance;

    // Collected fields across onboarding
    public string firstName;
    public string middleName;
    public string lastName;
    public string birthday;
    public string age;
    public string email;
    public string password;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}