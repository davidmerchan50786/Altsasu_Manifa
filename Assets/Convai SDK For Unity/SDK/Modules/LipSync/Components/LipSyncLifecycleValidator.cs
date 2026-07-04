using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Shared.Interfaces;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncLifecycleValidator : ILipSyncLifecycleValidator
    {
        public LipSyncValidationResult ValidateProfile(LipSyncProfileId profileId)
        {
            if (LipSyncProfileCatalog.TryGetProfile(profileId, out _)) return LipSyncValidationResult.Valid();

            return LipSyncValidationResult.Invalid(
                LipSyncValidationFailure.ProfileMissing,
                $"[Convai LipSync] Profile '{profileId}' not found. Component disabled.");
        }

        public LipSyncValidationResult ValidateCharacterBinding(
            Component context,
            ref ICharacterIdentitySource identitySource,
            out string characterId)
        {
            identitySource ??= context != null ? context.GetComponent<ICharacterIdentitySource>() : null;
            if (identitySource == null)
            {
                characterId = string.Empty;
                return LipSyncValidationResult.Invalid(
                    LipSyncValidationFailure.CharacterBindingMissing,
                    "[Convai LipSync] No ICharacterIdentitySource found. Component disabled.");
            }

            characterId = identitySource?.CharacterId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(characterId)) return LipSyncValidationResult.Valid();

            return LipSyncValidationResult.Invalid(
                LipSyncValidationFailure.CharacterIdMissing,
                "[Convai LipSync] ICharacterIdentitySource found but CharacterId is empty. Component disabled.");
        }
    }
}
