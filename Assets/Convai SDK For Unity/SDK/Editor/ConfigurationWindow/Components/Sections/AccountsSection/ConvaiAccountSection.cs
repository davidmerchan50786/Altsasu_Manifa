using Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.APIKeySetup;
using Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.UserAccountInformation;
using Convai.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections
{
    /// <summary>
    ///     Account section of the Convai configuration window.
    ///     Displays account details, API key configuration, and usage statistics.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiAccountSection : ConvaiBaseSection
    {
        /// <summary>Unique identifier for this section in navigation.</summary>
        public const string SECTION_NAME = "account";

        private readonly AccountInformationUI _accountInformationUI;
        private readonly ConfigurationWindowContext _context;
        private readonly ConvaiAccountSectionLogic _logic;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiAccountSection" /> class.
        /// </summary>
        public ConvaiAccountSection() : this(null)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiAccountSection" /> class.
        /// </summary>
        /// <param name="context">Shared window context.</param>
        public ConvaiAccountSection(ConfigurationWindowContext context)
        {
            _context = context;
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("section-header", "Account Settings", "header"));

            VisualElement topRow = new() { name = "top-row" };
            topRow.AddToClassList("account-top-row");
            topRow.Add(CreateAccountDetailsCard());
            topRow.Add(CreateAPIKeyCard());
            Add(topRow);

            Add(CreateUsagesCard());

            _accountInformationUI = new AccountInformationUI(this, _context);
            _logic = new ConvaiAccountSectionLogic(this, _accountInformationUI, _context);

            RegisterCallback<DetachFromPanelEvent>(_ => _accountInformationUI.CancelPendingOperations());
        }

        /// <summary>Text field for entering/editing the API key.</summary>
        public TextField APIInputField { get; private set; }

        /// <summary>Button to toggle API key visibility.</summary>
        public Button ShowHideAPIKeyButton { get; private set; }

        /// <summary>Button to save or update the API key.</summary>
        public Button UpdateSaveButton { get; private set; }

        /// <summary>Label displaying the current plan name.</summary>
        public Label PlanName { get; private set; }

        /// <summary>Label displaying the plan expiry date.</summary>
        public Label PlanExpiry { get; private set; }

        /// <summary>Label displaying the quota renewal date.</summary>
        public Label QuotaRenewal { get; private set; }

        /// <summary>Usage bar for interaction quota.</summary>
        public UsageBarUI InteractionUsageUI { get; private set; }

        /// <summary>Usage bar for ElevenLabs TTS quota.</summary>
        public UsageBarUI ElevenlabsUsageUI { get; private set; }

        /// <summary>Usage bar for Core API quota.</summary>
        public UsageBarUI CoreApiUsageUI { get; private set; }

        /// <summary>Usage bar for Pixel Streaming quota.</summary>
        public UsageBarUI PixelStreamingUsageUI { get; private set; }

        /// <summary>Inline status text for usage load state.</summary>
        public Label UsageStatusLabel { get; private set; }

        /// <summary>Retry button for usage fetch failures.</summary>
        public Button UsageRetryButton { get; private set; }

        /// <summary>
        ///     Starts fetching account usage data before the section is visible,
        ///     so the data is ready when the user navigates to this section.
        /// </summary>
        public void PreWarmData()
        {
            bool hasApiKey = _context != null
                ? _context.RefreshApiKeyAvailability(false)
                : ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            _accountInformationUI.GetUserAPIUsageData(hasApiKey);
        }

        protected override void OnSectionShown() => _logic?.OnSectionShown();

        protected override void OnSectionHidden() => _accountInformationUI?.CancelPendingOperations();

        private VisualElement CreateAccountDetailsCard()
        {
            VisualElement card = new() { name = "account-details-card" };
            card.AddToClassList("card");
            card.AddToClassList("account-details-card");
            card.style.marginRight = 10;

            Label subheader =
                ConvaiVisualElementUtility.CreateLabel("account-details-header", "Account Details", "subheader");
            card.Add(subheader);

            VisualElement planRow = CreateLabelValueRow("Plan:", out Label planNameLabel, "-");
            PlanName = planNameLabel;
            card.Add(planRow);

            VisualElement expiryRow = CreateLabelValueRow("Plan Expiry:", out Label planExpiryLabel, "-");
            PlanExpiry = planExpiryLabel;
            card.Add(expiryRow);

            VisualElement renewalRow = CreateLabelValueRow("Quota Renewal:", out Label quotaRenewalLabel, "-");
            QuotaRenewal = quotaRenewalLabel;
            card.Add(renewalRow);

            return card;
        }

        private VisualElement CreateLabelValueRow(string labelText, out Label valueLabel, string defaultValue)
        {
            VisualElement row = new()
            {
                name = "label-value-row",
                style = { flexDirection = FlexDirection.Row, marginTop = 5, marginBottom = 5 }
            };

            Label label = ConvaiVisualElementUtility.CreateLabel("row-label", labelText, "label");
            label.style.minWidth = 110;
            label.style.flexShrink = 0;
            label.style.marginRight = 10;

            valueLabel = ConvaiVisualElementUtility.CreateLabel("row-value", defaultValue, "helper-text");
            valueLabel.style.flexGrow = 1;

            row.Add(label);
            row.Add(valueLabel);

            return row;
        }

        private VisualElement CreateAPIKeyCard()
        {
            VisualElement card = new() { name = "api-key-card" };
            card.AddToClassList("card");
            card.AddToClassList("account-api-key-card");

            Label subheader = ConvaiVisualElementUtility.CreateLabel("api-key-header", "API Key", "subheader");
            card.Add(subheader);

            VisualElement row = new() { name = "api-key-row" };
            row.AddToClassList("api-key-row");

            APIInputField = new TextField { isPasswordField = true, maskChar = '●' };
            APIInputField.AddToClassList("api-key-field");

            ShowHideAPIKeyButton = new Button { name = "show-hide-btn", text = "Show" };
            ShowHideAPIKeyButton.AddToClassList("button-small");
            ShowHideAPIKeyButton.style.flexShrink = 0;
            ShowHideAPIKeyButton.style.marginLeft = 5;

            row.Add(APIInputField);
            row.Add(ShowHideAPIKeyButton);
            card.Add(row);

            UpdateSaveButton = new Button { name = "save-update-button", text = "Save API Key" };
            ConvaiVisualElementUtility.AddStyles(UpdateSaveButton, "button", "btn-medium");
            UpdateSaveButton.style.alignSelf = Align.Center;
            UpdateSaveButton.style.marginTop = 10;
            card.Add(UpdateSaveButton);

            return card;
        }

        private VisualElement CreateUsagesCard()
        {
            VisualElement card = new() { name = "usages-card" };
            card.AddToClassList("card");

            Label subheader = ConvaiVisualElementUtility.CreateLabel("usages-header", "Usages", "subheader");
            card.Add(subheader);

            VisualElement statusRow = new()
            {
                name = "usage-status-row",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    marginBottom = 8
                }
            };

            UsageStatusLabel = ConvaiVisualElementUtility.CreateLabel(
                "usage-status-label",
                "Open this section to load account usage.",
                "helper-text");
            UsageStatusLabel.style.flexGrow = 1;

            UsageRetryButton = new Button(() => _accountInformationUI.GetUserAPIUsageData())
            {
                name = "usage-retry-button", text = "Retry"
            };
            UsageRetryButton.AddToClassList("button-small");
            UsageRetryButton.style.display = DisplayStyle.None;

            statusRow.Add(UsageStatusLabel);
            statusRow.Add(UsageRetryButton);
            card.Add(statusRow);

            InteractionUsageUI = new UsageBarUI("interaction-usage", "Interaction Usage");
            card.Add(InteractionUsageUI.Container);
            card.Add(ConvaiVisualElementUtility.CreateSpacer(4));

            ElevenlabsUsageUI = new UsageBarUI("elevenlabs-usage", "Elevenlabs Usage");
            card.Add(ElevenlabsUsageUI.Container);
            card.Add(ConvaiVisualElementUtility.CreateSpacer(4));

            CoreApiUsageUI = new UsageBarUI("core-api-usage", "Core API Usage");
            card.Add(CoreApiUsageUI.Container);
            card.Add(ConvaiVisualElementUtility.CreateSpacer(4));

            PixelStreamingUsageUI = new UsageBarUI("pixel-streaming-usage", "Pixel Streaming Usage");
            card.Add(PixelStreamingUsageUI.Container);

            return card;
        }

        /// <summary>
        ///     UI component for displaying usage statistics with a progress bar and label.
        /// </summary>
        public class UsageBarUI
        {
            /// <summary>Container element holding the entire usage bar UI.</summary>
            public readonly VisualElement Container;

            /// <summary>Progress bar showing usage percentage.</summary>
            public readonly ProgressBar ProgressBar;

            /// <summary>Label showing the current/limit usage text.</summary>
            public readonly Label UsageLabel;

            /// <summary>
            ///     Creates a new usage bar UI component.
            /// </summary>
            /// <param name="name">Element name prefix for generated elements.</param>
            /// <param name="title">Display title for the usage bar.</param>
            public UsageBarUI(string name, string title)
            {
                Container = new VisualElement { name = $"{name}-container" };

                Label header = ConvaiVisualElementUtility.CreateLabel($"{name}-title", title, "label");
                header.style.marginBottom = 2;

                VisualElement barRow = new()
                {
                    name = $"{name}-bar-row",
                    style = { flexDirection = FlexDirection.Row, alignItems = Align.Center }
                };

                ProgressBar = new ProgressBar { name = $"{name}-progress" };
                ProgressBar.style.flexGrow = 1;
                ConvaiVisualElementUtility.AddStyles(ProgressBar, "usage-bar");

                UsageLabel = ConvaiVisualElementUtility.CreateLabel($"{name}-literal", "0 / 0", "helper-text");
                UsageLabel.style.marginLeft = 10;
                UsageLabel.style.minWidth = 100;
                UsageLabel.style.unityTextAlign = TextAnchor.MiddleRight;

                barRow.Add(ProgressBar);
                barRow.Add(UsageLabel);

                Container.Add(header);
                Container.Add(barRow);
            }

            /// <summary>
            ///     Updates the usage bar with current and limit values.
            /// </summary>
            /// <param name="current">Current usage amount.</param>
            /// <param name="limit">Maximum usage limit.</param>
            public void SetUsage(float current, float limit)
            {
                float percentage = limit > 0 ? current / limit * 100f : 0f;
                ProgressBar.value = percentage;
                UsageLabel.text = $"{current:N0} / {limit:N0}";
            }
        }
    }
}
