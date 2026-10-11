using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Firebase.Firestore;

public class FirestoreManager : MonoBehaviour
{
    public static FirestoreManager Instance { get; private set; }
    private FirebaseFirestore db;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeFirebase();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void InitializeFirebase()
    {
        db = FirebaseFirestore.DefaultInstance;
        Debug.Log("[FirestoreManager] Initialized successfully.");
    }

    public void LookupProductByIngredients(List<string> normalizedIngredients, Action<bool, string, string> onResultFound)
    {
        if (db == null)
        {
            db = FirebaseFirestore.DefaultInstance;
        }

        if (normalizedIngredients == null || normalizedIngredients.Count == 0)
        {
            Debug.LogWarning("[FirestoreManager] No ingredients provided for lookup.");
            onResultFound?.Invoke(false, "", "No ingredients provided.");
            return;
        }

        // Firestore 'WhereIn' queries support up to 10 comparison values safely.
        List<string> queryIngredients = normalizedIngredients.Count > 10
            ? normalizedIngredients.GetRange(0, 10)
            : normalizedIngredients;

        // Query the 'products' collection where Document ID matches any of the scanned ingredients
        Query query = db.Collection("products").WhereIn(FieldPath.DocumentId, queryIngredients);

        query.GetSnapshotAsync().ContinueWith(task => {
            if (task.IsCompleted)
            {
                QuerySnapshot snapshot = task.Result;
                DocumentSnapshot doc = snapshot.Documents.FirstOrDefault();

                if (doc != null)
                {
                    string productName = doc.ContainsField("productName") ? doc.GetValue<string>("productName") : doc.Id;
                    string productDescription = doc.ContainsField("description") ? doc.GetValue<string>("description") : "No description available in database.";

                    Debug.Log($"[FirestoreManager] Product found via multi-ingredient query: {productName}");
                    onResultFound?.Invoke(true, productName, productDescription);
                }
                else
                {
                    Debug.Log("[FirestoreManager] No product match found for the scanned ingredients.");
                    onResultFound?.Invoke(false, "", "Product not found in local database.");
                }
            }
            else
            {
                Debug.LogError($"[FirestoreManager] Failed to query Firestore: {task.Exception}");
                onResultFound?.Invoke(false, "", "Database query error.");
            }
        });
    }
}