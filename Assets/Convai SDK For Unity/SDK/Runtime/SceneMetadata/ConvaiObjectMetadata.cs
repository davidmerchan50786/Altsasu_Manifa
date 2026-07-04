using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.SceneMetadata
{
    /// <summary>
    ///     Component that stores metadata (name and description) for GameObjects.
    ///     Automatically registers with ConvaiMetadataRegistry for efficient collection.
    /// </summary>
    [AddComponentMenu("Convai/Object Metadata")]
    public class ConvaiObjectMetadata : MonoBehaviour
    {
        [Header("Object Metadata")]
        [Tooltip("Display name for this object that will be sent to the AI")]
        [SerializeField]
        private string _objectName = "";

        [Tooltip("Description of this object that will be sent to the AI")] [TextArea(2, 4)] [SerializeField]
        private string _objectDescription = "";

        [Header("Settings")]
        [Tooltip("Whether this object's metadata should be included in scene metadata collection")]
        [SerializeField]
        private bool _includeInMetadata = true;

        [Header("Debug Info")]
        [ReadOnly]
        [Tooltip("Shows the current registration status (read-only)")]
        [SerializeField]
        private bool _isRegistered;

        /// <summary>
        ///     The display name for this object
        /// </summary>
        public string ObjectName
        {
            get => _objectName;
            set => _objectName = value;
        }

        /// <summary>
        ///     The description for this object
        /// </summary>
        public string ObjectDescription
        {
            get => _objectDescription;
            set => _objectDescription = value;
        }

        /// <summary>
        ///     Whether this object should be included in metadata collection
        /// </summary>
        public bool IncludeInMetadata
        {
            get => _includeInMetadata;
            set => _includeInMetadata = value;
        }

        /// <summary>
        ///     Whether this component is currently registered with the metadata registry
        /// </summary>
        public bool IsRegistered => _isRegistered;

        /// <summary>
        ///     Whether this metadata is valid (has required fields)
        /// </summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(_objectName);

        private void OnEnable() => RegisterWithRegistry();

        private void OnDisable() => UnregisterFromRegistry();

        private void OnDestroy() => UnregisterFromRegistry();

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_objectName) && gameObject != null) _objectName = gameObject.name;

            List<string> errors = GetValidationErrors();
            if (errors.Count > 0)
            {
                ConvaiLogger.Warning(
                    $"[ConvaiObjectMetadata] Validation errors on {gameObject.name}: {string.Join(", ", errors)}",
                    LogCategory.SDK);
            }
        }

        private void RegisterWithRegistry()
        {
            if (!_isRegistered)
            {
                ConvaiMetadataRegistry.RegisterMetadata(this);
                _isRegistered = true;
            }
        }

        private void UnregisterFromRegistry()
        {
            if (_isRegistered)
            {
                ConvaiMetadataRegistry.UnregisterMetadata(this);
                _isRegistered = false;
            }
        }

        /// <summary>
        ///     Validates the metadata and returns any validation errors
        /// </summary>
        /// <returns>List of validation error messages, empty if valid</returns>
        public List<string> GetValidationErrors()
        {
            List<string> errors = new();

            if (string.IsNullOrWhiteSpace(_objectName)) errors.Add("Object Name is required");

            if (_objectName.Length > 50) errors.Add("Object Name should be 50 characters or less");

            if (_objectDescription.Length > 200) errors.Add("Object Description should be 200 characters or less");

            return errors;
        }

        /// <summary>
        ///     Creates a SceneMetadata object from this component's data
        /// </summary>
        /// <returns>SceneMetadata object for RTVI messaging</returns>
        public Infrastructure.Protocol.Messages.SceneMetadata ToSceneMetadata() =>
            new() { Name = _objectName, Description = _objectDescription };
    }

    /// <summary>
    ///     Custom attribute to make fields read-only in the inspector.
    ///     The PropertyDrawer (ReadOnlyDrawer) is defined in the Editor assembly.
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute
    {
    }
}
