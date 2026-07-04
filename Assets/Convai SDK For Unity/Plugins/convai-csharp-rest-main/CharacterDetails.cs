using System;
using System.Collections.Generic;
using Convai.RestAPI.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI
{

    [Serializable]
    public class CharacterDetails
    {
        public CharacterDetails(MemorySettings memorySettings, string characterName, string userID, string characterID, string listing, List<string> languageCodes, string voiceType, List<string> characterActions, List<string> characterEmotions, ModelDetailsData modelDetails, string languageCode, GuardrailMetaData guardrailMeta, CharacterTraitsData characterTraits, string timestamp, string organizationId, string startNarrativeSectionId, object pronunciations, List<string> boostedWords, List<string> allowedModerationFilters, string uncensoredAccessConsent, string nsfwModelSize, string temperature, string backstory)
        {
            MemorySettings = memorySettings;
            CharacterName = characterName;
            UserID = userID;
            CharacterID = characterID;
            Listing = listing;
            LanguageCodes = languageCodes;
            VoiceType = voiceType;
            CharacterActions = characterActions;
            CharacterEmotions = characterEmotions;
            ModelDetails = modelDetails;
            LanguageCode = languageCode;
            GuardrailMeta = guardrailMeta;
            CharacterTraits = characterTraits;
            Timestamp = timestamp;
            OrganizationId = organizationId;
            StartNarrativeSectionId = startNarrativeSectionId;
            Pronunciations = pronunciations;
            BoostedWords = boostedWords;
            AllowedModerationFilters = allowedModerationFilters;
            UncensoredAccessConsent = uncensoredAccessConsent;
            NsfwModelSize = nsfwModelSize;
            Temperature = temperature;
            Backstory = backstory;
        }

        public static CharacterDetails Default()
        {
            return new CharacterDetails(
                MemorySettings.Default(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                new List<string>(),
                string.Empty,
                new List<string>(),
                new List<string>(),
                new ModelDetailsData(string.Empty, string.Empty, string.Empty),
                string.Empty,
                new GuardrailMetaData(0, new List<string>()),
                new CharacterTraitsData(new List<string>(), string.Empty, new PersonalityTraits(0, 0, 0, 0, 0)),
                string.Empty,
                string.Empty,
                string.Empty,
                new List<string>(),
                new List<string>(),
                new List<string>(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        [JsonProperty("memory_settings")] public MemorySettings MemorySettings { get; private set; }
        [JsonProperty("character_name")] public string CharacterName { get; private set; }
        [JsonProperty("user_id")] public string UserID { get; private set; }
        [JsonProperty("character_id")] public string CharacterID { get; private set; }
        [JsonProperty("listing")] public string Listing { get; private set; }
        [JsonProperty("language_codes")] public List<string> LanguageCodes { get; private set; }
        [JsonProperty("voice_type")] public string VoiceType { get; private set; }
        [JsonProperty("character_actions")] public List<string> CharacterActions { get; private set; }
        [JsonProperty("character_emotions")] public List<string> CharacterEmotions { get; private set; }
        [JsonProperty("model_details")] public ModelDetailsData ModelDetails { get; private set; }
        [JsonProperty("language_code")] public string LanguageCode { get; private set; }
        [JsonProperty("guardrail_meta")] public GuardrailMetaData GuardrailMeta { get; private set; }
        [JsonProperty("character_traits")] public CharacterTraitsData CharacterTraits { get; private set; }
        [JsonProperty("timestamp")] public string Timestamp { get; private set; }
        [JsonProperty("verbosity")] public int Verbosity { get; private set; }
        [JsonProperty("organization_id")] public string OrganizationId { get; private set; }

        [JsonProperty("is_narrative_driven")] public bool IsNarrativeDriven { get; private set; }

        [JsonProperty("start_narrative_section_id")]
        public string StartNarrativeSectionId { get; private set; }

        [JsonProperty("moderation_enabled")] public bool ModerationEnabled { get; private set; }
        [JsonProperty("pronunciations")] public object Pronunciations { get; private set; }
        [JsonProperty("boosted_words")] public List<string> BoostedWords { get; private set; }

        [JsonProperty("allowed_moderation_filters")]
        public List<string> AllowedModerationFilters { get; private set; }

        [JsonProperty("uncensored_access_consent")]
        public string UncensoredAccessConsent { get; private set; }

        [JsonProperty("nsfw_model_size")] public string NsfwModelSize { get; private set; }
        [JsonProperty("temperature")] public string Temperature { get; private set; }
        [JsonProperty("backstory")] public string Backstory { get; private set; }

        [JsonProperty("edit_character_access")]
        public bool EditCharacterAccess { get; private set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; private set; } = new Dictionary<string, JToken>();

        /// <summary>
        /// Resolves emotion configuration for WebRTC room connect requests.
        /// Priority:
        /// 1) Explicit config fields from backend payload.
        /// 2) Explicit boolean enable/disable fields from backend payload.
        /// 3) Legacy <c>character_emotions</c> list as enable signal.
        /// </summary>
        public bool TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig)
        {
            emotionConfig = null;

            if (TryResolveExplicitEmotionConfig(out string explicitProvider, out bool? explicitEnabled))
            {
                if (explicitEnabled.HasValue && !explicitEnabled.Value)
                {
                    return false;
                }

                emotionConfig = RoomEmotionConfig.Create(explicitProvider);
                return true;
            }

            if (TryResolveExplicitEmotionEnabled(out bool enabledFromFlag))
            {
                if (!enabledFromFlag)
                {
                    return false;
                }

                emotionConfig = RoomEmotionConfig.Create();
                return true;
            }

            if (CharacterEmotions != null && CharacterEmotions.Count > 0)
            {
                emotionConfig = RoomEmotionConfig.Create();
                return true;
            }

            return false;
        }

        private bool TryResolveExplicitEmotionConfig(out string provider, out bool? enabled)
        {
            provider = null;
            enabled = null;

            if (AdditionalData == null || AdditionalData.Count == 0)
            {
                return false;
            }

            if (TryGetTokenCaseInsensitive("emotion_config", out JToken emotionConfigToken) &&
                TryResolveEmotionConfigToken(emotionConfigToken, out provider, out enabled))
            {
                return true;
            }

            if (TryGetTokenCaseInsensitive("state_of_mind_config", out JToken stateOfMindConfigToken) &&
                TryResolveEmotionConfigToken(stateOfMindConfigToken, out provider, out enabled))
            {
                return true;
            }

            if (TryGetTokenCaseInsensitive("state_of_mind", out JToken stateOfMindToken) &&
                TryResolveEmotionConfigToken(stateOfMindToken, out provider, out enabled))
            {
                return true;
            }

            if (TryGetStringTokenCaseInsensitive("emotion_provider", out string directProvider) ||
                TryGetStringTokenCaseInsensitive("state_of_mind_provider", out directProvider))
            {
                provider = directProvider;
                enabled = null;
                return true;
            }

            return false;
        }

        private bool TryResolveExplicitEmotionEnabled(out bool enabled)
        {
            enabled = false;

            if (AdditionalData == null || AdditionalData.Count == 0)
            {
                return false;
            }

            string[] flagKeys =
            {
                "is_emotion_enabled",
                "emotion_enabled",
                "enable_emotion",
                "is_state_of_mind_enabled",
                "state_of_mind_enabled",
                "enable_state_of_mind"
            };

            foreach (string key in flagKeys)
            {
                if (!TryGetTokenCaseInsensitive(key, out JToken token))
                {
                    continue;
                }

                if (TryParseBooleanToken(token, out enabled))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveEmotionConfigToken(
            JToken token,
            out string provider,
            out bool? enabled)
        {
            provider = null;
            enabled = null;

            if (token == null || token.Type != JTokenType.Object)
            {
                return false;
            }

            JObject config = (JObject)token;

            if (TryGetStringPropertyCaseInsensitive(config, "provider", out string parsedProvider) ||
                TryGetStringPropertyCaseInsensitive(config, "emotion_provider", out parsedProvider) ||
                TryGetStringPropertyCaseInsensitive(config, "state_of_mind_provider", out parsedProvider))
            {
                provider = parsedProvider;
            }

            if (TryGetBooleanPropertyCaseInsensitive(config, "enabled", out bool parsedEnabled) ||
                TryGetBooleanPropertyCaseInsensitive(config, "is_enabled", out parsedEnabled) ||
                TryGetBooleanPropertyCaseInsensitive(config, "emotion_enabled", out parsedEnabled) ||
                TryGetBooleanPropertyCaseInsensitive(config, "is_emotion_enabled", out parsedEnabled) ||
                TryGetBooleanPropertyCaseInsensitive(config, "state_of_mind_enabled", out parsedEnabled) ||
                TryGetBooleanPropertyCaseInsensitive(config, "is_state_of_mind_enabled", out parsedEnabled))
            {
                enabled = parsedEnabled;
            }

            string normalizedProvider = RoomEmotionConfig.NormalizeProvider(provider);
            if (!string.IsNullOrEmpty(normalizedProvider))
            {
                provider = normalizedProvider;
            }
            else
            {
                provider = null;
            }

            return provider != null || enabled.HasValue;
        }

        private bool TryGetTokenCaseInsensitive(string key, out JToken token)
        {
            token = null;

            if (AdditionalData == null || AdditionalData.Count == 0)
            {
                return false;
            }

            foreach (KeyValuePair<string, JToken> pair in AdditionalData)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                token = pair.Value;
                return token != null;
            }

            return false;
        }

        private bool TryGetStringTokenCaseInsensitive(string key, out string value)
        {
            value = null;

            if (!TryGetTokenCaseInsensitive(key, out JToken token))
            {
                return false;
            }

            string stringValue = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            string normalized = RoomEmotionConfig.NormalizeProvider(stringValue);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            value = normalized;
            return true;
        }

        private static bool TryGetStringPropertyCaseInsensitive(JObject obj, string key, out string value)
        {
            value = null;
            if (obj == null)
            {
                return false;
            }

            foreach (JProperty property in obj.Properties())
            {
                if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string raw = property.Value.Type == JTokenType.String
                    ? property.Value.Value<string>()
                    : property.Value.ToString();
                string normalized = RoomEmotionConfig.NormalizeProvider(raw);
                if (string.IsNullOrEmpty(normalized))
                {
                    return false;
                }

                value = normalized;
                return true;
            }

            return false;
        }

        private static bool TryGetBooleanPropertyCaseInsensitive(JObject obj, string key, out bool value)
        {
            value = false;
            if (obj == null)
            {
                return false;
            }

            foreach (JProperty property in obj.Properties())
            {
                if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return TryParseBooleanToken(property.Value, out value);
            }

            return false;
        }

        private static bool TryParseBooleanToken(JToken token, out bool value)
        {
            value = false;
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }

            if (token.Type == JTokenType.Integer)
            {
                value = token.Value<int>() != 0;
                return true;
            }

            if (token.Type != JTokenType.String)
            {
                return false;
            }

            string raw = token.Value<string>();
            if (bool.TryParse(raw, out bool parsedBool))
            {
                value = parsedBool;
                return true;
            }

            if (int.TryParse(raw, out int parsedInt))
            {
                value = parsedInt != 0;
                return true;
            }

            return false;
        }

        [Serializable]
        public class ModelDetailsData
        {
            public ModelDetailsData(string modelType, string modelLink, string modelPlaceholder)
            {
                ModelType = modelType;
                ModelLink = modelLink;
                ModelPlaceholder = modelPlaceholder;
            }

            [JsonProperty("modelType")] public string ModelType { get; private set; }
            [JsonProperty("modelLink")] public string ModelLink { get; private set; }
            [JsonProperty("modelPlaceholder")] public string ModelPlaceholder { get; private set; }
        }

        [Serializable]
        public class GuardrailMetaData
        {
            public GuardrailMetaData(int limitResponseLevel, List<string> blockedWords)
            {
                LimitResponseLevel = limitResponseLevel;
                BlockedWords = blockedWords;
            }

            [JsonProperty("limitResponseLevel")] public int LimitResponseLevel { get; private set; }
            [JsonProperty("blockedWords")] public List<string> BlockedWords { get; private set; }
        }

        [Serializable]
        public class CharacterTraitsData
        {
            public CharacterTraitsData(List<string> catchPhrases, string speakingStyle, PersonalityTraits personalityTraits)
            {
                CatchPhrases = catchPhrases;
                SpeakingStyle = speakingStyle;
                PersonalityTraits = personalityTraits;
            }

            [JsonProperty("catch_phrases")] public List<string> CatchPhrases { get; private set; }
            [JsonProperty("speaking_style")] public string SpeakingStyle { get; private set; }
            [JsonProperty("personality_traits")] public PersonalityTraits PersonalityTraits { get; private set; }
        }

        [Serializable]
        public class PersonalityTraits
        {
            public PersonalityTraits(int openness, int sensitivity, int extraversion, int agreeableness, int meticulousness)
            {
                Openness = openness;
                Sensitivity = sensitivity;
                Extraversion = extraversion;
                Agreeableness = agreeableness;
                Meticulousness = meticulousness;
            }

            [JsonProperty("openness")] public int Openness { get; private set; }
            [JsonProperty("sensitivity")] public int Sensitivity { get; private set; }
            [JsonProperty("extraversion")] public int Extraversion { get; private set; }
            [JsonProperty("agreeableness")] public int Agreeableness { get; private set; }
            [JsonProperty("meticulousness")] public int Meticulousness { get; private set; }
        }
    }

}
