using System;
using System.Collections.Generic;

namespace GlowUp.Scanning
{
    
    public enum GeminiErrorCode
    {
        None = 0,
        NoInternet,        
        Timeout,             
        RateLimited,         
        ServerError,         
        ModelNotFound,       
        AuthError,           
        BadRequest,          
        MalformedResponse,   
        Cancelled,           
        Unknown
    }

    [Serializable]
    public class GeminiScanResult
    {
        public bool Success;
        public GeminiErrorCode ErrorCode;
        public string ErrorMessage;      
        public string UserFacingMessage; 
        public int AttemptsMade;
        public string ModelUsed;

        public ProductAnalysis Analysis; 

        public static GeminiScanResult Failed(GeminiErrorCode code, string devMessage, string userMessage, int attempts)
            => new GeminiScanResult
            {
                Success = false,
                ErrorCode = code,
                ErrorMessage = devMessage,
                UserFacingMessage = userMessage,
                AttemptsMade = attempts
            };

        public static GeminiScanResult Ok(ProductAnalysis analysis, string modelUsed, int attempts)
            => new GeminiScanResult
            {
                Success = true,
                ErrorCode = GeminiErrorCode.None,
                Analysis = analysis,
                ModelUsed = modelUsed,
                AttemptsMade = attempts
            };
    }

   
    [Serializable]
    public class ProductAnalysis
    {
        public string ProductSummary;
        public bool IsSafeForUser;
        public List<string> RawIngredients = new List<string>();
        public List<SafetyFlag> SafetyFlags = new List<SafetyFlag>();
        public List<string> MatchedAllergens = new List<string>();
        public List<string> RecommendationNotes = new List<string>();
        public List<RegulatoryInsight> RegulatoryInsights = new List<RegulatoryInsight>();
        public List<HiddenIngredientAlert> HiddenIngredientAlerts = new List<HiddenIngredientAlert>();

        // Optional extras requested by the scan pipeline (left empty when the model does not return them)
        public string ProductName;
        public string BrandName;
        public string CosmeticType;
        public string Explanation;
        public List<string> KeyActives = new List<string>();
    }

    [Serializable]
    public class RegulatoryInsight
    {
        public string Ingredient;
        public string Region;
        public string Status;
        public string Explanation;
        public string SourceUrl;
        public string Confidence; 
    }

    [Serializable]
    public class HiddenIngredientAlert
    {
        public string LabelTerm;
        public string WhyItsOpaque;
        public List<string> CommonlyHiddenSubstances = new List<string>();
        public string RelevanceToUserProfile;
    }

    [Serializable]
    public class SafetyFlag
    {
        public string Ingredient;
        public string Severity;  
        public string Reason;
    }
}