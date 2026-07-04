using Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.UserAccountInformation;
using Convai.Runtime;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.APIKeySetup
{
    /// <summary>
    ///     UI logic for the Account section (API key setup and account info refresh).
    /// </summary>
    public class ConvaiAccountSectionLogic
    {
        private readonly AccountInformationUI _accountInformationUI;
        private readonly ConfigurationWindowContext _context;
        private readonly ConvaiAccountSection _ui;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiAccountSectionLogic" /> class.
        /// </summary>
        /// <param name="section">Account section UI instance.</param>
        /// <param name="accountInformationUI">Account information UI helper.</param>
        /// <param name="context">Shared window context.</param>
        public ConvaiAccountSectionLogic(
            ConvaiAccountSection section,
            AccountInformationUI accountInformationUI,
            ConfigurationWindowContext context)
        {
            _ui = section;
            _accountInformationUI = accountInformationUI;
            _context = context;

            bool hasApiKey = APIKeySetupLogic.LoadExistingApiKey(_ui.APIInputField, _ui.UpdateSaveButton);
            if (hasApiKey)
                _context?.NotifyApiKeyUpdated();
            else
                _context?.RefreshApiKeyAvailability(false);

            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            _ui.ShowHideAPIKeyButton.clicked += TogglePasswordVisibility;
            _ui.UpdateSaveButton.clicked += () => ClickEvent(_ui.APIInputField.value);
        }

        private void TogglePasswordVisibility()
        {
            _ui.APIInputField.isPasswordField = !_ui.APIInputField.isPasswordField;
            _ui.ShowHideAPIKeyButton.text = _ui.APIInputField.isPasswordField ? "Show" : "Hide";
        }

        /// <summary>
        ///     Called when the Account section becomes visible.
        /// </summary>
        public void OnSectionShown()
        {
            bool hasApiKey = _context != null
                ? _context.RefreshApiKeyAvailability(false)
                : ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            _accountInformationUI.GetUserAPIUsageData(hasApiKey);
        }

        private void ClickEvent(string apiKey) =>
            APIKeySetupLogic.BeginButtonTask(apiKey, isSuccessful =>
            {
                if (isSuccessful)
                {
                    _ui.APIInputField.isReadOnly = false;
                    _ui.UpdateSaveButton.text = "Update API Key";
                }

                _context?.NotifyApiKeyUpdated();
                _accountInformationUI.GetUserAPIUsageData(isSuccessful);
            });
    }
}
